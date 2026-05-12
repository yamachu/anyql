using System.Security.Cryptography;
using System.Text;

namespace AnyQL.Postgres.Protocol;

/// <summary>
/// Computes PostgreSQL MD5 password hashes.
/// Format: md5(md5(password + user) + salt)  → "md5" + hex
/// </summary>
internal static class PgMd5Auth
{
    public static string Compute(string password, string user, ReadOnlySpan<byte> salt)
    {
        // Step 1: md5(password + user)
        byte[] inner = Encoding.UTF8.GetBytes(password + user);
        byte[] step1 = MD5.HashData(inner);
        string step1Hex = Convert.ToHexString(step1).ToLowerInvariant();

        // Step 2: md5(step1Hex + salt)
        byte[] saltedInput = Encoding.ASCII.GetBytes(step1Hex).Concat(salt.ToArray()).ToArray();
        byte[] step2 = MD5.HashData(saltedInput);
        string step2Hex = Convert.ToHexString(step2).ToLowerInvariant();

        return "md5" + step2Hex;
    }
}
