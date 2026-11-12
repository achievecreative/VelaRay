using System.Diagnostics;
using VelaRay.Core.Platform;

namespace VelaRay.Maui.Services;

/// <summary>Windows implementation; macOS can supply Finder integration later.</summary>
public sealed class WindowsFileLocationService : IFileLocationService
{
    public void RevealFile(string absoluteFilePath)
    {
        if (!File.Exists(absoluteFilePath))
            throw new FileNotFoundException("配置文件不存在。", absoluteFilePath);

        // /select,"<path>" asks Explorer to highlight the file (rather than opening it).
        // Windows paths cannot contain a double quote, so the quoting is unambiguous.
        var path = Path.GetFullPath(absoluteFilePath);
        var startInfo = new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true };
        Process.Start(startInfo);
    }
}
