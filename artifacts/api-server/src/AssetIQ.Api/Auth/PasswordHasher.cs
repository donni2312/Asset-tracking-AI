using System.Security.Cryptography;
using System.Text;

namespace AssetIQ.Api.Auth;

/// <summary>
/// Password hashes are self-describing: <c>scrypt$&lt;salt-hex&gt;$&lt;hash-hex&gt;</c>, a per-password
/// random salt and a 64-byte scrypt key — the format scripts/src/seed.ts writes.
/// </summary>
public sealed class PasswordHasher
{
    private const int KeyLength = 64;

    /// <summary>
    /// Verified when the username is unknown, so a wrong username takes as long to reject as a
    /// wrong password and the endpoint does not reveal which accounts exist.
    /// </summary>
    public const string DummyHash = "scrypt$00$00";

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Scrypt.DeriveKey(Encoding.UTF8.GetBytes(password), salt, KeyLength);
        return $"scrypt${Convert.ToHexString(salt).ToLowerInvariant()}${Convert.ToHexString(derived).ToLowerInvariant()}";
    }

    /// <summary>Constant-time comparison; false for any malformed hash.</summary>
    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length < 3 || parts[0] != "scrypt" || parts[1].Length == 0 || parts[2].Length == 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromHexString(parts[1]);
            expected = Convert.FromHexString(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (expected.Length == 0) return false;

        var derived = Scrypt.DeriveKey(Encoding.UTF8.GetBytes(password), salt, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, derived);
    }
}
