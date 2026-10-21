using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Sign-in and the permission matrix on asset records (ported from src/routes/auth.test.ts):
/// viewer reads; operator may also change an asset's operational fields (status, holder, location,
/// notes); admin may also change the record itself and register / delete assets.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture api) : IAsyncLifetime
{
    private TestWorld w = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        w = await TestWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<UserAccount> CreateUser(string username, string role, string password = "secret123", bool active = true)
    {
        await using var db = api.NewDbContext();
        var user = new UserAccount
        {
            Username = username, Name = $"{username} user", Role = role, PasswordHash = new PasswordHasher().Hash(password),
            Active = active, CreatedAt = api.Now, UpdatedAt = api.Now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<string> TokenFor(string username, string password = "secret123")
    {
        var res = await api.CallAsync("POST", "/auth/login", new { username, password });
        Assert.Equal(200, res.Status);
        return res.Body.Str("token")!;
    }

    private async Task SetUser(int id, string? role = null, bool? active = null)
    {
        await using var db = api.NewDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        if (role is not null) user.Role = role;
        if (active is not null) user.Active = active.Value;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Login_returns_a_token_and_the_user_for_the_right_password()
    {
        await CreateUser("rudo", "admin");
        var res = await api.CallAsync("POST", "/auth/login", new { username = "rudo", password = "secret123" });

        Assert.Equal(200, res.Status);
        Assert.False(string.IsNullOrEmpty(res.Body.Str("token")));
        Assert.Equal(("rudo", "admin"), (res.Body.Prop("user").Str("username"), res.Body.Prop("user").Str("role")));
        Assert.True(DateTimeOffset.Parse(res.Body.Str("expiresAt")!, System.Globalization.CultureInfo.InvariantCulture) > api.Clock.GetUtcNow());
        // The hash must never travel to the client.
        Assert.DoesNotContain("scrypt$", res.Body.GetRawText());
    }

    [Fact]
    public async Task Login_records_the_sign_in_time()
    {
        var user = await CreateUser("rudo", "admin");
        Assert.Null(user.LastLoginAt);
        await TokenFor("rudo");

        await using var db = api.NewDbContext();
        Assert.NotNull((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).LastLoginAt);
    }

    [Fact]
    public async Task A_wrong_password_is_answered_exactly_like_an_unknown_user()
    {
        await CreateUser("rudo", "admin");
        var wrongPassword = await api.CallAsync("POST", "/auth/login", new { username = "rudo", password = "nope" });
        var unknownUser = await api.CallAsync("POST", "/auth/login", new { username = "ghost", password = "nope" });

        Assert.Equal(401, wrongPassword.Status);
        Assert.Equal(401, unknownUser.Status);
        Assert.Equal(wrongPassword.Error(), unknownUser.Error());
    }

    [Fact]
    public async Task Login_refuses_a_disabled_account()
    {
        await CreateUser("gone", "admin", "secret123", active: false);
        Assert.Equal(401, (await api.CallAsync("POST", "/auth/login", new { username = "gone", password = "secret123" })).Status);
    }

    [Fact]
    public async Task Login_is_case_insensitive_about_the_username()
    {
        await CreateUser("rudo", "viewer");
        Assert.Equal(200, (await api.CallAsync("POST", "/auth/login", new { username = "  RUDO  ", password = "secret123" })).Status);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        await CreateUser("rudo", "operator");
        var res = await api.CallAsync("GET", "/auth/me", token: await TokenFor("rudo"));
        Assert.Equal(200, res.Status);
        Assert.Equal(("rudo", "operator"), (res.Body.Str("username"), res.Body.Str("role")));
    }

    [Fact]
    public async Task Me_rejects_a_missing_forged_or_expired_token()
    {
        var user = await CreateUser("rudo", "operator");
        var nowSeconds = api.Clock.GetUtcNow().ToUnixTimeSeconds();
        var forged = TokenService.Sign(new TokenPayload(user.Id, "rudo", "admin", nowSeconds + 3600), "the-wrong-secret");
        var expired = TokenService.Sign(new TokenPayload(user.Id, "rudo", "admin", nowSeconds - 60), ApiFixture.AuthSecret);

        Assert.Equal(401, (await api.CallAsync("GET", "/auth/me", token: ApiFixture.Anonymous)).Status);
        Assert.Equal(401, (await api.CallAsync("GET", "/auth/me", token: "not.a.token")).Status);
        Assert.Equal(401, (await api.CallAsync("GET", "/auth/me", token: forged)).Status);
        Assert.Equal(401, (await api.CallAsync("GET", "/auth/me", token: expired)).Status);
    }

    [Fact]
    public async Task An_edit_with_no_token_is_refused_with_401_not_403()
    {
        Assert.Equal(401, (await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}", new { name = "Renamed" }, ApiFixture.Anonymous)).Status);
    }

    [Fact]
    public async Task A_viewer_is_refused_with_403_and_the_asset_is_untouched()
    {
        await CreateUser("nyasha", "viewer");
        var res = await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}", new { status = "missing" }, await TokenFor("nyasha"));

        Assert.Equal(403, res.Status);
        Assert.Equal("active", (await api.CallAsync("GET", $"/assets/{w.Laptop.Id}")).Body.Str("status"));
    }

    [Fact]
    public async Task An_operator_may_change_the_operational_fields()
    {
        await CreateUser("tendai", "operator");
        var res = await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}",
            new { status = "maintenance", assignedTo = "Rudo Chikwanha", notes = "In for a battery" }, await TokenFor("tendai"));

        Assert.Equal(200, res.Status);
        Assert.Equal(("maintenance", "Rudo Chikwanha"), (res.Body.Str("status"), res.Body.Str("assignedTo")));
    }

    [Fact]
    public async Task An_operator_may_edit_the_asset_record_itself()
    {
        await CreateUser("tendai", "operator");
        var res = await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}",
            new { name = "Dell Latitude 5450", category = "Laptop", sbuId = w.Bak.Id, status = "maintenance" }, await TokenFor("tendai"));

        Assert.Equal(200, res.Status);
        Assert.Equal(("Dell Latitude 5450", w.Bak.Id, "maintenance"), (res.Body.Str("name"), res.Body.Int("sbuId"), res.Body.Str("status")));
    }

    [Fact]
    public async Task A_viewer_changes_nothing_on_the_asset_and_is_told_which_role_is_needed()
    {
        await CreateUser("nyasha", "viewer");
        var res = await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}", new { status = "maintenance", sbuId = w.Bak.Id }, await TokenFor("nyasha"));

        Assert.Equal(403, res.Status);
        Assert.Equal("This action needs the operator role; you are signed in as viewer.", res.Error());
        var after = await api.CallAsync("GET", $"/assets/{w.Laptop.Id}");
        Assert.Equal(("active", w.Tsas.Id), (after.Body.Str("status"), after.Body.Int("sbuId"))); // nothing was applied
    }

    [Fact]
    public async Task An_admin_may_edit_the_whole_record()
    {
        await CreateUser("rudo", "admin");
        var res = await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}",
            new { name = "Dell Latitude 5450", category = "Laptop", sbuId = w.Bak.Id, status = "active" }, await TokenFor("rudo"));

        Assert.Equal(200, res.Status);
        Assert.Equal(("Dell Latitude 5450", w.Bak.Id), (res.Body.Str("name"), res.Body.Int("sbuId")));
    }

    [Fact]
    public async Task Operators_and_admins_may_register_or_delete_an_asset_viewers_may_not()
    {
        await CreateUser("nyasha", "viewer");
        await CreateUser("tendai", "operator");
        var viewerToken = await TokenFor("nyasha");
        var operatorToken = await TokenFor("tendai");
        var asset = new { name = "HP EliteBook 840", category = "Laptop", serialNumber = "SN-HP-900", rfidTag = "TAG-HP-900", sbuId = w.Tsas.Id };

        Assert.Equal(401, (await api.CallAsync("POST", "/assets", asset, ApiFixture.Anonymous)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/assets", asset, viewerToken)).Status);

        var created = await api.CallAsync("POST", "/assets", asset, operatorToken);
        Assert.Equal(201, created.Status);

        Assert.Equal(403, (await api.CallAsync("DELETE", $"/assets/{created.Body.Int("id")}", token: viewerToken)).Status);
        Assert.Equal(204, (await api.CallAsync("DELETE", $"/assets/{created.Body.Int("id")}", token: operatorToken)).Status);
    }

    [Fact]
    public async Task A_demotion_applies_to_the_next_request_without_waiting_for_the_token_to_expire()
    {
        var user = await CreateUser("rudo", "admin");
        var token = await TokenFor("rudo");
        Assert.Equal(200, (await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}", new { name = "Renamed" }, token)).Status);

        await SetUser(user.Id, role: "viewer");
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}", new { name = "Renamed again" }, token)).Status);
    }

    [Fact]
    public async Task A_disabled_account_is_stopped_dead_token_or_not()
    {
        var user = await CreateUser("rudo", "admin");
        var token = await TokenFor("rudo");

        await SetUser(user.Id, active: false);
        Assert.Equal(401, (await api.CallAsync("PATCH", $"/assets/{w.Laptop.Id}", new { name = "Renamed" }, token)).Status);
    }

    [Fact]
    public async Task Reading_stays_open_so_the_dashboard_works_signed_out()
    {
        Assert.Equal(200, (await api.CallAsync("GET", "/assets", token: ApiFixture.Anonymous)).Status);
        Assert.Equal(200, (await api.CallAsync("GET", $"/assets/{w.Laptop.Id}", token: ApiFixture.Anonymous)).Status);
    }

    [Fact]
    public async Task A_session_signed_in_on_the_node_api_keeps_working()
    {
        // Same AUTH_SECRET, same token format: browsers signed in before the switch stay signed in.
        var user = await CreateUser("rudo", "operator");
        var exp = api.Clock.GetUtcNow().ToUnixTimeSeconds() + 3600;
        var nodeStyleToken = TokenService.Sign(new TokenPayload(user.Id, "rudo", "operator", exp), ApiFixture.AuthSecret);

        var me = await api.CallAsync("GET", "/auth/me", token: nodeStyleToken);
        Assert.Equal((200, "rudo"), (me.Status, me.Body.Str("username")));
    }
}
