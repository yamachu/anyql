using System.Buffers.Binary;
using System.Text;
using AnyQL.Core.Transport;

namespace AnyQL.MySql.Protocol;

/// <summary>
/// Low-level helpers for the MySQL client/server protocol (packet format).
/// MySQL packets: 3-byte length (little-endian) + 1-byte sequence number + payload.
/// </summary>
internal sealed class MyStream(ISocketTransport transport)
{
    private readonly ISocketTransport _transport = transport;
    private byte _seq;

    public void ResetSequence() => _seq = 0;

    // ── Write ────────────────────────────────────────────────────────────────

    public async Task WritePacketAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        byte[] header = new byte[4];
        header[0] = (byte)(payload.Length & 0xFF);
        header[1] = (byte)((payload.Length >> 8) & 0xFF);
        header[2] = (byte)((payload.Length >> 16) & 0xFF);
        header[3] = _seq++;
        await _transport.WriteAsync(header, ct).ConfigureAwait(false);
        await _transport.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<byte[]> ReadPacketAsync(CancellationToken ct)
    {
        byte[] header = new byte[4];
        await ReadExactAsync(header, ct).ConfigureAwait(false);
        int length = header[0] | (header[1] << 8) | (header[2] << 16);
        _seq = (byte)(header[3] + 1);
        byte[] payload = new byte[length];
        if (length > 0)
            await ReadExactAsync(payload, ct).ConfigureAwait(false);
        return payload;
    }

    private async Task ReadExactAsync(byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buf.Length)
        {
            int read = await _transport.ReadAsync(buf.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Connection closed unexpectedly while reading from MySQL.");
            offset += read;
        }
    }

    // ── Packet field readers ─────────────────────────────────────────────────

    public static byte ReadUInt8(byte[] buf, ref int offset) => buf[offset++];

    public static ushort ReadUInt16(byte[] buf, ref int offset)
    {
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(offset));
        offset += 2;
        return v;
    }

    public static uint ReadUInt32(byte[] buf, ref int offset)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(offset));
        offset += 4;
        return v;
    }

    public static ulong ReadLengthEncodedInt(byte[] buf, ref int offset)
    {
        byte first = buf[offset++];
        return first switch
        {
            0xFB => 0,           // NULL
            0xFC => ReadUInt16Le(buf, ref offset),
            0xFD => ReadUInt24Le(buf, ref offset),
            0xFE => ReadUInt64Le(buf, ref offset),
            _ => first
        };
    }

    public static string ReadLengthEncodedString(byte[] buf, ref int offset)
    {
        ulong len = ReadLengthEncodedInt(buf, ref offset);
        string s = Encoding.UTF8.GetString(buf, offset, (int)len);
        offset += (int)len;
        return s;
    }

    public static string ReadNullTerminatedString(byte[] buf, ref int offset)
    {
        int start = offset;
        while (offset < buf.Length && buf[offset] != 0)
            offset++;
        string s = Encoding.UTF8.GetString(buf, start, offset - start);
        offset++; // skip null
        return s;
    }

    private static ushort ReadUInt16Le(byte[] buf, ref int offset)
    {
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(offset));
        offset += 2;
        return v;
    }

    private static uint ReadUInt24Le(byte[] buf, ref int offset)
    {
        uint v = (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16));
        offset += 3;
        return v;
    }

    private static ulong ReadUInt64Le(byte[] buf, ref int offset)
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(offset));
        offset += 8;
        return v;
    }
}
