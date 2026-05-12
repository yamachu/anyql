using System.Security.Cryptography;
using System.Text;

namespace AnyQL.Postgres.Protocol;

/// <summary>
/// Implements SCRAM-SHA-256 client-side authentication as required by PostgreSQL.
/// RFC 5802 / RFC 7677 compliant minimal implementation.
/// </summary>
internal sealed class PgScramSha256Auth
{
    private const string MechanismName = "SCRAM-SHA-256";

    private readonly string _user;
    private readonly string _password;
    private string? _clientNonce;
    private string? _clientFirstMessageBare;

    public PgScramSha256Auth(string user, string password)
    {
        _user = user;
        _password = password;
    }

    /// <summary>Builds the initial SASL client-first message.</summary>
    public (string mechanism, byte[] initialResponse) BuildClientFirstMessage()
    {
        _clientNonce = GenerateNonce();
        _clientFirstMessageBare = $"n={_user},r={_clientNonce}";
        // gs2-header: "n,," (no channel binding, no authzid)
        string clientFirstMessage = "n,," + _clientFirstMessageBare;
        return (MechanismName, Encoding.UTF8.GetBytes(clientFirstMessage));
    }

    /// <summary>
    /// Processes the server-first message and returns the client-final message.
    /// </summary>
    public byte[] BuildClientFinalMessage(byte[] serverFirstBytes)
    {
        string serverFirst = Encoding.UTF8.GetString(serverFirstBytes);
        var parts = ParseAttributes(serverFirst);

        string serverNonce = parts["r"];
        string serverSalt = parts["s"];
        int iterations = int.Parse(parts["i"]);

        if (!serverNonce.StartsWith(_clientNonce!, StringComparison.Ordinal))
            throw new InvalidOperationException("SCRAM: server nonce does not start with client nonce.");

        byte[] salt = Convert.FromBase64String(serverSalt);
        byte[] saltedPassword = Hi(NormalizePassword(_password), salt, iterations);

        byte[] clientKey = HMACSHA256(saltedPassword, "Client Key"u8.ToArray());
        byte[] storedKey = SHA256.HashData(clientKey);

        // channel-binding: base64("n,,")
        string cbind = Convert.ToBase64String("n,,"u8.ToArray());
        string clientFinalMessageWithoutProof = $"c={cbind},r={serverNonce}";

        string authMessage = _clientFirstMessageBare + "," + serverFirst + "," + clientFinalMessageWithoutProof;
        byte[] clientSignature = HMACSHA256(storedKey, Encoding.UTF8.GetBytes(authMessage));

        byte[] clientProof = new byte[clientKey.Length];
        for (int i = 0; i < clientKey.Length; i++)
            clientProof[i] = (byte)(clientKey[i] ^ clientSignature[i]);

        string clientFinal = $"{clientFinalMessageWithoutProof},p={Convert.ToBase64String(clientProof)}";
        return Encoding.UTF8.GetBytes(clientFinal);
    }

    /// <summary>Validates the server-final message (server signature).</summary>
    public void ValidateServerFinal(byte[] serverFinalBytes, byte[] serverFirstBytes)
    {
        string serverFinal = Encoding.UTF8.GetString(serverFinalBytes);
        var parts = ParseAttributes(serverFinal);
        if (!parts.TryGetValue("v", out string? serverVerifier))
            throw new InvalidOperationException("SCRAM: missing server verifier.");

        // Recompute ServerSignature = HMAC(ServerKey, AuthMessage)
        string serverFirst = Encoding.UTF8.GetString(serverFirstBytes);
        var serverParts = ParseAttributes(serverFirst);
        byte[] salt = Convert.FromBase64String(serverParts["s"]);
        int iterations = int.Parse(serverParts["i"]);
        string serverNonce = serverParts["r"];

        byte[] saltedPassword = Hi(NormalizePassword(_password), salt, iterations);
        byte[] serverKey = HMACSHA256(saltedPassword, "Server Key"u8.ToArray());

        string cbind = Convert.ToBase64String("n,,"u8.ToArray());
        string clientFinalWithoutProof = $"c={cbind},r={serverNonce}";
        string authMessage = _clientFirstMessageBare + "," + serverFirst + "," + clientFinalWithoutProof;
        byte[] expectedSig = HMACSHA256(serverKey, Encoding.UTF8.GetBytes(authMessage));

        if (Convert.ToBase64String(expectedSig) != serverVerifier)
            throw new InvalidOperationException("SCRAM: server signature verification failed.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string NormalizePassword(string password) => password; // SASLprep is complex; ASCII passwords work as-is

    private static byte[] Hi(string password, byte[] salt, int iterations)
    {
        // PBKDF2 with HMAC-SHA256
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    private static byte[] HMACSHA256(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    private static string GenerateNonce()
    {
        var bytes = new byte[18];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static Dictionary<string, string> ParseAttributes(string message)
    {
        var result = new Dictionary<string, string>();
        foreach (var part in message.Split(','))
        {
            int eq = part.IndexOf('=');
            if (eq > 0)
                result[part[..eq]] = part[(eq + 1)..];
        }
        return result;
    }
}
