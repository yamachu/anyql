using System.Buffers.Binary;
using System.Text;
using AnyQL.Core.Transport;

namespace AnyQL.Postgres.Protocol;

/// <summary>
/// Low-level helpers for reading from and writing to the PostgreSQL wire protocol stream.
/// All multi-byte integers are big-endian per the PG specification.
/// </summary>
internal sealed class PgStream(ISocketTransport transport)
{
    private readonly ISocketTransport _transport = transport;

    // ── Write helpers ────────────────────────────────────────────────────────

    public async Task WriteMessageAsync(byte messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        // Format: type(1) + length(4 = sizeof(int) + payload.Length) + payload
        int totalLength = 4 + payload.Length;
        byte[] buf = new byte[1 + totalLength];
        buf[0] = messageType;
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(1), totalLength);
        payload.Span.CopyTo(buf.AsSpan(5));
        await _transport.WriteAsync(buf, ct).ConfigureAwait(false);
    }

    /// <summary>Writes a startup message (no type byte, length-prefixed only).</summary>
    public async Task WriteStartupAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        int totalLength = 4 + payload.Length;
        byte[] buf = new byte[totalLength];
        BinaryPrimitives.WriteInt32BigEndian(buf, totalLength);
        payload.Span.CopyTo(buf.AsSpan(4));
        await _transport.WriteAsync(buf, ct).ConfigureAwait(false);
    }

    // ── Read helpers ─────────────────────────────────────────────────────────

    public async Task<(byte type, byte[] body)> ReadMessageAsync(CancellationToken ct)
    {
        byte[] header = new byte[5];
        await ReadExactAsync(header, ct).ConfigureAwait(false);
        byte type = header[0];
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
        int bodyLength = length - 4; // length includes itself
        byte[] body = bodyLength > 0 ? new byte[bodyLength] : [];
        if (bodyLength > 0)
            await ReadExactAsync(body, ct).ConfigureAwait(false);
        return (type, body);
    }

    private async Task ReadExactAsync(byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buf.Length)
        {
            int read = await _transport.ReadAsync(buf.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Connection closed unexpectedly while reading from PostgreSQL.");
            offset += read;
        }
    }

    // ── Message builder helpers ──────────────────────────────────────────────

    public static byte[] BuildCString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var result = new byte[bytes.Length + 1];
        bytes.CopyTo(result, 0);
        result[^1] = 0; // null-terminator
        return result;
    }

    public static string ReadCString(byte[] body, ref int offset)
    {
        int start = offset;
        while (offset < body.Length && body[offset] != 0)
            offset++;
        var value = Encoding.UTF8.GetString(body, start, offset - start);
        offset++; // skip null terminator
        return value;
    }

    public static int ReadInt32(byte[] body, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(offset));
        offset += 4;
        return value;
    }

    public static uint ReadUInt32(byte[] body, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(offset));
        offset += 4;
        return value;
    }

    public static short ReadInt16(byte[] body, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt16BigEndian(body.AsSpan(offset));
        offset += 2;
        return value;
    }

    public static ushort ReadUInt16(byte[] body, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(offset));
        offset += 2;
        return value;
    }
}
