using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VelaRay.Maui.Services;

/// <summary>Loopback-only PAC server with hot reload of PAC files per request; no HttpListener URL ACL.</summary>
public sealed class PacHttpServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private volatile string _selectedFile = ""; // Request handlers see a hot-swapped file path.
    public static string DefaultScript(int httpPort) => $$"""
        function FindProxyForURL(url, host) {
            if (isPlainHostName(host) || host == 'localhost' || shExpMatch(host, '*.local')) return 'DIRECT';
            return 'PROXY 127.0.0.1:{{httpPort}}';
        }
        """;

    public void Start(int pacPort, int httpPort, string pacFile)
    {
        Stop();
        Directory.CreateDirectory(Path.GetDirectoryName(pacFile)!);
        if (!File.Exists(pacFile)) File.WriteAllText(pacFile, DefaultScript(httpPort));
        _selectedFile = pacFile;
        var cts = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, pacPort);
        listener.Start();
        _cts = cts;
        _listener = listener;
        _ = ServeAsync(listener, cts.Token);
    }

    /// <summary>Change the served PAC without restarting the loopback listener.</summary>
    public void ChangeFile(string path)
    {
        if (_listener is null) throw new InvalidOperationException("PAC server is not running.");
        if (!File.Exists(path)) throw new FileNotFoundException("PAC file not found.", path);
        _selectedFile = path;
    }

    private async Task ServeAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleAsync(client, token); // Each client has its own short timeout.
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (token.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(timeout.Token) ?? "";
                if (request.Length > 2048) return;
                for (var i = 0; i < 32; ++i)
                {
                    var line = await reader.ReadLineAsync(timeout.Token);
                    if (line is null || line.Length > 2048) return;
                    if (line.Length == 0) break;
                }
                var allowed = request == "GET /proxy.pac HTTP/1.1" || request == "GET /proxy.pac HTTP/1.0";
                var body = allowed ? await File.ReadAllBytesAsync(_selectedFile, token) : Encoding.UTF8.GetBytes("Not Found");
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(allowed ? "200 OK" : "404 Not Found")}\r\nContent-Type: {(allowed ? "application/x-ns-proxy-autoconfig" : "text/plain")}\r\nCache-Control: no-store\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, token);
                await stream.WriteAsync(body, token);
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }
    public void Dispose() => Stop();
}
