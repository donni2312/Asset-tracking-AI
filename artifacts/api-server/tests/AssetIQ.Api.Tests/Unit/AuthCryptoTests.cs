using System.Text;
using AssetIQ.Api.Auth;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>
/// The password hashes in the users table and the tokens in browsers' localStorage were produced by the
/// Node API; these vectors were generated with its node:crypto code, so the C# API must reproduce them
/// byte for byte.
/// </summary>
public sealed class AuthCryptoTests
{
    private const string TestSecret = "test-secret-do-not-use-in-production";

    [Theory]
    // RFC 7914 §12 test vectors (also what Node's crypto.scryptSync returns).
    [InlineData("", "", 16, 1, 1,
        "77d6576238657b203b19ca42c18a0497f16b4844e3074ae8dfdffa3fede21442fcd0069ded0948f8326a753a0fc81f17e8d3e0fb2e0d3628cf35e20c38d18906")]
    [InlineData("password", "NaCl", 1024, 8, 16,
        "fdbabe1c9d3472007856e7190d01e9fe7c6ad7cbc8237830e77376634b3731622eaf30d92e22a3886ff109279d9830dac727afb94a83ee6d8360cbdfa2cc0640")]
    [InlineData("pleaseletmein", "SodiumChloride", 16384, 8, 1,
        "7023bdcb3afd7348461c06cd81fd38ebfda8fbba904f8e3ea9b543f6545da1f2d5432955613f0fcf62d49705242a9af9e61e85dc0d651e40dfcf017b45575887")]
    public void Scrypt_matches_the_rfc_7914_test_vectors(string password, string salt, int n, int r, int p, string expectedHex)
    {
        var key = Scrypt.DeriveKey(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), 64, n, r, p);
        Assert.Equal(expectedHex, Convert.ToHexString(key).ToLowerInvariant());
    }

    [Fact]
    public void Scrypt_with_nodes_defaults_matches_node()
    {
        var key = Scrypt.DeriveKey(Encoding.UTF8.GetBytes("admin123"), Convert.FromHexString("00112233445566778899aabbccddeeff"), 64);
        Assert.Equal(
            "443ae9cf636476bf6847f00a96ba9f9e6055cb8ac4d45f79f3b09ce55035d5a45e8fd48f379dcab83faa8674e28a3ff4b1d8b99a5ad5bcce09b7fe83ffd14d0d",
            Convert.ToHexString(key).ToLowerInvariant());
    }

    [Fact]
    public void Verifies_a_password_hash_made_by_the_node_api()
    {
        const string nodeHash = "scrypt$571aef634057418b09b927936d97b778$185e19f0321780f4478be066790e1649b95d0c39b2765814c19555066796b90b31ab6a5d24818092c4172458c0ecae1da6e2db9924192e45f96de6d8d6af4852";
        var hasher = new PasswordHasher();
        Assert.True(hasher.Verify("operator123", nodeHash));
        Assert.False(hasher.Verify("nope", nodeHash));
    }

    [Fact]
    public void Hashes_in_the_same_self_describing_format_with_a_fresh_salt()
    {
        var hasher = new PasswordHasher();
        var first = hasher.Hash("secret123");
        var second = hasher.Hash("secret123");
        var parts = first.Split('$');
        Assert.Equal(("scrypt", 32, 128), (parts[0], parts[1].Length, parts[2].Length));
        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("secret123", first));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text")]
    [InlineData("bcrypt$aa$bb")]
    [InlineData("scrypt$zz$00")]
    [InlineData("scrypt$00$")]
    public void Rejects_malformed_hashes(string stored)
    {
        Assert.False(new PasswordHasher().Verify("anything", stored));
    }

    [Fact]
    public void The_dummy_hash_takes_the_full_scrypt_cost_and_never_matches()
    {
        Assert.False(new PasswordHasher().Verify("anything", PasswordHasher.DummyHash));
    }

    [Fact]
    public void Signs_tokens_byte_for_byte_like_the_node_api()
    {
        const string nodeToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOjcsInVzZXJuYW1lIjoicnVkbyIsInJvbGUiOiJhZG1pbiIsImV4cCI6NDEwMjQ0NDgwMH0.PrEyt5DG9lB-XGI4X_Da58bnpRN-V56DJOoKy0KuMz0";
        Assert.Equal(nodeToken, TokenService.Sign(new TokenPayload(7, "rudo", "admin", 4102444800), TestSecret));
    }

    [Fact]
    public void Verifies_tokens_issued_by_the_node_api_including_non_ascii_secrets()
    {
        var now = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
        const string nodeToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOjcsInVzZXJuYW1lIjoicnVkbyIsInJvbGUiOiJhZG1pbiIsImV4cCI6NDEwMjQ0NDgwMH0.PrEyt5DG9lB-XGI4X_Da58bnpRN-V56DJOoKy0KuMz0";
        Assert.Equal(new TokenPayload(7, "rudo", "admin", 4102444800), TokenService.Verify(nodeToken, TestSecret, now));

        const string unicodeSecretToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOjgsInVzZXJuYW1lIjoidGVuZGFpIiwicm9sZSI6Im9wZXJhdG9yIiwiZXhwIjo0MTAyNDQ0ODAwfQ.2XRHKnqQ3bnoauEVc3PDAknUA3HH1PlCm1d1ulf2zXw";
        Assert.Equal(8, TokenService.Verify(unicodeSecretToken, "sécret-ünïcode-key-1234", now)?.Sub);
    }

    [Fact]
    public void Rejects_forged_expired_and_malformed_tokens()
    {
        var now = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
        var nowSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();
        var valid = TokenService.Sign(new TokenPayload(1, "rudo", "admin", nowSeconds + 3600), TestSecret);
        Assert.NotNull(TokenService.Verify(valid, TestSecret, now));

        Assert.Null(TokenService.Verify(TokenService.Sign(new TokenPayload(1, "rudo", "admin", nowSeconds + 3600), "the-wrong-secret"), TestSecret, now));
        Assert.Null(TokenService.Verify(TokenService.Sign(new TokenPayload(1, "rudo", "admin", nowSeconds - 60), TestSecret), TestSecret, now));
        Assert.Null(TokenService.Verify(TokenService.Sign(new TokenPayload(1, "rudo", "admin", nowSeconds), TestSecret), TestSecret, now));
        Assert.Null(TokenService.Verify("not.a.token", TestSecret, now));
        Assert.Null(TokenService.Verify("only-one-part", TestSecret, now));
        Assert.Null(TokenService.Verify(valid + "x", TestSecret, now));

        var parts = valid.Split('.');
        var tamperedBody = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":2,\"username\":\"rudo\",\"role\":\"admin\",\"exp\":9999999999}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(TokenService.Verify($"{parts[0]}.{tamperedBody}.{parts[2]}", TestSecret, now));
    }
}
