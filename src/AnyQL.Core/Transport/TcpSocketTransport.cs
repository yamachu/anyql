using System.Net.Sockets;

namespace AnyQL.Core.Transport;

/// <summary>
/// Socket transport backed by System.Net.Sockets.Socket.
/// Used in native .NET (tests, CLI, server-side tools).
/// </summary>
public sealed class TcpSocketTransport : ISocketTransport
{
    private Socket? _socket;
    private NetworkStream? _stream;

    public bool IsConnected => _socket?.Connected ?? false;

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        await _socket.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = new NetworkStream(_socket, ownsSocket: false);
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        EnsureConnected();
        return _stream!.ReadAsync(buffer, ct).AsTask();
    }

    public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        EnsureConnected();
        return _stream!.WriteAsync(buffer, ct).AsTask();
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }
        _socket?.Close();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _socket?.Dispose();
        _socket = null;
    }

    private void EnsureConnected()
    {
        if (_stream is null)
            throw new InvalidOperationException("Transport is not connected. Call ConnectAsync first.");
    }
}
