using System.Security.Cryptography;
using System.Text;

namespace AnyQL.MySql.Protocol;

/// <summary>
/// Implements MySQL's caching_sha2_password authentication (fast auth path only).
/// Fast path: XOR(SHA256(password), SHA256(SHA256(SHA256(password)) + scramble))
/// If the server requires full auth (0x04), we send the password in cleartext
/// over the connection (acceptable for localhost/trusted networks without TLS in MVP).
/// </summary>
internal static class MySqlCachingSha2Auth
{
    /// <summary>Computes the fast-path auth response.</summary>
    public static byte[] ComputeFastAuthResponse(string password, byte[] scramble)
    {
        if (string.IsNullOrEmpty(password)) return [];

        byte[] pwBytes = Encoding.UTF8.GetBytes(password);
        byte[] sha1 = SHA256.HashData(pwBytes);
        byte[] sha2 = SHA256.HashData(sha1);

        byte[] combined = [.. sha2, .. scramble];
        byte[] sha3 = SHA256.HashData(combined);

        byte[] result = new byte[sha1.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = (byte)(sha1[i] ^ sha3[i]);
        return result;
    }

    /// <summary>Cleartext password for full auth (no TLS — MVP only).</summary>
    public static byte[] CleartextPassword(string password)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        // Null-terminated
        return [.. bytes, 0];
    }
}
