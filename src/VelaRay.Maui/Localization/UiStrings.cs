using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace VelaRay.Maui.Localization;

/// <summary>
/// UI-only localization provider. Uses stable resource keys in XAML, not runtime string replacement.
/// It notifies bindings when culture changes, so the user never needs to restart the proxy.
/// </summary>
public sealed class UiStrings : INotifyPropertyChanged
{
    public static UiStrings Instance { get; } = new();
    private readonly IReadOnlyDictionary<string, string> _zh;
    private readonly IReadOnlyDictionary<string, string> _en;
    private readonly IReadOnlyDictionary<string, string> _lookup;
    private string _language = "zh-CN";

    private UiStrings()
    {
        _zh = Load("zh-CN");
        _en = Load("en-US");
        _lookup = Load("lookup");
    }

    private static IReadOnlyDictionary<string, string> Load(string name)
    {
        using var stream = typeof(UiStrings).Assembly.GetManifestResourceStream($"VelaRay.I18n.{name}.json")
            ?? throw new InvalidOperationException($"Missing localization resource: {name}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Empty localization resource: {name}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Language => _language;
    public string this[string key] => (_language == "en-US" ? _en : _zh).TryGetValue(key, out var value) ? value : key;

    // Explicitly typed binding for a localized label inside the ServerProfile DataTemplate.
    // Avoid indexing a UiStrings resource with the template's ServerProfile x:DataType in MAUI 11 XamlC.
    public string ServerRunningIndicatorShort => this["ServerRunningIndicatorShort"];

    // Runtime messages can use the original Simplified Chinese source as lookup key.
    public string Translate(string source) =>
        _language == "en-US" && _lookup.TryGetValue(source, out var value) ? value : source;

    /// <summary>
    /// Human-readable localized summary for application and V2Fly validation failures.
    /// Original core diagnostics are retained verbatim for troubleshooting.
    /// </summary>
    public string TranslateDiagnostic(string message)
    {
        if (_language == "en-US") return Translate(message);
        if (string.IsNullOrWhiteSpace(message)) return "发生未知错误。";
        if (_lookup.TryGetValue(message, out _)) return message; // Already a known Chinese message.
        var lower = message.ToLowerInvariant();
        string? summary = null;
        if (lower.Contains("validation failed") || lower.Contains("failed to load config") || lower.Contains("unable to build config"))
            summary = "V2Fly 配置验证失败，请检查所选配置格式、节点认证及路由规则。";
        if (lower.Contains("uuid") || lower.Contains("alterid"))
            summary = "VMess 认证参数无效：请检查对应节点的 UUID 和 alterId。";
        else if (lower.Contains("allow_insecure") || lower.Contains("forbidden value"))
            summary = "配置包含内核禁止的 TLS 参数。V5 不支持 allowInsecure，请检查配置格式。";
        else if (lower.Contains("no such file") || lower.Contains("file not found") || lower.Contains("is missing"))
            summary = "缺少需要的配置或内核文件，请检查路径与 Geo 数据文件。";
        else if (lower.Contains("connection refused") || lower.Contains("timed out"))
            summary = "连接被拒绝或超时，请检查服务器地址、端口和网络。";
        else if (lower.Contains("external config is"))
            summary = "外部配置格式与当前选择的 V4/V5 加载器不匹配。";
        else if (lower.Contains("v2ray-core exited"))
            summary = "V2Ray 内核提前退出，请查看下方原始诊断信息。";
        if (summary is null && message.Any(c => c >= '\u4e00' && c <= '\u9fff')) return message;
        summary ??= "操作失败，请查看原始错误详情。";
        return summary + "\n\n内核或系统原始信息：\n" + message;
    }

    public void ChangeLanguage(string code)
    {
        if (code is not ("zh-CN" or "en-US")) throw new ArgumentOutOfRangeException(nameof(code));
        if (_language == code) return;
        _language = code;
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
