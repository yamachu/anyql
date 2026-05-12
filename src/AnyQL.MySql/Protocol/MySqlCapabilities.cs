namespace AnyQL.MySql.Protocol;

/// <summary>MySQL capability flags (subset used during handshake).</summary>
internal static class MySqlCapabilities
{
    public const uint LongPassword = 0x00000001;
    public const uint FoundRows = 0x00000002;
    public const uint LongFlag = 0x00000004;
    public const uint ConnectWithDb = 0x00000008;
    public const uint Protocol41 = 0x00000200;
    public const uint Transactions = 0x00002000;
    public const uint SecureConnection = 0x00008000;
    public const uint MultiStatements = 0x00010000;
    public const uint MultiResults = 0x00020000;
    public const uint PluginAuth = 0x00080000;
    public const uint ConnectAttrs = 0x00100000;
    public const uint PluginAuthLenEncData = 0x00200000;

    // Charset UTF8MB4 = 45
    public const byte Utf8Mb4Charset = 45;
}
