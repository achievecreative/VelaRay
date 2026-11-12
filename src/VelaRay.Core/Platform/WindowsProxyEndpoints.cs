namespace VelaRay.Core.Platform;

/// <summary>WinINET proxy map for HTTP/HTTPS and SOCKS aware clients.</summary>
public static class WindowsProxyEndpoints
{
    public static string GlobalProxyServer(int httpPort, int socksPort)
    {
        if (httpPort is < 1 or > 65535 || socksPort is < 1 or > 65535 || httpPort == socksPort)
            throw new ArgumentException("Global proxy requires distinct valid HTTP and SOCKS ports.");
        return $"http=127.0.0.1:{httpPort};https=127.0.0.1:{httpPort};socks=127.0.0.1:{socksPort}";
    }
}
