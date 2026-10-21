using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Auth;

/// <param name="Sub">User id.</param>
/// <param name="Username">Username at sign-in (informational; the role is re-read from the database).</param>
/// <param name="Role">Role at sign-in (informational).</param>
/// <param name="Exp">Expiry, seconds since the epoch.</param>
public sealed record TokenPayload(int Sub, string Username, string Role, long Exp);

/// <summary>Compact HS256 JWTs signed with AUTH_SECRET, in the same format the Node API issued.</summary>
public sealed class TokenService(AppConfig config, AppClock clock)
{
    private const string Header = """{"alg":"HS256","typ":"JWT"}""";

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string Sign(TokenPayload payload) => Sign(payload, config.Auth.Secret);

    /// <summary>The payload, or null when the token is malformed, forged or expired.</summary>
    public TokenPayload? Verify(string token) => Verify(token, config.Auth.Secret, clock.UtcNow);

    public static string Sign(TokenPayload payload, string secret)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes(Header));
        var body = Base64Url(SerializePayload(payload));
        return $"{header}.{body}.{Signature($"{header}.{body}", secret)}";
    }

    public static TokenPayload? Verify(string token, string secret, DateTime now)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;

        var expected = FromBase64Url(Signature($"{parts[0]}.{parts[1]}", secret))!;
        var actual = FromBase64Url(parts[2]);
        if (actual is null || expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual)) return null;

        var body = FromBase64Url(parts[1]);
        if (body is null) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number || !exp.TryGetDouble(out var expSeconds)) return null;
            var nowUtc = now.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(now, DateTimeKind.Utc) : now.ToUniversalTime();
            if (expSeconds * 1000 <= new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds()) return null;
            if (!root.TryGetProperty("sub", out var sub) || sub.ValueKind != JsonValueKind.Number || !sub.TryGetInt32(out var userId)) return null;
            return new TokenPayload(userId, Text(root, "username"), Text(root, "role"), (long)Math.Floor(expSeconds));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[] SerializePayload(TokenPayload payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sub", payload.Sub);
            writer.WriteString("username", payload.Username);
            writer.WriteString("role", payload.Role);
            writer.WriteNumber("exp", payload.Exp);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static string Signature(string data, string secret) =>
        Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[]? FromBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        var buffer = new byte[base64.Length * 3 / 4];
        return Convert.TryFromBase64String(base64, buffer, out var written) ? buffer[..written] : null;
    }
}
