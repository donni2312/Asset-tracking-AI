using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace AssetIQ.Api.Auth;

/// <summary>
/// scrypt (RFC 7914). The password hashes in the users table were made with Node's
/// <c>crypto.scrypt</c> defaults (N = 16384, r = 8, p = 1); .NET has no built-in scrypt, and this
/// avoids a third-party package for a security-critical primitive. Verified against the RFC test
/// vectors and against hashes produced by Node (see ScryptTests).
/// </summary>
public static class Scrypt
{
    public const int DefaultCost = 16384;
    public const int DefaultBlockSize = 8;
    public const int DefaultParallelization = 1;

    public static byte[] DeriveKey(byte[] password, byte[] salt, int keyLength,
        int cost = DefaultCost, int blockSize = DefaultBlockSize, int parallelization = DefaultParallelization)
    {
        if (cost < 2 || !BitOperations.IsPow2(cost)) throw new ArgumentOutOfRangeException(nameof(cost), "N must be a power of two greater than 1");
        if (blockSize < 1) throw new ArgumentOutOfRangeException(nameof(blockSize));
        if (parallelization < 1) throw new ArgumentOutOfRangeException(nameof(parallelization));
        if (keyLength < 1) throw new ArgumentOutOfRangeException(nameof(keyLength));

        var blockBytes = 128 * blockSize;
        var b = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, parallelization * blockBytes);

        var words = 32 * blockSize;
        var x = new uint[words];
        var y = new uint[words];
        var v = new uint[words * cost];
        var scratch = new uint[16];

        for (var i = 0; i < parallelization; i++)
        {
            var chunk = b.AsSpan(i * blockBytes, blockBytes);
            for (var k = 0; k < words; k++) x[k] = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(k * 4)..]);
            RoMix(x, y, v, scratch, cost, blockSize);
            for (var k = 0; k < words; k++) BinaryPrimitives.WriteUInt32LittleEndian(chunk[(k * 4)..], x[k]);
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(password, b, 1, HashAlgorithmName.SHA256, keyLength);
        CryptographicOperations.ZeroMemory(b);
        Array.Clear(v);
        return key;
    }

    private static void RoMix(uint[] x, uint[] y, uint[] v, uint[] scratch, int cost, int blockSize)
    {
        var words = 32 * blockSize;
        for (var i = 0; i < cost; i++)
        {
            Array.Copy(x, 0, v, i * words, words);
            BlockMix(x, y, scratch, blockSize);
        }
        for (var i = 0; i < cost; i++)
        {
            var j = (int)(x[(2 * blockSize - 1) * 16] & (uint)(cost - 1));
            var offset = j * words;
            for (var k = 0; k < words; k++) x[k] ^= v[offset + k];
            BlockMix(x, y, scratch, blockSize);
        }
    }

    /// <summary>scryptBlockMix: B' = (Y0, Y2, …, Y2r-2, Y1, Y3, …, Y2r-1), written back into <paramref name="b"/>.</summary>
    private static void BlockMix(uint[] b, uint[] y, uint[] t, int blockSize)
    {
        Array.Copy(b, (2 * blockSize - 1) * 16, t, 0, 16);
        for (var i = 0; i < 2 * blockSize; i++)
        {
            for (var k = 0; k < 16; k++) t[k] ^= b[i * 16 + k];
            Salsa208(t);
            var target = (i % 2 == 0 ? i / 2 : blockSize + i / 2) * 16;
            Array.Copy(t, 0, y, target, 16);
        }
        Array.Copy(y, b, 32 * blockSize);
    }

    private static void Salsa208(uint[] block)
    {
        uint x0 = block[0], x1 = block[1], x2 = block[2], x3 = block[3],
             x4 = block[4], x5 = block[5], x6 = block[6], x7 = block[7],
             x8 = block[8], x9 = block[9], x10 = block[10], x11 = block[11],
             x12 = block[12], x13 = block[13], x14 = block[14], x15 = block[15];

        for (var round = 0; round < 8; round += 2)
        {
            // Columns
            x4 ^= BitOperations.RotateLeft(x0 + x12, 7);
            x8 ^= BitOperations.RotateLeft(x4 + x0, 9);
            x12 ^= BitOperations.RotateLeft(x8 + x4, 13);
            x0 ^= BitOperations.RotateLeft(x12 + x8, 18);
            x9 ^= BitOperations.RotateLeft(x5 + x1, 7);
            x13 ^= BitOperations.RotateLeft(x9 + x5, 9);
            x1 ^= BitOperations.RotateLeft(x13 + x9, 13);
            x5 ^= BitOperations.RotateLeft(x1 + x13, 18);
            x14 ^= BitOperations.RotateLeft(x10 + x6, 7);
            x2 ^= BitOperations.RotateLeft(x14 + x10, 9);
            x6 ^= BitOperations.RotateLeft(x2 + x14, 13);
            x10 ^= BitOperations.RotateLeft(x6 + x2, 18);
            x3 ^= BitOperations.RotateLeft(x15 + x11, 7);
            x7 ^= BitOperations.RotateLeft(x3 + x15, 9);
            x11 ^= BitOperations.RotateLeft(x7 + x3, 13);
            x15 ^= BitOperations.RotateLeft(x11 + x7, 18);
            // Rows
            x1 ^= BitOperations.RotateLeft(x0 + x3, 7);
            x2 ^= BitOperations.RotateLeft(x1 + x0, 9);
            x3 ^= BitOperations.RotateLeft(x2 + x1, 13);
            x0 ^= BitOperations.RotateLeft(x3 + x2, 18);
            x6 ^= BitOperations.RotateLeft(x5 + x4, 7);
            x7 ^= BitOperations.RotateLeft(x6 + x5, 9);
            x4 ^= BitOperations.RotateLeft(x7 + x6, 13);
            x5 ^= BitOperations.RotateLeft(x4 + x7, 18);
            x11 ^= BitOperations.RotateLeft(x10 + x9, 7);
            x8 ^= BitOperations.RotateLeft(x11 + x10, 9);
            x9 ^= BitOperations.RotateLeft(x8 + x11, 13);
            x10 ^= BitOperations.RotateLeft(x9 + x8, 18);
            x12 ^= BitOperations.RotateLeft(x15 + x14, 7);
            x13 ^= BitOperations.RotateLeft(x12 + x15, 9);
            x14 ^= BitOperations.RotateLeft(x13 + x12, 13);
            x15 ^= BitOperations.RotateLeft(x14 + x13, 18);
        }

        block[0] += x0; block[1] += x1; block[2] += x2; block[3] += x3;
        block[4] += x4; block[5] += x5; block[6] += x6; block[7] += x7;
        block[8] += x8; block[9] += x9; block[10] += x10; block[11] += x11;
        block[12] += x12; block[13] += x13; block[14] += x14; block[15] += x15;
    }
}
