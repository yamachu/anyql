using System.Runtime.InteropServices.JavaScript;
using AnyQL.Core.Transport;

namespace AnyQL.Wasm;

/// <summary>
/// ISocketTransport implementation that delegates all I/O to the JavaScript host
/// via JSImport. The host (Node.js / VS Code extension host) provides the actual
/// TCP connection using node:net.
///
/// JS interop contract (module "anyql-socket"):
///   socketConnect(id: number, host: string, port: number): Promise<void>
///   socketWrite(id: number, data: Uint8Array): Promise<void>
///   socketRead(id: number, maxBytes: number): Promise<Uint8Array>
///   socketClose(id: number): Promise<void>
/// </summary>
internal sealed class JsSocketTransport : ISocketTransport
{
    private static int _nextId;
    private readonly int _id = System.Threading.Interlocked.Increment(ref _nextId);
    private bool _connected;

    public bool IsConnected => _connected;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        await JsSocketInterop.Connect(_id, host, port).ConfigureAwait(false);
        _connected = true;
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        // Data is transferred as base64 because byte[] is not marshallable
        // across the WASM boundary inside a Promise return type.
        string base64 = await JsSocketInterop.Read(_id, buffer.Length).ConfigureAwait(false);
        if (string.IsNullOrEmpty(base64)) return 0;
        byte[] data = Convert.FromBase64String(base64);
        data.CopyTo(buffer);
        return data.Length;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        string base64 = Convert.ToBase64String(buffer.Span);
        await JsSocketInterop.Write(_id, base64).ConfigureAwait(false);
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        if (_connected)
        {
            await JsSocketInterop.Close(_id).ConfigureAwait(false);
            _connected = false;
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync(CancellationToken.None).ConfigureAwait(false);
}

/// <summary>
/// JSImport declarations for the socket interop functions provided by the JS host.
/// </summary>
internal static partial class JsSocketInterop
{
    [JSImport("connect", "anyql-socket")]
    public static partial Task Connect(int id, string host, int port);

    [JSImport("write", "anyql-socket")]
    public static partial Task Write(int id, string dataBase64);

    [JSImport("read", "anyql-socket")]
    public static partial Task<string> Read(int id, int maxBytes);

    [JSImport("close", "anyql-socket")]
    public static partial Task Close(int id);
}
