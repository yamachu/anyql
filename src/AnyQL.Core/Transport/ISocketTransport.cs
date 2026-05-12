namespace AnyQL.Core.Transport;

/// <summary>
/// Abstraction over a TCP socket connection.
/// On native .NET this is backed by System.Net.Sockets.
/// On Browser-WASM this is backed by JSImport calls into the host (node:net).
/// </summary>
public interface ISocketTransport : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, CancellationToken ct = default);
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
    Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);
    Task CloseAsync(CancellationToken ct = default);
    bool IsConnected { get; }
}
