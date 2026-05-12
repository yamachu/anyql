namespace AnyQL.Postgres.Protocol;

/// <summary>Known PostgreSQL backend message type bytes.</summary>
internal static class PgMessageType
{
    // Backend messages
    public const byte Authentication = (byte)'R';
    public const byte BackendKeyData = (byte)'K';
    public const byte BindComplete = (byte)'2';
    public const byte CloseComplete = (byte)'3';
    public const byte CommandComplete = (byte)'C';
    public const byte DataRow = (byte)'D';
    public const byte EmptyQueryResponse = (byte)'I';
    public const byte ErrorResponse = (byte)'E';
    public const byte NoData = (byte)'n';
    public const byte NoticeResponse = (byte)'N';
    public const byte ParameterDescription = (byte)'t';
    public const byte ParameterStatus = (byte)'S';
    public const byte ParseComplete = (byte)'1';
    public const byte ReadyForQuery = (byte)'Z';
    public const byte RowDescription = (byte)'T';

    // Frontend messages
    public const byte Bind = (byte)'B';
    public const byte Close = (byte)'C';
    public const byte Describe = (byte)'D';
    public const byte Execute = (byte)'E';
    public const byte Parse = (byte)'P';
    public const byte Sync = (byte)'S';
    public const byte Terminate = (byte)'X';
    public const byte PasswordMessage = (byte)'p';

    // Auth sub-types (Int32 inside the 'R' message body)
    public const int AuthOk = 0;
    public const int AuthKerberosV5 = 2;
    public const int AuthCleartextPassword = 3;
    public const int AuthMd5Password = 5;
    public const int AuthSASL = 10;
    public const int AuthSASLContinue = 11;
    public const int AuthSASLFinal = 12;
}
