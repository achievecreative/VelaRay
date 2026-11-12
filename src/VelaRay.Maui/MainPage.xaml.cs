using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VelaRay.Core.Configuration;
using VelaRay.Core.Models;
using VelaRay.Core.Pac;
using VelaRay.Core.Platform;
using VelaRay.Core.Runtime;
using VelaRay.Maui.Services;
using VelaRay.Maui.Localization;

namespace VelaRay.Maui;

public partial class MainPage : ContentPage
{
    private readonly ClientSettingsStore _store;
    private readonly V2RayCoreRunner _core;
    private readonly GeoDataUpdater _geoUpdater;
    private readonly CoreReleaseUpdater _coreUpdater;
    private readonly ISystemProxyService _proxy;
    private readonly IStartupService _startup;
    private readonly ITrayService _tray;
    private readonly PacHttpServer _pac;
    private readonly IWindowShell _shell;
    private readonly ICoreLocator _locator;
    private readonly IFileLocationService _fileLocations;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly ObservableCollection<ServerProfile> _profiles = [];
    private readonly RuntimeLogStore _runtimeLogs;
    private CancellationTokenSource? _trafficCancellation;
    private TrafficSnapshot? _trafficSnapshot;
    private bool _trafficStatsUnavailable;
    private int _runningTrafficApiPort;
    private bool _logRefreshTimerStarted;
    private int _logUiDirty;
    private bool _logsDisposed;
    private ClientSettings _settings = new();
    private ServerProfile? _selected;
    // Captured only after the core and local listeners are ready; independent of editor selection.
    private readonly HashSet<string> _activeProfileIds = new(StringComparer.Ordinal);
    private readonly List<string> _activeProfileNames = [];
    private bool _runningExternalConfig;
    private string _runningProxyMode = "";
    // Snapshot of the exact V4/V5 JSON loaded at connection time, not the editor's
    // selected server. This is what distinguishes PAC-only hot changes from core changes.
    private string? _runningConfigSnapshot;
    private string? _runningConfigProfileId;
    private string? _runningExternalFilePath;
    private bool _hasPendingCoreChanges;
    private bool _initialized;
    private bool _loading;
    // Layout-only responsive mode grid (no change to persisted proxy mode or events).
    private int _proxyModeCardColumnCount = 3;
    // Synchronize the visual mode cards with the original picker without generating changes.
    private bool _syncingProxyModeCards;
    private bool _updatingCapabilities;
    private bool _updatingVisualRouting;
    private bool _creatingVisualRule;
    private bool _expectedShutdown;
    private bool _connectionActionBusy;
    private bool _checkingCoreUpdates;
    private bool _checkingNodeLatencies;
    private bool _installingCoreUpdate;
    private bool _updatingCorePreference;
    private CoreRelease? _latestCoreRelease;
    private Version? _installedCoreVersion;
    private bool _hasCheckedInstalledCoreVersion;
    private string _pacTemplateStatusKey = "可输入域名、*.域名或 HTTPS 网站地址；保存后在 PAC 模式下生效。";
    private int _runningHttpPort;
    private int _runningPacPort;
    private bool _systemProxyApplied;
    private sealed record PacTemplateChoice(string Key, string Label);
    private sealed record LocalizedChoice(string Value, string Label);
    private static readonly (string Value, string Label)[] ProxyModes =
    [
        ("Global", "全局"), ("PAC", "PAC"), ("Manual", "手动")
    ];
    private static readonly (string Value, string Label)[] RouteModes =
    [
        ("Smart", "智能分流（推荐）"), ("ProxyAll", "全部代理"), ("Direct", "全部直连"),
        ("BypassCN", "绕过中国大陆"), ("Custom", "自定义")
    ];

    private static readonly PacTemplateChoice[] PacChoices =
    [
        new(PacTemplateGenerator.DirectByDefault, "默认直连"),
        new(PacTemplateGenerator.ProxyByDefault, "默认代理"),
        new(PacTemplateGenerator.ChinaSitesDirect, "国内网站直连")
    ];

    public MainPage(ClientSettingsStore store, V2RayCoreRunner core, GeoDataUpdater geoUpdater, CoreReleaseUpdater coreUpdater,
        ISystemProxyService proxy, IStartupService startup,
        ITrayService tray, PacHttpServer pac, IWindowShell shell, ICoreLocator locator,
        IFileLocationService fileLocations)
    {
        InitializeComponent();
        _store = store; _core = core; _geoUpdater = geoUpdater; _coreUpdater = coreUpdater;
        _proxy = proxy; _startup = startup; _tray = tray; _pac = pac; _shell = shell; _locator = locator;
        _fileLocations = fileLocations;
        _runtimeLogs = new RuntimeLogStore(_store.LogFilePath);
        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged += (_, _) =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UpdateLanguageButtons();
                    UpdateProxyModeCards();
                });
        UpdateTrafficLabels();
        ProfilesList.ItemsSource = _profiles;
        ProtocolPicker.ItemsSource = new[] { "hysteria2", "vmess", "trojan" };
        ConfigVersionPicker.ItemsSource = new[] { ConfigFlavor.V5, ConfigFlavor.V4 };
        RouteNetworkPicker.ItemsSource = new[] { "tcp,udp", "tcp", "udp" };
        RouteStrategyPicker.ItemsSource = new[] { "AsIs", "IPIfNonMatch", "IPOnDemand" };
        RouteTargetPicker.ItemDisplayBinding = new Binding(nameof(LocalizedChoice.Label));
        VmessSecurityPicker.ItemsSource = new[] { "auto", "aes-128-gcm", "chacha20-poly1305", "none", "zero" };
        TransportPicker.ItemsSource = ProfileCapabilities.Transports(ConfigFlavor.V5, "hysteria2").ToArray();
        CongestionPicker.ItemsSource = new[] { "bbr", "brutal" };
        ProxyModePicker.ItemDisplayBinding = new Binding(nameof(LocalizedChoice.Label));
        RoutingModePicker.ItemDisplayBinding = new Binding(nameof(LocalizedChoice.Label));
        RouteModeInRoutingPicker.ItemDisplayBinding = new Binding(nameof(LocalizedChoice.Label));
        SmartFallbackPicker.ItemDisplayBinding = new Binding(nameof(LocalizedChoice.Label));
        SmartFallbackRoutingPicker.ItemDisplayBinding = new Binding(nameof(LocalizedChoice.Label));
        RefreshLocalizedChoices();
        RefreshSmartUnmatchedChoices();
        GeoIpFilePicker.ItemDisplayBinding = new Binding(nameof(LocalizedChoice.Label));
        RefreshGeoIpChoices();
        PacTemplatePicker.ItemsSource = PacChoices;
        PacTemplatePicker.ItemDisplayBinding = new Binding(nameof(PacTemplateChoice.Label));

        LogLevelPicker.ItemsSource = new[] { "None", "Error", "Warning", "Info", "Debug" };
        _core.LogReceived += AppendLog;
        LogTextLabel.SizeChanged += (_, _) => ScrollLogToBottom();
        _core.UnexpectedExit += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_expectedShutdown) return;
            TryRestoreProxy();
            _pac.Stop();
            StatusLabel.Text = L("代理核心意外退出");
            SetRunningConnection(false);
            UpdateConnectionBanner(false);
            _runningHttpPort = 0;
            _runningPacPort = 0;
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;
        StartLogRefreshTimer();
        try
        {
            _settings = await _store.LoadAsync();
            UiStrings.Instance.ChangeLanguage(_settings.Language);
            UpdateLanguageButtons();
            _shell.MinimizeToTray = _settings.MinimizeToTray;
            if (_proxy.RecoverAbandonedBackup())
                AppendLog("Recovered WinINET proxy after previous abnormal exit.");
            if (_settings.Profiles.Count == 0) _settings.Profiles.Add(new ServerProfile { Name = "Server 1" });
            RefreshProfiles();
            ShowOptions();
            ShowOnly(NodeSection);
            var target = _settings.Profiles.FirstOrDefault(p => p.Id == _settings.SelectedProfileId) ?? _settings.Profiles[0];
            ProfilesList.SelectedItem = target;
            ShowProfile(target);
            _updatingCorePreference = true;
            IncludePrereleaseCoreSwitch.IsToggled = _settings.IncludePrereleaseCoreUpdates;
            _updatingCorePreference = false;
            RefreshRules();
            RefreshPacFiles();
            UpdateConnectionBanner(false);
            CustomConfigLabel.Text = _settings.CustomConfigPath ?? "未选择外部 JSON 文件";
            RefreshLocalizedRuntimeText();
            SetRunningConnection(false);
            if (_settings.ConnectAtStartup)
            {
                if (File.Exists(CoreExecutable)) await ToggleConnectionAsync();
                else AppendLog("Auto-connect skipped: v2ray.exe not found.");
            }
        }
        catch (Exception ex) { await AlertAsync("无法加载设置", ex); }
    }

    private static string L(string source) => UiStrings.Instance.Translate(source);

    private string RunningServerLabel => _runningExternalConfig
        ? UiStrings.Instance["TrayExternalConfiguration"]
        : string.Join(", ", _activeProfileNames);

    private void SynchronizeRunningIndicators()
    {
        foreach (var profile in _settings.Profiles)
            profile.IsConnected = _activeProfileIds.Contains(profile.Id);
    }

    private void SetRunningConnection(bool connected, IReadOnlyList<ServerProfile>? launchedProfiles = null,
        bool launchedExternalConfig = false)
    {
        _activeProfileIds.Clear();
        _activeProfileNames.Clear();
        _runningExternalConfig = connected && launchedExternalConfig;
        if (connected && !_runningExternalConfig && launchedProfiles is not null)
        {
            // Capture the actual launch selection, not a later editor selection.
            // Balancer participants are marked as loaded, not independently health-checked.
            foreach (var profile in launchedProfiles)
            {
                if (_activeProfileIds.Add(profile.Id)) _activeProfileNames.Add(profile.Name);
            }
        }
        if (!connected)
        {
            StopTrafficMonitoring();
            _runningProxyMode = "";
            _runningConfigSnapshot = null;
            _runningConfigProfileId = null;
            _runningExternalFilePath = null;
            _hasPendingCoreChanges = false;
        }
        SynchronizeRunningIndicators();
        _tray.SetStatus(connected, RunningServerLabel);
        UpdateNodeEditorAccess();
        UpdateConnectionBanner(connected);
    }

    private void RefreshLocalizedChoices()
    {
        var mode = (ProxyModePicker.SelectedItem as LocalizedChoice)?.Value ?? _settings.ProxyMode;
        var route = (RoutingModePicker.SelectedItem as LocalizedChoice)?.Value ?? _settings.RoutingMode;
        var proxyChoices = ProxyModes.Select(x => new LocalizedChoice(x.Value, L(x.Label))).ToArray();
        var routeChoices = RouteModes.Select(x => new LocalizedChoice(x.Value,
            x.Value == "Smart" ? UiStrings.Instance["RoutingSmartRecommended"] : L(x.Label))).ToArray();
        var previousLoading = _loading;
        _loading = true;
        ProxyModePicker.ItemsSource = proxyChoices;
        RoutingModePicker.ItemsSource = routeChoices;
        RouteModeInRoutingPicker.ItemsSource = routeChoices;
        ProxyModePicker.SelectedItem = proxyChoices.FirstOrDefault(x => x.Value == mode);
        RoutingModePicker.SelectedItem = routeChoices.FirstOrDefault(x => x.Value == route);
        RouteModeInRoutingPicker.SelectedItem = routeChoices.FirstOrDefault(x => x.Value == route);
        _loading = previousLoading;
        UpdateProxyModeCards();
    }
    private void RefreshSmartUnmatchedChoices()
    {
        var current = _settings.SmartUnmatchedRoute == SmartRoutingPolicy.UnmatchedProxy
            ? SmartRoutingPolicy.UnmatchedProxy : SmartRoutingPolicy.UnmatchedDirect;
        var choices = new[]
        {
            new LocalizedChoice(SmartRoutingPolicy.UnmatchedDirect, UiStrings.Instance["SmartUnmatchedDirect"]),
            new LocalizedChoice(SmartRoutingPolicy.UnmatchedProxy, UiStrings.Instance["SmartUnmatchedProxy"])
        };
        var previousLoading = _loading;
        _loading = true;
        try
        {
            SmartFallbackPicker.ItemsSource = choices;
            SmartFallbackRoutingPicker.ItemsSource = choices;
            SmartFallbackPicker.SelectedItem = choices.First(x => x.Value == current);
            SmartFallbackRoutingPicker.SelectedItem = choices.First(x => x.Value == current);
        }
        finally { _loading = previousLoading; }
        RefreshSmartUnmatchedVisibility();
    }

    private void RefreshSmartUnmatchedVisibility()
    {
        var smart = CurrentRouteMode == "Smart";
        SmartFallbackSettingsPanel.IsVisible = smart;
        SmartFallbackRoutingPanel.IsVisible = smart;
        SmartManagedPacNoticeLabel.IsVisible = smart && SmartRoutingPolicy.ProxyUnmatched(_settings);
    }

    private void RefreshGeoIpChoices()
    {
        var selected = (GeoIpFilePicker.SelectedItem as LocalizedChoice)?.Value ?? _settings.GeoIpFile;
        var english = UiStrings.Instance.Language == "en-US";
        var choices = new[]
        {
            new LocalizedChoice(GeoDataAssets.ChinaPrivateGeoIp, english ? "CN + Private (compact)" : "中国大陆 + 私有地址（精简）"),
            new LocalizedChoice(GeoDataAssets.FullGeoIp, english ? "Full GeoIP database" : "完整 GeoIP 数据库")
        };
        var previousLoading = _loading;
        _loading = true;
        GeoIpFilePicker.ItemsSource = choices;
        GeoIpFilePicker.SelectedItem = choices.FirstOrDefault(x => x.Value == GeoDataAssets.NormalizeGeoIpFile(selected));
        _loading = previousLoading;
    }

    private string CurrentProxyMode => (ProxyModePicker.SelectedItem as LocalizedChoice)?.Value ?? _settings.ProxyMode;
    private string CurrentRouteMode => (RoutingModePicker.SelectedItem as LocalizedChoice)?.Value ?? _settings.RoutingMode;
    private string CurrentSmartUnmatchedRoute =>
        (SmartFallbackPicker.SelectedItem as LocalizedChoice)?.Value ?? _settings.SmartUnmatchedRoute;


    private async void ChineseLanguageClicked(object sender, EventArgs e) =>
        await ChangeLanguageAsync("zh-CN");

    private async void EnglishLanguageClicked(object sender, EventArgs e) =>
        await ChangeLanguageAsync("en-US");

    private async Task ChangeLanguageAsync(string code)
    {
        if (!_initialized || _settings.Language == code) return;
        _settings.Language = code;
        UiStrings.Instance.ChangeLanguage(code);
        UpdateLanguageButtons();
        RefreshLocalizedRuntimeText();
        // The tray menu resolves labels on demand; refresh the tooltip immediately too.
        _tray.SetStatus(_core.IsRunning, RunningServerLabel);
        try { await _store.SaveAsync(_settings); }
        catch (Exception ex) { await AlertAsync(L("无法保存语言设置"), ex); }
    }

    private void UpdateLanguageButtons()
    {
        var english = _settings.Language == "en-US";
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        // Screenshot-inspired light segmented pill: only the selected option has
        // a raised white surface; the other stays transparent in the tinted track.
        var selectedBackground = Color.FromArgb(dark ? "#405575" : "#FFFFFF");
        var selectedText = Color.FromArgb(dark ? "#C3D5FF" : "#3156BD");
        var inactiveText = Color.FromArgb(dark ? "#B3C1D6" : "#5F6E84");
        ChineseLanguageButton.BackgroundColor = english ? Colors.Transparent : selectedBackground;
        EnglishLanguageButton.BackgroundColor = english ? selectedBackground : Colors.Transparent;
        ChineseLanguageButton.TextColor = english ? inactiveText : selectedText;
        EnglishLanguageButton.TextColor = english ? selectedText : inactiveText;
        ChineseLanguageButton.FontAttributes = english ? FontAttributes.None : FontAttributes.Bold;
        EnglishLanguageButton.FontAttributes = english ? FontAttributes.Bold : FontAttributes.None;
        SemanticProperties.SetHint(ChineseLanguageButton, english ? "Activate Chinese" : "Selected");
        SemanticProperties.SetHint(EnglishLanguageButton, english ? "Selected" : "Activate English");
    }

    // A separate explicit test avoids network traffic simply because the user
    // opened the server list. UDP-based Hysteria2/mKCP/QUIC are NOT represented
    // by a bogus TCP connect time.
    private static bool SupportsTcpProbe(ServerProfile profile)
    {
        if (string.Equals(profile.Protocol, "hysteria2", StringComparison.OrdinalIgnoreCase)) return false;
        var transport = profile.Transport?.Trim().ToLowerInvariant() ?? "";
        return transport is "tcp" or "ws" or "grpc" or "httpupgrade" or "h2" or "http";
    }

    private async void MeasureLatenciesClicked(object sender, EventArgs e)
    {
        if (_checkingNodeLatencies) return;
        _checkingNodeLatencies = true;
        MeasureLatencyButton.IsEnabled = false;
        var profiles = _profiles.ToArray();
        using var concurrency = new SemaphoreSlim(4, 4);
        try
        {
            await Task.WhenAll(profiles.Select(async profile =>
            {
                if (!SupportsTcpProbe(profile) || string.IsNullOrWhiteSpace(profile.Address) ||
                    profile.Port is < 1 or > 65535)
                {
                    MainThread.BeginInvokeOnMainThread(() => profile.TcpLatencyMs = null);
                    return;
                }
                await concurrency.WaitAsync();
                try
                {
                    MainThread.BeginInvokeOnMainThread(() => profile.TcpLatencyMs = null);
                    using var client = new TcpClient();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var watch = Stopwatch.StartNew();
                    await client.ConnectAsync(profile.Address.Trim(), profile.Port, timeout.Token);
                    watch.Stop();
                    var ms = Math.Max(1, watch.ElapsedMilliseconds);
                    MainThread.BeginInvokeOnMainThread(() => profile.TcpLatencyMs = ms);
                }
                catch (Exception)
                {
                    // A failed manual probe is not a connection failure. Never
                    // crash an async-void UI event because a TCP probe failed.
                    MainThread.BeginInvokeOnMainThread(() => profile.TcpLatencyMs = null);
                }
                finally
                {
                    concurrency.Release();
                }
            }));
        }
        finally
        {
            _checkingNodeLatencies = false;
            MeasureLatencyButton.IsEnabled = true;
        }
    }

    private void RefreshLocalizedRuntimeText()
    {
        var active = NodeSection.IsVisible ? NodeSection : SettingsSection.IsVisible ? SettingsSection : RoutingSection;
        ShowOnly(active);
        UpdateConnectionBanner(_core.IsRunning);
        RefreshLocalizedChoices();
        RefreshSmartUnmatchedChoices();
        RefreshGeoIpChoices();
        // Rebuild localized outbound names without resetting an in-progress rule form.
        var selectedTarget = (RouteTargetPicker.SelectedItem as LocalizedChoice)?.Value;
        RefreshRouteTargets();
        RouteTargetPicker.SelectedItem = (RouteTargetPicker.ItemsSource as IEnumerable<LocalizedChoice>)?
            .FirstOrDefault(x => x.Value == selectedTarget);
        UpdateProxyModeDescription();
        UpdateConditionalFields();
        RefreshGeoFileStatus();
        RefreshRoutingModePresentation();
        var pacKey = (PacTemplatePicker.SelectedItem as PacTemplateChoice)?.Key ?? _settings.PacTemplate;
        _loading = true;
        var localizedChoices = PacChoices.Select(x => new PacTemplateChoice(x.Key, L(x.Label))).ToArray();
        PacTemplatePicker.ItemsSource = localizedChoices;
        PacTemplatePicker.SelectedItem = localizedChoices.FirstOrDefault(x => x.Key == pacKey);
        _loading = false;
        StatusLabel.Text = _core.IsRunning ? L("已连接") : L("未连接");
        PacTemplateStatusLabel.Text = _pacTemplateStatusKey == "RoutingPendingPacDeferred"
            ? UiStrings.Instance["RoutingPendingPacDeferred"] : L(_pacTemplateStatusKey);
        RefreshCoreVersionLabels();
        RefreshCoreUpdateButtons();
        UpdateTrafficLabels();
    }

    private string SelectedConfigVersion => ConfigFlavor.Normalize(_selected?.ConfigVersion);

    private string CoreExecutable => _locator.Locate(_store.DirectoryPath, _settings.CorePathOverride);


    private void ShowOnly(VisualElement active)
    {
        NodeSection.IsVisible = ReferenceEquals(active, NodeSection);
        SettingsSection.IsVisible = ReferenceEquals(active, SettingsSection);
        RoutingSection.IsVisible = ReferenceEquals(active, RoutingSection);
        (WorkspaceTitle.Text, WorkspaceSubtitle.Text) = ReferenceEquals(active, NodeSection)
            ? (UiStrings.Instance["NavServerSettings"], L("管理服务器、认证信息及 Hysteria2 / VMess 等传输配置。"))
            : ReferenceEquals(active, SettingsSection)
            ? (L("代理设置"), L("本地端口、系统代理模式以及 Windows 桌面行为。"))
            : (L("路由与 PAC"), L("基于模板生成 PAC，添加自定义直连或代理网站。"));
        var selectedColor = Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark ? "#2F5180" : "#DDEBFF");
        var normalColor = Colors.Transparent;
        NodeNavButton.BackgroundColor = ReferenceEquals(active, NodeSection) ? selectedColor : normalColor;
        SettingsNavButton.BackgroundColor = ReferenceEquals(active, SettingsSection) ? selectedColor : normalColor;
        RoutingNavButton.BackgroundColor = ReferenceEquals(active, RoutingSection) ? selectedColor : normalColor;
    }

    private void UpdateConnectionBanner(bool connected)
    {
        // The selected server is the editing target, never the connection status.
        ConnectionStateLabel.Text = connected
            ? L("已连接") + (string.IsNullOrWhiteSpace(_runningProxyMode) ? "" : " · " + _runningProxyMode)
            : L("未连接");
        ActiveNodeLabel.Text = connected ? RunningServerLabel : "—";
        // The top bar reports the live process state, not the selected editor profile.
        TopConnectionStateLabel.Text = connected ? L("已连接") : L("未连接");
        TopConnectionDot.BackgroundColor = Color.FromArgb(connected ? "#16B878" : "#94A3B8");
        TopConnectionStateLabel.TextColor = connected ? Color.FromArgb("#16803C")
            : Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark ? "#B5CBDD" : "#3C617B");
        UpdatePendingCoreChanges();
        RefreshConnectionActionButtons();
        RefreshCoreUpdateButtons();
        RefreshCoreAvailabilityMessage();
        UpdateFooterStatus();
    }

    /// <summary>
    /// Compare the proposed configuration against the exact core config used at launch.
    /// Selection alone is not a change: always use the original launch profile.
    /// An invalid draft is considered pending, never silently applied.
    /// </summary>
    private bool UpdatePendingCoreChanges()
    {
        var pending = false;
        if (_core.IsRunning && _runningConfigSnapshot is not null)
        {
            pending = RunningConfigChangeDetector.NeedsReconnect(
                _settings, _runningConfigSnapshot, _runningConfigProfileId,
                _runningExternalConfig, _runningExternalFilePath, _runningProxyMode, _runningPacPort);
        }
        _hasPendingCoreChanges = pending;
        PendingRoutingBanner.IsVisible = pending;
        PendingReconnectButton.IsEnabled = pending && !_connectionActionBusy && !_installingCoreUpdate;
        return pending;
    }

    private async void PendingReconnectClicked(object sender, EventArgs e)
    {
        if (!_hasPendingCoreChanges || _connectionActionBusy || _installingCoreUpdate) return;
        // Only the explicit button authorizes a core restart. No save/change event calls this.
        await ToggleConnectionAsync(forceRestart: true);
    }

    private void RefreshCoreAvailabilityMessage()
    {
        if (_latestCoreRelease is null || _installingCoreUpdate) return;
        var labels = UiStrings.Instance;
        CoreUpdateStatusLabel.Text = !IsManagedCorePath ? labels["CoreCustomPathCannotInstall"] :
            !CoreReleaseUpdater.UpdateAvailable(_installedCoreVersion, _latestCoreRelease) ? labels["CoreUpToDate"] :
            _core.IsRunning ? labels["CoreDisconnectToInstall"] : labels["CoreUpdateAvailable"];
    }

    private ConnectionAction SelectedConnectionAction => ConnectionActionResolver.Resolve(
        _core.IsRunning, _selected?.Id, _activeProfileIds, _runningExternalConfig, UseCustomSwitch.IsToggled);

    private void RefreshConnectionActionButtons()
    {
        var action = SelectedConnectionAction;
        var key = action switch
        {
            ConnectionAction.Disconnect => "ActionDisconnect",
            ConnectionAction.Switch => "ActionConnectSwitch",
            _ => "HeaderConnect"
        };
        var label = UiStrings.Instance[key];
        foreach (var button in new[] { HeaderConnectionButton, ConnectionActionButton })
        {
            button.Text = label;
            // Keep the icon semantic when the same button becomes Connect / Switch / Disconnect.
            // These are visual assets only; no changes to connection behavior or callbacks.
            button.ImageSource = action switch
            {
                ConnectionAction.Disconnect => Application.Current?.RequestedTheme == AppTheme.Dark
                    ? "action_power_danger_dark.png" : "action_power_danger.png",
                ConnectionAction.Switch => "action_switch_primary.png",
                _ => "action_plug_primary.png"
            };
            button.IsEnabled = _initialized && !_connectionActionBusy && !_installingCoreUpdate &&
                (_selected is not null || _settings.UseCustomConfig);
            button.BackgroundColor = action == ConnectionAction.Disconnect
                ? Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark ? "#502A39" : "#FCE9EC")
                : Color.FromArgb("#2B67C7");
            button.TextColor = action == ConnectionAction.Disconnect
                ? Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark ? "#FFB4C2" : "#B4233C")
                : Colors.White;
            SemanticProperties.SetDescription(button, label + " · " + RunningServerLabel);
        }
        // Freeze selection only during the actual connect/switch operation.
        // While connected and idle, other profiles remain freely editable.
        ProfilesList.IsEnabled = !_connectionActionBusy && !_installingCoreUpdate;
    }
    // Align navigation icons AND captions at the left edge of full-width WinUI buttons.
    // ContentLayout controls image-vs-text ordering, not the native button alignment.
    // Keep the original Button and Clicked handlers for keyboard/accessibility behavior.
    private static void NavigationButtonHandlerChanged(object sender, EventArgs e)
    {
#if WINDOWS
        if (sender is Button button &&
            button.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button nativeButton)
            nativeButton.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
#endif
    }

    // Only reflow the three visual choices when the available viewport width changes.
    // No proxy mode selection, save, system-proxy update, or reconnect is triggered here.
    private void ProxyModeCardsGridSizeChanged(object sender, EventArgs e)
    {
        if (sender is not Grid grid || grid.Width <= 0) return;
        int columns = grid.Width >= 830 ? 3 : grid.Width >= 540 ? 2 : 1;
        if (columns == _proxyModeCardColumnCount) return;
        _proxyModeCardColumnCount = columns;
        grid.ColumnDefinitions.Clear();
        for (int i = 0; i < columns; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        Border[] cards = [PacProxyModeCard, GlobalProxyModeCard, ManualProxyModeCard];
        for (int i = 0; i < cards.Length; i++)
        {
            Grid.SetColumn(cards[i], i % columns);
            Grid.SetRow(cards[i], i / columns);
        }
    }

    private void ShowNodeClicked(object sender, EventArgs e) => ShowOnly(NodeSection);
    private void ShowSettingsClicked(object sender, EventArgs e) => ShowOnly(SettingsSection);
    private void ShowRoutingClicked(object sender, EventArgs e)
    {
        RefreshRoutingModePresentation();
        ShowOnly(RoutingSection);
    }

    private void ShowOptions()
    {
        _loading = true;
        SocksPortEntry.Text = _settings.SocksPort.ToString();
        HttpPortEntry.Text = _settings.HttpPort.ToString();
        PacPortEntry.Text = _settings.PacPort.ToString();
        ProxyModePicker.SelectedItem = (ProxyModePicker.ItemsSource as LocalizedChoice[])?.FirstOrDefault(x => x.Value == _settings.ProxyMode);
        GeoIpFilePicker.SelectedItem = (GeoIpFilePicker.ItemsSource as LocalizedChoice[])?.FirstOrDefault(x => x.Value == GeoDataAssets.NormalizeGeoIpFile(_settings.GeoIpFile));
        RoutingModePicker.SelectedItem = (RoutingModePicker.ItemsSource as LocalizedChoice[])?.FirstOrDefault(x => x.Value == _settings.RoutingMode);
        var selectedFallback = (SmartFallbackPicker.ItemsSource as LocalizedChoice[])?.FirstOrDefault(x =>
            x.Value == _settings.SmartUnmatchedRoute) ?? (SmartFallbackPicker.ItemsSource as LocalizedChoice[])?.FirstOrDefault();
        SmartFallbackPicker.SelectedItem = selectedFallback;
        SmartFallbackRoutingPicker.SelectedItem = selectedFallback;
        LogLevelPicker.SelectedItem = _settings.LogLevel;
        DnsEntry.Text = _settings.DnsServers;
        CorePathEntry.Text = _settings.CorePathOverride;
        LanSwitch.IsToggled = _settings.AllowLan;
        EnableUdpSwitch.IsToggled = _settings.EnableUdp;
        SniffSwitch.IsToggled = _settings.Sniffing;
        _settings.RestoreSystemProxy = true; // Legacy setting is no longer user-configurable.
        StartupSwitch.IsToggled = _startup.IsEnabled();
        AutoConnectSwitch.IsToggled = _settings.ConnectAtStartup;
        TraySwitch.IsToggled = _settings.MinimizeToTray;
        BalanceSwitch.IsToggled = _settings.BalanceAllNodes;
        UseCustomSwitch.IsToggled = _settings.UseCustomConfig;
        PacTemplatePicker.SelectedItem = PacChoices.FirstOrDefault(x => x.Key == _settings.PacTemplate) ?? PacChoices[0];
        PacDirectSitesEditor.Text = _settings.PacDirectWebsites ?? "";
        PacProxySitesEditor.Text = _settings.PacProxyWebsites ?? "";
        _loading = false;
        UpdateProxyModeDescription();
        UpdateFooterStatus();
        RefreshGeoFileStatus();
        RefreshRoutingModePresentation();
        RefreshSmartUnmatchedVisibility();
    }

    private void ShowProfile(ServerProfile p)
    {
        _loading = true;
        _selected = p;
        ConfigVersionPicker.SelectedItem = ConfigFlavor.Normalize(p.ConfigVersion);
        NameEntry.Text = p.Name; ProtocolPicker.SelectedItem = p.Protocol;
        AddressEntry.Text = p.Address; PortEntry.Text = p.Port.ToString();
        // The selected node's options must be loaded before its transport value.
        // Otherwise a previously selected Hysteria2-only Picker can erase a VMess network.
        TransportPicker.ItemsSource = ProfileCapabilities.Transports(p.ConfigVersion, p.Protocol).ToArray();
        UuidEntry.Text = p.Uuid; PasswordEntry.Text = p.Password;
        VmessSecurityPicker.SelectedItem = string.IsNullOrWhiteSpace(p.VmessSecurity) ? "auto" : p.VmessSecurity;
        VmessAlterIdEntry.Text = p.VmessAlterId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AllowInsecureSwitch.IsToggled = p.AllowInsecureTls;
        TransportPicker.SelectedItem = p.Transport; SniEntry.Text = p.ServerName;
        TlsSwitch.IsToggled = p.TlsEnabled; UdpSwitch.IsToggled = p.UseUdpExtension;
        AlpnEntry.Text = p.Alpn; PinEntry.Text = p.PinnedCertificateSha256;
        Hy2PasswordEntry.Text = p.TransportPassword; CongestionPicker.SelectedItem = p.Congestion;
        UploadEntry.Text = p.UploadMbps.ToString(); DownloadEntry.Text = p.DownloadMbps.ToString();
        WsPathEntry.Text = p.WebSocketPath; WsHeadersEntry.Text = p.WebSocketHeadersJson;
        GrpcEntry.Text = p.GrpcServiceName; Http2PathEntry.Text = p.Http2Path; Http2HostsEntry.Text = p.Http2Hosts;
        TcpHeaderEntry.Text = p.TcpHeaderJson; TcpFastOpenSwitch.IsToggled = p.TcpFastOpen;
        MuxSwitch.IsToggled = p.EnableMux; MuxEntry.Text = p.MuxConcurrency.ToString();
        KcpMtuEntry.Text = p.KcpMtu.ToString(); KcpTtiEntry.Text = p.KcpTti.ToString();
        KcpUplinkEntry.Text = p.KcpUplinkCapacity.ToString(); KcpDownlinkEntry.Text = p.KcpDownlinkCapacity.ToString();
        KcpReadEntry.Text = p.KcpReadBufferSize.ToString(); KcpWriteEntry.Text = p.KcpWriteBufferSize.ToString();
        KcpCongestionSwitch.IsToggled = p.KcpCongestion; KcpHeaderEntry.Text = p.KcpHeader;
        QuicSecurityEntry.Text = p.QuicSecurity; QuicKeyEntry.Text = p.QuicKey; QuicHeaderEntry.Text = p.QuicHeader;
        _loading = false;
        UpdateConditionalFields();
        UpdateNodeEditorAccess();
    }

    private bool IsActiveProfile(ServerProfile? profile) =>
        _core.IsRunning && profile is not null && _activeProfileIds.Contains(profile.Id);

    private void UpdateNodeEditorAccess()
    {
        // A running profile (including balancer participants) is immutable until disconnected.
        // All other profiles remain selectable and editable without reconnecting.
        var locked = IsActiveProfile(_selected);
        NodeEditFields.IsEnabled = !locked;
        NodeEditFields.Opacity = locked ? 0.58 : 1.0;
        RunningNodeLockNotice.IsVisible = locked;
        SaveProfileButton.IsEnabled = _selected is not null && !locked;
        DeleteProfileButton.IsEnabled = _selected is not null && !locked;
    }

    private void UpdateConditionalFields()
    {
        if (_loading || _updatingCapabilities) return;
        _updatingCapabilities = true;
        try
        {
            var format = ConfigVersionPicker.SelectedItem as string ?? _selected?.ConfigVersion ?? ConfigFlavor.V5;
            var proto = ProtocolPicker.SelectedItem as string ?? "hysteria2";
            var previousTransport = TransportPicker.SelectedItem as string;
            var allowed = ProfileCapabilities.Transports(format, proto).ToArray();
            var selected = previousTransport ?? _selected?.Transport;
            TransportPicker.ItemsSource = allowed;
            if (proto == "hysteria2") selected = "hysteria2";
            // Do NOT silently convert legacy HTTP/2 or unsupported transports to TCP.
            TransportPicker.SelectedItem = allowed.Contains(selected) ? selected : null;
            var transport = TransportPicker.SelectedItem as string;
            var validTransport = transport is not null;
            var hy2 = transport == "hysteria2";
            if (hy2) TlsSwitch.IsToggled = true;
            var tls = hy2 || TlsSwitch.IsToggled;

            // UUID belongs to VMess only. Native Hysteria2 authenticates via transport password.
            VmessUuidPanel.IsVisible = proto == "vmess";
            TrojanPasswordPanel.IsVisible = proto == "trojan";
            SetFieldEnabled(UuidEntry, proto == "vmess");
            SetFieldEnabled(PasswordEntry, proto == "trojan");
            var vmessLegacy = format == ConfigFlavor.V4 && proto == "vmess";
            SetFieldVisibility(VmessSecurityPicker, vmessLegacy);
            SetFieldEnabled(VmessSecurityPicker, vmessLegacy);
            SetFieldVisibility(VmessAlterIdEntry, vmessLegacy);
            SetFieldEnabled(VmessAlterIdEntry, vmessLegacy);
            AllowInsecurePanel.IsVisible = format == ConfigFlavor.V4 && tls;
            SetFieldEnabled(TransportPicker, proto != "hysteria2");
            SetFieldEnabled(TlsSwitch, !hy2);
            SetFieldEnabled(SniEntry, tls);
            // Hysteria2 negotiates HTTP/3 on QUIC; hide TCP-oriented ALPN overrides.
            SetFieldVisibility(AlpnEntry, tls && !hy2);
            SetFieldEnabled(AlpnEntry, tls && !hy2);
            SetFieldVisibility(PinEntry, tls && ProfileCapabilities.SupportsPinning(transport ?? ""));
            SetFieldEnabled(PinEntry, tls && ProfileCapabilities.SupportsPinning(transport ?? ""));
            SetFieldVisibility(Hy2PasswordEntry, hy2);
            SetFieldVisibility(CongestionPicker, hy2);
            SetFieldEnabled(Hy2PasswordEntry, hy2);
            SetFieldEnabled(CongestionPicker, hy2);
            SetFieldVisibility(UploadEntry, hy2 && CongestionPicker.SelectedItem as string == "brutal");
            SetFieldVisibility(DownloadEntry, hy2 && CongestionPicker.SelectedItem as string == "brutal");
            SetFieldEnabled(UdpSwitch, hy2 && proto == "hysteria2");
            SetFieldEnabled(UploadEntry, hy2 && CongestionPicker.SelectedItem as string == "brutal");
            SetFieldEnabled(DownloadEntry, hy2 && CongestionPicker.SelectedItem as string == "brutal");

            // One transport = one focused panel, with no vacant grid rows or unrelated controls.
            TransportDetailsPanel.IsVisible = transport is "ws" or "grpc" or "httpupgrade" or "tcp" or "mkcp" or "quic";
            WebSocketPanel.IsVisible = transport == "ws";
            GrpcPanel.IsVisible = transport == "grpc";
            HttpUpgradePanel.IsVisible = transport == "httpupgrade";
            TcpPanel.IsVisible = transport == "tcp";
            KcpTransportPanel.IsVisible = transport == "mkcp";
            QuicTransportPanel.IsVisible = transport == "quic";
            var (title, hint) = transport switch
            {
                "ws" => ("WebSocket", "仅显示 WebSocket 专用选项。"),
                "grpc" => ("gRPC", "仅显示 gRPC 专用选项。"),
                "httpupgrade" => ("HTTPUpgrade", "仅显示 HTTPUpgrade 专用选项。"),
                "tcp" => ("TCP", "仅显示 TCP 专用选项。"),
                "mkcp" => ("mKCP", "仅显示 mKCP 专用选项。"),
                "quic" => ("QUIC", "仅显示 QUIC 专用选项。"),
                _ => ("", "")
            };
            TransportDetailsTitle.Text = title;
            TransportDetailsHint.Text = L(hint);
            SocketOptionsPanel.IsVisible = validTransport && !hy2;
            SetFieldEnabled(TcpFastOpenSwitch, transport is "tcp" or "ws" or "httpupgrade");
            SetFieldEnabled(MuxSwitch, validTransport && !hy2);
            SetFieldVisibility(MuxEntry, validTransport && !hy2 && MuxSwitch.IsToggled);
            SetFieldEnabled(MuxEntry, validTransport && !hy2 && MuxSwitch.IsToggled);

        }
        finally { _updatingCapabilities = false; }
    }

    private static void SetFieldEnabled(VisualElement field, bool enabled)
    {
        field.IsEnabled = enabled;
        field.Opacity = enabled ? 1.0 : 0.3;
        // Gray out both caption and input so users can distinguish disabled state immediately.
        if (field.Parent is VisualElement parent) parent.Opacity = enabled ? 1.0 : 0.38;
    }

    private static void SetFieldVisibility(VisualElement field, bool visible)
    {
        if (field.Parent is VisualElement container) container.IsVisible = visible;
    }

    private void ProxyModeChanged(object sender, EventArgs e)
    {
        if (!_loading) UpdateProxyModeDescription();
    }

    // The three radio cards are a visual adapter only: the original picker
    // remains the one source of truth for CaptureOptions and runtime behavior.
    private void ProxyModeCardCheckedChanged(object sender, CheckedChangedEventArgs args)
    {
        if (!args.Value || _loading || _syncingProxyModeCards || sender is not RadioButton radio) return;
        if (radio.Value?.ToString() is { } mode) SelectProxyModeCard(mode);
    }

    private void PacProxyModeCardTapped(object sender, TappedEventArgs e) => SelectProxyModeCard("PAC");
    private void GlobalProxyModeCardTapped(object sender, TappedEventArgs e) => SelectProxyModeCard("Global");
    private void ManualProxyModeCardTapped(object sender, TappedEventArgs e) => SelectProxyModeCard("Manual");

    private void SelectProxyModeCard(string mode)
    {
        if (_loading || _syncingProxyModeCards || CurrentProxyMode == mode) return;
        var selection = (ProxyModePicker.ItemsSource as IEnumerable<LocalizedChoice>)?
            .FirstOrDefault(item => item.Value == mode);
        if (selection is not null) ProxyModePicker.SelectedItem = selection;
    }

    private void UpdateProxyModeCards()
    {
        // The selected outline indicates a pending UI choice, not an already-applied
        // Windows system proxy. Save/apply remains governed by existing handlers.
        if (PacProxyModeCard is null || GlobalProxyModeCard is null || ManualProxyModeCard is null) return;
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var active = Color.FromArgb(dark ? "#8AABFF" : "#2563EB");
        var passive = Color.FromArgb(dark ? "#40536D" : "#DDE6F2");
        var selectedBackground = Color.FromArgb(dark ? "#203757" : "#ECF3FF");
        var normalBackground = Color.FromArgb(dark ? "#1A293E" : "#FAFCFF");
        var mode = CurrentProxyMode;
        _syncingProxyModeCards = true;
        try
        {
            foreach (var (value, card, radio) in new[]
            {
                ("PAC", PacProxyModeCard, PacProxyModeRadio),
                ("Global", GlobalProxyModeCard, GlobalProxyModeRadio),
                ("Manual", ManualProxyModeCard, ManualProxyModeRadio)
            })
            {
                var selected = mode == value;
                radio.IsChecked = selected;
                card.Stroke = new SolidColorBrush(selected ? active : passive);
                card.StrokeThickness = selected ? 2 : 1;
                card.BackgroundColor = selected ? selectedBackground : normalBackground;
            }
        }
        finally { _syncingProxyModeCards = false; }
    }

    private void UpdateFooterStatus()
    {
        ConfiguredPortsFooterLabel.Text = string.Format(
            UiStrings.Instance["FooterConfiguredPorts"],
            _settings.SocksPort, _settings.HttpPort, _settings.PacPort);
        RunningModeFooterLabel.Text = _core.IsRunning
            ? string.Format(UiStrings.Instance["FooterRuntimeMode"],
                string.IsNullOrWhiteSpace(_runningProxyMode) ? "—" : _runningProxyMode)
            : UiStrings.Instance["FooterDisconnected"];
    }

    private void CustomConfigToggled(object sender, ToggledEventArgs e)
    {
        if (!_loading) RefreshConnectionActionButtons();
    }

    private void UpdateProxyModeDescription()
    {
        ProxyModeDescriptionLabel.Text = L(CurrentProxyMode switch
        {
            "Global" => "全局：向 Windows 设置 HTTP/HTTPS + SOCKS5 代理；只有遵循系统代理的应用会经过代理（非 TUN）。",
            "PAC" => "PAC：将 Windows 自动配置 URL 指向本地 PAC 服务；命中 PAC 直连的请求不会进入 V2Ray。",
            _ => "手动：只启动本地 SOCKS5 和 HTTP 代理，不修改任何 Windows 系统代理设置。"
        });
        UpdateProxyModeCards();
    }

    private void RefreshGeoFileStatus()
    {
        var candidate = _locator.Locate(_store.DirectoryPath, CorePathEntry.Text?.Trim());
        var dir = Path.GetDirectoryName(candidate) ?? _store.CoreDirectory;
        var files = new[] { "geosite.dat", GeoDataAssets.FullGeoIp, GeoDataAssets.ChinaPrivateGeoIp };
        GeoAssetsStatusLabel.Text = L("V2Fly 数据位置：") + dir + Environment.NewLine
            + string.Join("  |  ", files.Select(name =>
                $"{name}: {L(File.Exists(Path.Combine(dir, name)) ? "存在" : "缺失")}"));
    }

    private string SelectedCorePath => _locator.Locate(_store.DirectoryPath, CorePathEntry.Text?.Trim());

    private bool IsManagedCorePath
    {
        get
        {
            try
            {
                return Path.GetFullPath(SelectedCorePath).Equals(
                    Path.GetFullPath(_locator.Locate(_store.DirectoryPath, null)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch (ArgumentException) { return false; }
        }
    }

    private void CorePathChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || !_initialized) return;
        _latestCoreRelease = null;
        _installedCoreVersion = null;
        _hasCheckedInstalledCoreVersion = false;
        RefreshCoreVersionLabels();
        RefreshCoreUpdateButtons();
        CoreUpdateStatusLabel.Text = UiStrings.Instance["CorePathChangedRecheck"];
    }

    private void RefreshCoreVersionLabels()
    {
        var labels = UiStrings.Instance;
        bool hasCore;
        try { hasCore = File.Exists(SelectedCorePath); }
        catch (ArgumentException) { hasCore = false; }
        CurrentCoreVersionLabel.Text = labels["CoreInstalledVersion"] + ": " +
            (_installedCoreVersion?.ToString() ?? (hasCore
                ? labels[_hasCheckedInstalledCoreVersion ? "CoreVersionUnknown" : "CoreNotChecked"]
                : labels["CoreNotInstalled"]));
        LatestCoreVersionLabel.Text = labels[_settings.IncludePrereleaseCoreUpdates ? "CoreLatestAll" : "CoreLatestStable"] + ": " +
            (_latestCoreRelease is null ? labels["CoreNotChecked"] :
                _latestCoreRelease.Tag + (_latestCoreRelease.IsPrerelease ? " (" + labels["CorePrerelease"] + ")" : ""));
    }

    private void RefreshCoreUpdateButtons()
    {
        CheckCoreUpdateButton.IsEnabled = _initialized && !_checkingCoreUpdates && !_installingCoreUpdate;
        IncludePrereleaseCoreSwitch.IsEnabled = !_checkingCoreUpdates && !_installingCoreUpdate;
        InstallCoreUpdateButton.IsEnabled = _initialized && !_checkingCoreUpdates && !_installingCoreUpdate &&
            !_core.IsRunning && IsManagedCorePath && CoreReleaseUpdater.UpdateAvailable(_installedCoreVersion, _latestCoreRelease);
    }

    private async void CorePrereleaseToggled(object sender, ToggledEventArgs e)
    {
        if (_updatingCorePreference || !_initialized) return;
        _settings.IncludePrereleaseCoreUpdates = e.Value;
        _latestCoreRelease = null;
        RefreshCoreVersionLabels();
        RefreshCoreUpdateButtons();
        // Changing channels invalidates previous release results; only an explicit click may check online.
        CoreUpdateStatusLabel.Text = UiStrings.Instance["CoreManualCheckPrompt"];
        try { await _store.SaveAsync(_settings); }
        catch (Exception ex) { await AlertAsync("无法保存内核更新设置", ex); }
    }

    private async void CheckCoreUpdatesClicked(object sender, EventArgs e) => await CheckCoreUpdatesAsync();

    // Intentionally called only from the explicit user action, never from page startup or settings changes.
    private async Task CheckCoreUpdatesAsync()
    {
        if (_checkingCoreUpdates || _installingCoreUpdate) return;
        _checkingCoreUpdates = true;
        _latestCoreRelease = null;
        CoreUpdateStatusLabel.Text = UiStrings.Instance["CoreChecking"];
        RefreshCoreUpdateButtons();
        try
        {
            var selectedCorePath = SelectedCorePath;
            var includePrerelease = _settings.IncludePrereleaseCoreUpdates;
            try { _installedCoreVersion = await CoreReleaseUpdater.GetInstalledVersionAsync(selectedCorePath); }
            catch (Exception ex)
            {
                _installedCoreVersion = null;
                AppendLog("Cannot identify current v2ray-core version: " + ex.Message);
            }
            _hasCheckedInstalledCoreVersion = true;
            RefreshCoreVersionLabels();
            var result = await _coreUpdater.GetLatestAsync(includePrerelease);
            if (selectedCorePath != SelectedCorePath || includePrerelease != _settings.IncludePrereleaseCoreUpdates)
                return; // The user changed the update target while the request was in flight.
            _latestCoreRelease = result;
            RefreshCoreVersionLabels();
            RefreshCoreAvailabilityMessage();
            AppendLog($"V2Fly core check: installed={_installedCoreVersion?.ToString() ?? "unknown"}, latest={_latestCoreRelease.Tag}.");
        }
        catch (Exception ex)
        {
            CoreUpdateStatusLabel.Text = UiStrings.Instance["CoreCheckFailed"] + ": " + ex.Message;
            AppendLog("V2Fly core update check failed: " + ex);
            await AlertAsync(UiStrings.Instance["CoreCheckFailed"], ex);
        }
        finally
        {
            _checkingCoreUpdates = false;
            RefreshCoreVersionLabels();
            RefreshCoreUpdateButtons();
        }
    }

    private async void InstallCoreUpdateClicked(object sender, EventArgs e)
    {
        if (_installingCoreUpdate || _checkingCoreUpdates || _latestCoreRelease is not { } release) return;
        if (_core.IsRunning || !IsManagedCorePath)
        {
            await DisplayAlert(UiStrings.Instance["SectionCoreUpdates"],
                UiStrings.Instance[_core.IsRunning ? "CoreDisconnectToInstall" : "CoreCustomPathCannotInstall"], "OK");
            return;
        }
        var confirmed = await DisplayAlert(UiStrings.Instance["SectionCoreUpdates"],
            string.Format(UiStrings.Instance["CoreInstallConfirmation"], release.Tag),
            UiStrings.Instance["CoreInstallConfirm"], UiStrings.Instance["CoreInstallCancel"]);
        if (!confirmed) return;
        _installingCoreUpdate = true;
        RefreshCoreUpdateButtons();
        RefreshConnectionActionButtons();
        try
        {
            await _connectionGate.WaitAsync();
            try
            {
                if (_core.IsRunning || !IsManagedCorePath)
                    throw new InvalidOperationException("Disconnect the core and select the managed core path before installing.");
                CoreUpdateStatusLabel.Text = UiStrings.Instance["CoreDownloadingAndVerifying"];
                var backup = await _coreUpdater.InstallAsync(SelectedCorePath, release);
                _installedCoreVersion = await CoreReleaseUpdater.GetInstalledVersionAsync(SelectedCorePath);
                _hasCheckedInstalledCoreVersion = true;
                CoreUpdateStatusLabel.Text = UiStrings.Instance["CoreInstallSuccess"] +
                    (backup is null ? "" : Environment.NewLine + UiStrings.Instance["CoreBackupLocation"] + ": " + backup);
                AppendLog($"V2Fly core installed: {release.Tag}; backup={backup ?? "none"}");
            }
            finally { _connectionGate.Release(); }
        }
        catch (Exception ex)
        {
            CoreUpdateStatusLabel.Text = UiStrings.Instance["CoreInstallFailed"] + ": " + ex.Message;
            AppendLog("V2Fly core install failed: " + ex);
            await AlertAsync(UiStrings.Instance["CoreInstallFailed"], ex);
        }
        finally
        {
            _installingCoreUpdate = false;
            RefreshCoreVersionLabels();
            RefreshCoreUpdateButtons();
            RefreshConnectionActionButtons();
        }
    }

    private async void CheckGeoAssetsClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            RefreshGeoFileStatus();
            var candidate = _locator.Locate(_store.DirectoryPath, CorePathEntry.Text?.Trim());
            var currentSelection = new ClientSettings
            {
                RoutingMode = CurrentRouteMode,
                GeoIpFile = GeoDataAssets.NormalizeGeoIpFile((GeoIpFilePicker.SelectedItem as LocalizedChoice)?.Value)
            };
            var missing = GeoDataAssets.MissingForDefaultRouting(currentSelection, candidate);
            await DisplayAlert(L("Geo 数据检查"), missing.Count == 0
                ? L("当前内核路由模式所需的 Geo 文件齐全；完整 geoip.dat 可作为备用数据。")
                : L("当前绕过中国大陆规则缺少：") + string.Join(", ", missing), L("确定"));
        }, "Geo 数据检查失败");
    }

    private async void UpdateGeoAssetsClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var answer = await DisplayAlert(L("更新 Geo 数据"), L("将从 V2Fly 官方发布地址下载 geosite.dat、geoip.dat、geoip-only-cn-private.dat，逐个验证 SHA256 后写入 v2ray-core 目录。正在运行的代理需重新连接才会加载新数据。是否继续？"), L("继续"), L("取消"));
            if (!answer) return;
            StatusLabel.Text = L("正在下载 Geo 数据…");
            int count = await _geoUpdater.UpdateAsync(CoreExecutable);
            RefreshGeoFileStatus();
            AppendLog($"Updated {count} verified Geo assets. Reconnect to apply them.");
            await DisplayAlert(L("Geo 数据已更新"), UiStrings.Instance.Language == "en-US"
                ? $"Updated {count} data files. Disconnect and reconnect to load the new Geo data."
                : $"已更新 {count} 个数据文件。请断开并重新连接，以便 V2Fly 加载最新的 Geo 数据。", L("确定"));
            StatusLabel.Text = L(_core.IsRunning ? "已连接（Geo 数据更新待重连）" : "Geo 数据已更新");
        }, "更新 Geo 数据失败");
    }

    private void ConfigVersionChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        if (_selected is null) return;
        var current = _selected;
        var previous = ConfigFlavor.Normalize(current.ConfigVersion);
        var next = ConfigFlavor.Normalize(ConfigVersionPicker.SelectedItem as string);
        if (previous == next) return;
        // Capture V4-only settings while the previous format is still active.
        try { CaptureProfile(); }
        catch (Exception ex)
        {
            _loading = true;
            ConfigVersionPicker.SelectedItem = previous;
            _loading = false;
            AppendLog(L("无法切换格式，当前节点参数无效：") + ex.Message);
            return;
        }
        current.ConfigVersion = next;
        // Reload the selected node to recompute its supported controls and transports;
        // other profiles keep their own V4/V5 choices untouched.
        ShowProfile(current);
    }
    private void SecurityOptionsChanged(object sender, EventArgs e) => UpdateConditionalFields();
    private void ProtocolChanged(object sender, EventArgs e) => UpdateConditionalFields();
    private void TransportChanged(object sender, EventArgs e) => UpdateConditionalFields();

    private void ProfilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ServerProfile p || _loading) return;
        if (_selected is not null && !ReferenceEquals(_selected, p))
        {
            try { CaptureProfile(); }
            catch (Exception ex)
            {
                AppendLog("当前节点存在尚未保存的无效参数：" + ex.Message);
                // Do not silently overwrite an invalid editor field when selecting another node.
                ProfilesList.SelectedItem = _selected;
                return;
            }
        }
        _settings.SelectedProfileId = p.Id;
        // Selection intentionally does not start, stop, or switch the current connection.
        ShowProfile(p);
        // Selecting a server opens its editor; it never starts or switches the proxy.
        ShowOnly(NodeSection);
        UpdateConnectionBanner(_core.IsRunning);
    }

    private void RefreshProfiles()
    {
        var selectedId = _settings.SelectedProfileId;
        _profiles.Clear();
        foreach (var profile in _settings.Profiles) _profiles.Add(profile);
        SynchronizeRunningIndicators();
        ProfilesList.SelectedItem = _profiles.FirstOrDefault(x => x.Id == selectedId) ?? _profiles.FirstOrDefault();
    }
    private void RefreshProfileDisplay()
    {
        foreach (var profile in _profiles) profile.NotifyCardChanged();
        var selected = _selected;
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _profiles;
        ProfilesList.SelectedItem = selected;
    }

    private void AddClicked(object sender, EventArgs e)
    {
        var item = new ServerProfile { Name = "Server " + (_profiles.Count + 1) };
        _settings.Profiles.Add(item);
        _profiles.Add(item);
        ProfilesList.SelectedItem = item;
        ShowProfile(item);
    }
    private void CloneClicked(object sender, EventArgs e)
    {
        if (_selected is null) return;
        CaptureProfile();
        var cloned = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(_selected))!;
        cloned.Id = Guid.NewGuid().ToString("N");
        cloned.Name += " (copy)";
        _settings.Profiles.Add(cloned);
        _profiles.Add(cloned);
        ProfilesList.SelectedItem = cloned;
        ShowProfile(cloned);
    }
    private async void DeleteClicked(object sender, EventArgs e)
    {
        if (_selected is null) return;
        if (IsActiveProfile(_selected))
        {
            await DisplayAlert(UiStrings.Instance["ActionDeleteNotAllowed"], UiStrings.Instance["DisconnectBeforeDelete"], "OK");
            return;
        }
        var deleted = _selected;
        _settings.Profiles.Remove(deleted);
        _profiles.Remove(deleted);
        _selected = _settings.Profiles.FirstOrDefault();
        _settings.SelectedProfileId = _selected?.Id;
        if (_selected is null) AddClicked(sender, e);
        else ProfilesList.SelectedItem = _selected;
        await _store.SaveAsync(_settings);
    }
    private async void ShareClicked(object sender, EventArgs e)
    {
        if (_selected is null) return;
        try
        {
            CaptureProfile();
            var p = _selected;
            string link;
            if (p.Protocol == "hysteria2")
            {
                link = $"hysteria2://{Uri.EscapeDataString(p.TransportPassword)}@{p.Address}:{p.Port}?sni={Uri.EscapeDataString(p.ServerName)}#{Uri.EscapeDataString(p.Name)}";
            }
            else if (p.Protocol == "vmess")
            {
                if (ConfigFlavor.Normalize(p.ConfigVersion) == ConfigFlavor.V5 && p.VmessAlterId != 0)
                    throw new NotSupportedException("V5 仅支持 alterId=0。请切换回 V4 修改后再分享。 ");
                var data = new { v = "2", ps = p.Name, add = p.Address, port = p.Port.ToString(), id = p.Uuid,
                    aid = p.VmessAlterId.ToString(System.Globalization.CultureInfo.InvariantCulture), scy = p.VmessSecurity, net = p.Transport, host = p.ServerName, path = p.WebSocketPath,
                    tls = p.TlsEnabled ? "tls" : "", sni = p.ServerName };
                link = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            }
            else if (p.Protocol == "trojan")
                link = $"trojan://{Uri.EscapeDataString(p.Password)}@{p.Address}:{p.Port}?sni={Uri.EscapeDataString(p.ServerName)}#{Uri.EscapeDataString(p.Name)}";
            else
                throw new NotSupportedException("Only VMess, Trojan and Hysteria2 share links are supported.");
            await Clipboard.Default.SetTextAsync(link);
            await DisplayAlert(L("分享"), L("分享链接已复制；高级传输参数不一定包含在通用分享格式中。"), "OK");
        }
        catch (Exception ex) { await AlertAsync("无法分享", ex); }
    }

    private void CaptureProfile()
    {
        if (_selected is not { } p) return;
        // Even indirect Save/Share/Preview actions must not mutate the running profile.
        if (IsActiveProfile(p)) return;
        var originalEndpoint = (p.Address, p.Port, p.Protocol, p.Transport);
        p.Name = string.IsNullOrWhiteSpace(NameEntry.Text) ? "Unnamed" : NameEntry.Text.Trim();
        p.Protocol = ProtocolPicker.SelectedItem as string ?? p.Protocol;
        p.Address = AddressEntry.Text?.Trim() ?? "";
        p.Port = Port(PortEntry.Text, "服务器端口");
        p.Uuid = UuidEntry.Text?.Trim() ?? "";
        p.Password = PasswordEntry.Text ?? "";
        if (p.Protocol == "vmess" && ConfigFlavor.Normalize(p.ConfigVersion) == ConfigFlavor.V4)
        {
            p.VmessSecurity = VmessSecurityPicker.SelectedItem as string ?? "auto";
            p.VmessAlterId = Int(VmessAlterIdEntry.Text, "VMess alterId", 0);
            if (p.VmessAlterId > 65535)
                throw new ArgumentException("VMess alterId 必须在 0-65535 之间；推荐设置为 0（AEAD）。");
        }
        p.Transport = TransportPicker.SelectedItem as string
            ?? throw new ArgumentException(L("当前配置需升级传输类型。"));
        p.ServerName = SniEntry.Text?.Trim() ?? "";
        p.TlsEnabled = TlsSwitch.IsToggled;
        p.UseUdpExtension = UdpSwitch.IsToggled;
        p.Alpn = AlpnEntry.Text?.Trim() ?? "";
        p.PinnedCertificateSha256 = PinEntry.Text?.Trim() ?? "";
        // Preserve V4 TLS opt-in when switching to V5: V5 hides the control but never emits the flag.
        if (ConfigFlavor.Normalize(p.ConfigVersion) == ConfigFlavor.V4)
            p.AllowInsecureTls = AllowInsecureSwitch.IsToggled && p.TlsEnabled;
        p.TransportPassword = Hy2PasswordEntry.Text ?? "";
        p.Congestion = CongestionPicker.SelectedItem as string ?? "bbr";
        p.UploadMbps = Int(UploadEntry.Text, "上传 Mbps", 0);
        p.DownloadMbps = Int(DownloadEntry.Text, "下载 Mbps", 0);
        p.WebSocketPath = WsPathEntry.Text?.Trim() ?? "/";
        p.WebSocketHeadersJson = WsHeadersEntry.Text?.Trim() ?? "{}";
        p.GrpcServiceName = GrpcEntry.Text?.Trim() ?? "";
        p.Http2Path = Http2PathEntry.Text?.Trim() ?? "/";
        p.Http2Hosts = Http2HostsEntry.Text?.Trim() ?? "";
        p.TcpHeaderJson = TcpHeaderEntry.Text?.Trim() ?? "{\"type\":\"none\"}";
        p.TcpFastOpen = TcpFastOpenSwitch.IsToggled;
        p.EnableMux = MuxSwitch.IsToggled;
        p.MuxConcurrency = Int(MuxEntry.Text, "Mux", 8);
        p.KcpMtu = Int(KcpMtuEntry.Text, "KCP MTU", 1350);
        p.KcpTti = Int(KcpTtiEntry.Text, "KCP TTI", 50);
        p.KcpUplinkCapacity = Int(KcpUplinkEntry.Text, "KCP uplink", 5);
        p.KcpDownlinkCapacity = Int(KcpDownlinkEntry.Text, "KCP downlink", 20);
        p.KcpReadBufferSize = Int(KcpReadEntry.Text, "KCP read", 2);
        p.KcpWriteBufferSize = Int(KcpWriteEntry.Text, "KCP write", 2);
        p.KcpHeader = KcpHeaderEntry.Text?.Trim() ?? "none";
        p.KcpCongestion = KcpCongestionSwitch.IsToggled;
        p.QuicSecurity = QuicSecurityEntry.Text?.Trim() ?? "none";
        p.QuicKey = QuicKeyEntry.Text?.Trim() ?? "";
        p.QuicHeader = QuicHeaderEntry.Text?.Trim() ?? "none";
        if (originalEndpoint != (p.Address, p.Port, p.Protocol, p.Transport))
            p.TcpLatencyMs = null;
        p.NotifyCardChanged();
    }

    private void CaptureOptions()
    {
        _settings.SocksPort = Port(SocksPortEntry.Text, "SOCKS5");
        _settings.HttpPort = Port(HttpPortEntry.Text, "HTTP");
        _settings.PacPort = Port(PacPortEntry.Text, "PAC");
        _settings.ProxyMode = CurrentProxyMode;
        _settings.GeoIpFile = GeoDataAssets.NormalizeGeoIpFile((GeoIpFilePicker.SelectedItem as LocalizedChoice)?.Value);
        _settings.RoutingMode = CurrentRouteMode;
        _settings.SmartUnmatchedRoute = CurrentSmartUnmatchedRoute;
        _settings.LogLevel = LogLevelPicker.SelectedItem as string ?? "Warning";
        _settings.DnsServers = DnsEntry.Text?.Trim() ?? "";
        _settings.CorePathOverride = CorePathEntry.Text?.Trim();
        _settings.AllowLan = LanSwitch.IsToggled;
        _settings.EnableUdp = EnableUdpSwitch.IsToggled;
        _settings.Sniffing = SniffSwitch.IsToggled;
        _settings.RestoreSystemProxy = true;
        _settings.StartWithWindows = StartupSwitch.IsToggled;
        _settings.ConnectAtStartup = AutoConnectSwitch.IsToggled;
        _settings.MinimizeToTray = TraySwitch.IsToggled;
        _shell.MinimizeToTray = _settings.MinimizeToTray;
        _settings.BalanceAllNodes = BalanceSwitch.IsToggled;
        _settings.UseCustomConfig = UseCustomSwitch.IsToggled;
        CapturePacTemplateOptions();
        _settings.SelectedProfileId = _selected?.Id;
    }
    private static int Port(string? value, string label) =>
        int.TryParse(value, out var n) && n is >= 1 and <= 65535 ? n : throw new ArgumentException($"{label}: 端口必须在 1-65535 之间。");
    private static int Int(string? value, string label, int fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : int.TryParse(value, out var n) && n >= 0
            ? n : throw new ArgumentException($"{label}: 需要非负整数。");

    private async Task SaveAllAsync()
    {
        CaptureProfile();
        CaptureOptions();
        if (_settings.StartWithWindows != _startup.IsEnabled()) _startup.SetEnabled(_settings.StartWithWindows);
        await _store.SaveAsync(_settings);
        RefreshProfileDisplay();
        UpdatePendingCoreChanges();
        UpdateFooterStatus();
    }

    private async Task<string> SaveAndGenerateAsync()
    {
        await SaveAllAsync();
        if (_settings.UseCustomConfig)
        {
            var path = string.IsNullOrWhiteSpace(_settings.CustomConfigPath) ? null : _store.ResolveFilePath(_settings.CustomConfigPath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("请选择现有 JSON 配置文件。", path);
            var external = await File.ReadAllTextAsync(path);
            V5ConfigBuilder.ParseObject(external, "external config");
            var detected = ConfigFlavor.DetectFromJson(external);
            if (detected is not null && detected != ConfigFlavor.Normalize(_selected?.ConfigVersion))
                throw new InvalidOperationException($"External config is {detected}, but the selected loader is {SelectedConfigVersion}. Switch the config format before starting.");
            return path;
        }
        var json = ConfigFlavor.Build(_settings, _selected ?? throw new InvalidOperationException("没有选择节点。"));
        var target = _store.GetCoreConfigPath(SelectedConfigVersion);
        await File.WriteAllTextAsync(target, json);
        return target;
    }

    private async void SaveClicked(object sender, EventArgs e)
    {
        // Only the server-specific Save button is blocked for running profiles.
        // Global proxy/PAC settings remain editable and savable while connected.
        if (ReferenceEquals(sender, SaveProfileButton) && IsActiveProfile(_selected))
        {
            await DisplayAlert(UiStrings.Instance["ActionEditNotAllowed"],
                UiStrings.Instance["DisconnectBeforeEdit"], "OK");
            return;
        }
        try { await SaveAllAsync(); StatusLabel.Text = "设置已保存"; }
        catch (Exception ex) { await AlertAsync("保存失败", ex); }
    }
    private async void ExportClicked(object sender, EventArgs e)
    {
        try
        {
            var path = await SaveAndGenerateAsync();
            await DisplayAlert($"{SelectedConfigVersion} JSON", "已生成：" + path + "\n可点击「查看生成配置」查看 JSON 内容。", "OK");
        }
        catch (Exception ex) { await AlertAsync("导出失败", ex); }
    }

    // The footer opens the exact running snapshot if connected, otherwise the generated preview.
    private async void ViewConfigClicked(object sender, EventArgs e)
    {
        if (_core.IsRunning) await ShowRunningConfigAsync();
        else await ShowGeneratedConfigAsync();
    }
    private async void PreviewConfigClicked(object sender, EventArgs e) => await ShowGeneratedConfigAsync();

    private async Task ShowGeneratedConfigAsync() => await RunSafelyAsync(async () =>
    {
        var path = await SaveAndGenerateAsync();
        var json = await File.ReadAllTextAsync(path);
        await Navigation.PushModalAsync(new ConfigViewerPage(path, json, false, _fileLocations, SelectedConfigVersion));
    }, "无法生成配置预览");

    private async Task ShowRunningConfigAsync() => await RunSafelyAsync(async () =>
    {
        var path = _core.RunningConfigPath;
        if (!_core.IsRunning || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            await DisplayAlert(L("尚未连接"), L("目前没有运行中的 v2ray-core 配置。请先连接，或使用「查看生成配置」。"), "OK");
            return;
        }
        var json = await File.ReadAllTextAsync(path);
        await Navigation.PushModalAsync(new ConfigViewerPage(path, json, true, _fileLocations, _core.RunningConfigFormat ?? SelectedConfigVersion));
    }, "无法查看运行配置");
    private async void ApplyClicked(object sender, EventArgs e) => await ToggleConnectionAsync(forceRestart: true);
    private async void ConnectionActionClicked(object sender, EventArgs e)
    {
        if (_connectionActionBusy || _installingCoreUpdate) return;
        // Resolve once at click time; editing/selecting a node never changes the live process.
        var action = SelectedConnectionAction;
        _connectionActionBusy = true;
        RefreshConnectionActionButtons();
        try
        {
            await RunSafelyAsync(action == ConnectionAction.Disconnect ? DisconnectAsync : ConnectAsync,
                action == ConnectionAction.Disconnect ? "断开失败" : "无法切换连接");
        }
        finally
        {
            _connectionActionBusy = false;
            RefreshConnectionActionButtons();
        }
    }

    public async Task ToggleConnectionAsync(bool forceRestart = false)
    {
        if (_installingCoreUpdate) return; // Tray and auto-connect must not race the installer.
        await RunSafelyAsync(async () =>
        {
            if (_core.IsRunning && !forceRestart) await DisconnectAsync();
            else await ConnectAsync();
        }, "无法切换连接");
    }

    private async Task ConnectAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            _expectedShutdown = true;
            if (_core.IsRunning) await StopCoreAndProxyAsync();
            var configPath = await SaveAndGenerateAsync();
            // Snapshot the launch profile(s) before any asynchronous core startup.
            // Selecting a different node during startup must not move the green marker.
            bool launchedExternal = _settings.UseCustomConfig;
            IReadOnlyList<ServerProfile> launchedProfiles = launchedExternal || _selected is null
                ? []
                : ActiveProfileResolver.Resolve(_settings, _selected);
            // Freeze what V2Fly actually loads. Previews and external-file edits cannot
            // mutate this snapshot while the core is running.
            var activePath = await _store.CreateActiveConfigSnapshotAsync(configPath, SelectedConfigVersion);
            // The selected config may contain custom geosite/geoip references, not
            // only the default BypassCN routing presets.
            // Keep the baseline JSON for pending-change comparison. The temporary
            // active file may gain an optional local-only statistics endpoint.
            // External JSON is never altered, even in the active snapshot.
            var configJson = await File.ReadAllTextAsync(activePath);
            var trafficPort = 0;
            if (!launchedExternal)
            {
                try
                {
                    var candidate = CoreTrafficStatistics.FindLoopbackPort(
                        _settings.HttpPort, _settings.SocksPort, _settings.PacPort);
                    var statsJson = CoreTrafficStatistics.Enable(configJson, SelectedConfigVersion, candidate);
                    await File.WriteAllTextAsync(activePath, statsJson);
                    try
                    {
                        await _core.TestConfigAsync(CoreExecutable, activePath, SelectedConfigVersion);
                        trafficPort = candidate;
                    }
                    catch (Exception ex)
                    {
                        // Optional diagnostics must NEVER prevent the proxy from starting.
                        AppendLog("Traffic statistics disabled for this core: " + ex.Message);
                        await File.WriteAllTextAsync(activePath, configJson);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("Traffic statistics setup unavailable: " + ex.Message);
                    await File.WriteAllTextAsync(activePath, configJson);
                }
            }
            var missing = GeoDataAssets.MissingForConfig(configJson, CoreExecutable);
            if (missing.Count > 0)
                throw new FileNotFoundException("缺少 V2Fly Geo 数据文件：" + string.Join(", ", missing)
                    + "。请更新或安装对应的 v2ray-core 数据文件。");
            // Refresh managed rules before the PAC server and WinINET PAC URL become active.
            if (_settings.ProxyMode == "PAC" && _settings.SelectedPacFile.Equals("managed.pac", StringComparison.OrdinalIgnoreCase))
                await WriteManagedPacAsync();
            StatusLabel.Text = L("检查配置中…");
            await _core.StartAsync(CoreExecutable, activePath, SelectedConfigVersion);
            // Never activate Windows system proxy until the HTTP listener is accepting
            // connections. If the core fails, restore the previous proxy instead.
            // In Manual mode never touch Windows proxy settings, but both local
            // listeners should still be ready for apps explicitly configured by the user.
            await WaitForLocalHttpAsync(_settings.HttpPort);
            await WaitForLocalHttpAsync(_settings.SocksPort);
            _runningHttpPort = _settings.HttpPort;
            _runningPacPort = _settings.PacPort;
            if (_settings.ProxyMode == "PAC")
            {
                _pac.Start(_settings.PacPort, _settings.HttpPort, SelectedPacPath);
                _systemProxyApplied = true; // Set first so a partial WinINET write can be rolled back.
                _proxy.EnablePac(_settings.PacPort);
            }
            else if (_settings.ProxyMode == "Global")
            {
                _systemProxyApplied = true; // Set first so a partial WinINET write can be rolled back.
                _proxy.EnableGlobal(_settings.HttpPort, _settings.SocksPort);
            }
            // Manual: no registry writes and no PAC URL are applied.
            _runningProxyMode = _settings.ProxyMode;
            _runningConfigSnapshot = configJson;
            _runningConfigProfileId = _selected?.Id;
            _runningExternalFilePath = _settings.CustomConfigPath;
            SetRunningConnection(true, launchedProfiles, launchedExternal);
            StartTrafficMonitoring(trafficPort);
            StatusLabel.Text = L("已连接") + ": " + RunningServerLabel;
            if (_settings.ProxyMode == "PAC" &&
                _settings.SelectedPacFile.Equals("managed.pac", StringComparison.OrdinalIgnoreCase))
            {
                _pacTemplateStatusKey = "已生成并更新 Windows PAC 系统代理。浏览器可能需要重新发起请求以应用规则。";
                PacTemplateStatusLabel.Text = L(_pacTemplateStatusKey);
                RefreshPacFiles();
            }
            UpdateConnectionBanner(true);
        }
        catch
        {
            await StopCoreAndProxyAsync();
            StatusLabel.Text = L("未连接");
            UpdateConnectionBanner(false);
            throw;
        }
        finally { _expectedShutdown = false; _connectionGate.Release(); }
    }

    private async Task WaitForLocalHttpAsync(int port)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            if (!_core.IsRunning) throw new InvalidOperationException("代理内核已退出，未更新 Windows 系统代理。");
            using var client = new TcpClient();
            try
            {
                using var attempt = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
                await client.ConnectAsync(IPAddress.Loopback, port, attempt.Token);
                return;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                await Task.Delay(150);
            }
        }
        throw new TimeoutException($"本地 HTTP 代理 127.0.0.1:{port} 未就绪。已停止代理，避免 Windows 系统代理指向不可用端口。");
    }

    private async Task DisconnectAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            _expectedShutdown = true;
            await StopCoreAndProxyAsync();
            StatusLabel.Text = L("未连接");
            UpdateConnectionBanner(false);
        }
        finally { _expectedShutdown = false; _connectionGate.Release(); }
    }

    private async Task StopCoreAndProxyAsync()
    {
        try { await _core.StopAsync(); }
        finally
        {
            TryRestoreProxy();
            _pac.Stop();
            SetRunningConnection(false);
            _runningHttpPort = 0;
            _runningPacPort = 0;
            // Log persistence is handled by RuntimeLogStore's periodic 60-second
            // threshold check. Disconnect must not force a disk write. Users can
            // explicitly export logs when they need a partial batch saved.
        }
    }
    private void TryRestoreProxy()
    {
        if (!_systemProxyApplied) return; // Manual mode does not alter system settings.
        try
        {
            // Always undo the WinINET change owned by this connection. Disabling
            // the proxy would destroy a user's pre-existing corporate PAC/VPN settings.
            _proxy.Restore();
            _systemProxyApplied = false;
        }
        catch (Exception ex) { AppendLog("System proxy restore failed: " + ex.Message); }
    }


    private void StartTrafficMonitoring(int statsPort)
    {
        StopTrafficMonitoring();
        _runningTrafficApiPort = statsPort;
        _trafficStatsUnavailable = statsPort == 0;
        UpdateTrafficLabels();
        if (statsPort == 0) return;
        var cancellation = new CancellationTokenSource();
        var executable = CoreExecutable; // Snapshot the launched core, not a later editor setting.
        _trafficCancellation = cancellation;
        _ = PollTrafficAsync(cancellation, statsPort, executable);
    }

    private void StopTrafficMonitoring()
    {
        _trafficCancellation?.Cancel();
        _trafficCancellation = null;
        _runningTrafficApiPort = 0;
        _trafficSnapshot = null;
        _trafficStatsUnavailable = false;
        UpdateTrafficLabels();
    }

    private async Task PollTrafficAsync(CancellationTokenSource cancellation, int statsPort, string executable)
    {
        var token = cancellation.Token;
        var rates = new TrafficRateCalculator();
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var counters = await CoreTrafficStatistics.QueryAsync(executable, statsPort, token);
                    var snapshot = rates.Update(counters, Stopwatch.GetTimestamp());
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (!ReferenceEquals(_trafficCancellation, cancellation)) return;
                        _trafficStatsUnavailable = false;
                        _trafficSnapshot = snapshot;
                        UpdateTrafficLabels();
                    });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // No fake zero speed or flood of errors every polling interval.
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (!ReferenceEquals(_trafficCancellation, cancellation)) return;
                        _trafficStatsUnavailable = true;
                        _trafficSnapshot = null;
                        UpdateTrafficLabels();
                    });
                }
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { cancellation.Dispose(); }
    }

    private static string FormatTrafficBytes(double bytes, bool speed = false)
    {
        var unit = speed ? "B/s" : "B";
        if (bytes < 1024) return $"{bytes:0} {unit}";
        bytes /= 1024;
        unit = speed ? "KiB/s" : "KiB";
        if (bytes < 1024) return $"{bytes:0.0} {unit}";
        bytes /= 1024;
        unit = speed ? "MiB/s" : "MiB";
        if (bytes < 1024) return $"{bytes:0.0} {unit}";
        return $"{bytes / 1024:0.0} " + (speed ? "GiB/s" : "GiB");
    }

    private void UpdateTrafficLabels()
    {
        if (UploadSpeedLabel is null || DownloadSpeedLabel is null) return;
        var values = _trafficSnapshot;
        UploadSpeedLabel.Text = values is { } v ? FormatTrafficBytes(v.UplinkBytesPerSecond, true) : "—";
        DownloadSpeedLabel.Text = values is { } d ? FormatTrafficBytes(d.DownlinkBytesPerSecond, true) : "—";
        TrafficTotalsLabel.Text = values is { } total
            ? string.Format(UiStrings.Instance["TrafficSessionTotals"],
                FormatTrafficBytes(total.UplinkBytes), FormatTrafficBytes(total.DownlinkBytes))
            : "";
        TrafficAvailabilityLabel.Text = !_core.IsRunning ? "" : _trafficStatsUnavailable
            ? UiStrings.Instance["TrafficUnavailable"]
            : _runningTrafficApiPort > 0 && values is null ? UiStrings.Instance["TrafficCollecting"] : "";
    }

    private async void TestClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var config = await SaveAndGenerateAsync();
            AppendLog(_settings.UseCustomConfig
                ? "Validation source: external JSON (profile fields are not used)."
                : $"Validation source: generated {SelectedConfigVersion}; selected node: {_selected?.Name} ({_selected?.Protocol}).");
            await _core.TestConfigAsync(CoreExecutable, config, SelectedConfigVersion);
            StatusLabel.Text = $"{SelectedConfigVersion} · " + L("配置检查通过") + $" ({ConfigFlavor.Loader(SelectedConfigVersion)})";
        }, "配置检查失败");
    }

    private void RefreshRoutingModePresentation()
    {
        RefreshSmartUnmatchedVisibility();
        var mode = CurrentRouteMode;
        var custom = mode == "Custom";
        BuiltInRoutePreview.IsVisible = !custom;
        CustomRouterPanel.IsVisible = custom;
        if (custom)
        {
            RouteEmptyStateLabel.IsVisible = RulePicker.SelectedItem is not RoutingRuleSet;
            return;
        }
        // Built-in strategies are not saved as custom rule sets. Smart preview includes
        // the effective direct fallback, implemented by the first outbound in Smart mode.
        if (mode == "Smart")
        {
            BuiltInRouteSummary.Text = string.Join(Environment.NewLine,
                SmartRoutingPolicy.Describe(_settings, UiStrings.Instance.Language == "en-US"));
            return;
        }
        if (mode == "ProxyAll")
        {
            BuiltInRouteSummary.Text = UiStrings.Instance["RoutingProxyAllDefault"];
            return;
        }
        try
        {
            var previewSettings = new ClientSettings { RoutingMode = mode,
                GeoIpFile = _settings.GeoIpFile, Profiles = _settings.Profiles };
            var preview = V4ConfigBuilder.BuildRouting(previewSettings).ToJsonString();
            var summaries = VisualRoutingEditor.Summaries(preview);
            BuiltInRouteSummary.Text = summaries.Count == 0
                ? UiStrings.Instance["RoutingProxyAllDefault"]
                : string.Join(Environment.NewLine, summaries);
        }
        catch (Exception ex) { BuiltInRouteSummary.Text = ex.Message; }
    }

    private async void RoutingModeChanged(object sender, EventArgs e)
    {
        if (_loading || !_initialized) return;
        if (sender is not Picker picker || picker.SelectedItem is not LocalizedChoice value) return;
        _settings.RoutingMode = value.Value;
        var oldLoading = _loading;
        _loading = true;
        try
        {
            var choices = (RoutingModePicker.ItemsSource as IEnumerable<LocalizedChoice>)?.ToArray();
            var matching = choices?.FirstOrDefault(x => x.Value == value.Value);
            RoutingModePicker.SelectedItem = matching;
            RouteModeInRoutingPicker.SelectedItem = matching;
        }
        finally { _loading = oldLoading; }
        RefreshRoutingModePresentation();
        await RunSafelyAsync(() => _store.SaveAsync(_settings), UiStrings.Instance["RoutingRuleError"]);
        UpdatePendingCoreChanges();
    }

    private async void SmartFallbackChanged(object sender, EventArgs e)
    {
        if (_loading || !_initialized || sender is not Picker picker ||
            picker.SelectedItem is not LocalizedChoice choice) return;
        _settings.SmartUnmatchedRoute = choice.Value;
        RefreshSmartUnmatchedChoices();
        RefreshRoutingModePresentation();
        await RunSafelyAsync(() => _store.SaveAsync(_settings), UiStrings.Instance["RoutingRuleError"]);
        UpdatePendingCoreChanges();
    }

    private int SelectedVisualRuleIndex
    {
        get
        {
            if (VisualRuleList.SelectedItem is not string selected ||
                VisualRuleList.ItemsSource is not string[] rows) return -1;
            return Array.IndexOf(rows, selected);
        }
    }

    private void SetVisualRuleControls()
    {
        var index = SelectedVisualRuleIndex;
        var count = VisualRuleList.ItemsSource is string[] rows ? rows.Length : 0;
        var selected = index >= 0;
        RouteDetailPanel.IsVisible = selected || _creatingVisualRule;
        RouteDraftHelpLabel.IsVisible = _creatingVisualRule;
        MoveRuleUpButton.IsEnabled = selected && index > 0 && !_creatingVisualRule;
        MoveRuleDownButton.IsEnabled = selected && index < count - 1 && !_creatingVisualRule;
        DeleteVisualRuleButton.IsEnabled = selected && !_creatingVisualRule;
    }

    private void RefreshRules()
    {
        _loading = true;
        RulePicker.ItemsSource = null;
        RulePicker.ItemsSource = _settings.RoutingRuleSets;
        RulePicker.SelectedItem = _settings.RoutingRuleSets.FirstOrDefault(x => x.Id == _settings.SelectedRoutingRuleId)
            ?? _settings.RoutingRuleSets.FirstOrDefault();
        _loading = false;
        ShowSelectedRule();
    }
    private void ShowSelectedRule()
    {
        _updatingVisualRouting = true;
        _creatingVisualRule = false;
        try
        {
            if (RulePicker.SelectedItem is not RoutingRuleSet rule)
            {
                RuleNameEntry.Text = "";
                RoutingJsonEditor.Text = "";
                VisualRuleList.ItemsSource = Array.Empty<string>();
                VisualRuleList.SelectedItem = null;
                VisualRouteEditor.IsVisible = false;
                RouteEmptyStateLabel.IsVisible = true;
                RouteDetailPanel.IsVisible = false;
                RefreshRoutingModePresentation();
                return;
            }
            RouteEmptyStateLabel.IsVisible = false;
            RuleNameEntry.Text = rule.Name;
            RoutingJsonEditor.Text = rule.Json;
            RefreshRouteTargets();
        }
        finally { _updatingVisualRouting = false; }
        RefreshVisualRules(0);
        RefreshRoutingModePresentation();
    }

    private void RefreshRouteTargets()
    {
        var entries = new List<LocalizedChoice>
        {
            new("proxy", UiStrings.Instance["RoutingTargetSelected"]),
            new("direct", UiStrings.Instance["RoutingTargetDirect"]),
            new("block", UiStrings.Instance["RoutingTargetBlock"])
        };
        // The routing engine supports referencing a unique profile by its name.
        // Do not offer ambiguous names that the outbound planner cannot resolve.
        foreach (var group in _settings.Profiles.GroupBy(p => p.Name, StringComparer.Ordinal))
            if (group.Count() == 1 && !string.IsNullOrWhiteSpace(group.Key) &&
                group.Key is not ("main" or "proxy" or "direct" or "block" or "decline"))
                entries.Add(new LocalizedChoice(group.Key, group.Key));
        RouteTargetPicker.ItemsSource = entries;
    }

    private void RefreshVisualRules(int index)
    {
        if (RulePicker.SelectedItem is not RoutingRuleSet) return;
        _updatingVisualRouting = true;
        _creatingVisualRule = false;
        try
        {
            var json = RoutingJsonEditor.Text ?? VisualRoutingEditor.DefaultJson;
            var summaries = VisualRoutingEditor.Summaries(json).ToArray();
            VisualRuleList.ItemsSource = summaries;
            VisualRouteEditor.IsVisible = true;
            RouteStrategyPicker.SelectedIndex = VisualRoutingEditor.Strategy(json).ToLowerInvariant() switch
            {
                "ipifnonmatch" => 1, "ipondemand" or "useip" => 2, _ => 0
            };
            VisualRuleList.SelectedItem = summaries.Length == 0 ? null : summaries[Math.Clamp(index, 0, summaries.Length - 1)];
            RouteListEmptyLabel.IsVisible = summaries.Length == 0;
            LoadVisualRule();
        }
        catch (Exception ex)
        {
            // Unsupported advanced rules must remain safely editable as raw JSON.
            VisualRouteEditor.IsVisible = false;
            RouteRawModeSwitch.IsToggled = true;
            RoutingJsonEditor.IsVisible = true;
            AppendLog(UiStrings.Instance["RoutingRuleError"] + ": " + ex.Message);
        }
        finally { _updatingVisualRouting = false; }
    }

    private void LoadVisualRule()
    {
        var index = SelectedVisualRuleIndex;
        if (index < 0)
        {
            RouteDomainsEditor.Text = "";
            RouteIpsEditor.Text = "";
            RoutePortsEntry.Text = "";
            RouteNetworkPicker.SelectedItem = "tcp,udp";
            RouteTargetPicker.SelectedItem = (RouteTargetPicker.ItemsSource as IEnumerable<LocalizedChoice>)?
                .FirstOrDefault(t => t.Value == "proxy");
            SetVisualRuleControls();
            return;
        }
        var values = VisualRoutingEditor.Read(RoutingJsonEditor.Text ?? VisualRoutingEditor.DefaultJson, index);
        RouteDomainsEditor.Text = values.Domains;
        RouteIpsEditor.Text = values.Ips;
        RoutePortsEntry.Text = values.Ports;
        RouteNetworkPicker.SelectedItem = values.Network;
        var targets = RouteTargetPicker.ItemsSource as IEnumerable<LocalizedChoice>;
        var selectedTarget = targets?.FirstOrDefault(t => t.Value == values.Target);
        if (selectedTarget is null)
            throw new ArgumentException($"Routing outbound '{values.Target}' is not available. Use the raw JSON editor to repair this rule.");
        RouteTargetPicker.SelectedItem = selectedTarget;
        SetVisualRuleControls();
    }

    private void VisualRuleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingVisualRouting || _loading) return;
        try
        {
            _creatingVisualRule = false;
            LoadVisualRule();
        }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private async void RouteStrategyChanged(object sender, EventArgs e)
    {
        if (_updatingVisualRouting || _loading || RulePicker.SelectedItem is not RoutingRuleSet ruleSet) return;
        await RunSafelyAsync(async () =>
        {
            var json = VisualRoutingEditor.SetStrategy(
                RoutingJsonEditor.Text ?? VisualRoutingEditor.DefaultJson,
                RouteStrategyPicker.SelectedItem as string ?? "AsIs");
            RoutingJsonEditor.Text = json;
            ruleSet.Json = json;
            ActivateCustomRouting();
            await _store.SaveAsync(_settings);
        }, UiStrings.Instance["RoutingRuleError"]);
    }

    private void RouteRawModeToggled(object sender, ToggledEventArgs args)
        => RoutingJsonEditor.IsVisible = args.Value;

    private VisualRoutingEditor.Rule GetVisualForm() => new()
    {
        Target = (RouteTargetPicker.SelectedItem as LocalizedChoice)?.Value ?? "proxy",
        Domains = RouteDomainsEditor.Text ?? "",
        Ips = RouteIpsEditor.Text ?? "",
        Ports = RoutePortsEntry.Text ?? "",
        Network = RouteNetworkPicker.SelectedItem as string ?? "tcp,udp"
    };

    private void ActivateCustomRouting()
    {
        _settings.RoutingMode = "Custom";
        var choices = RoutingModePicker.ItemsSource as IEnumerable<LocalizedChoice>;
        var selected = choices?.FirstOrDefault(c => c.Value == "Custom");
        if (selected is null) return;
        var previous = _loading;
        _loading = true;
        try
        {
            RoutingModePicker.SelectedItem = selected;
            RouteModeInRoutingPicker.SelectedItem = selected;
        }
        finally { _loading = previous; }
        RefreshRoutingModePresentation();
    }

    private async Task ApplyVisualEditAsync(Func<string, string> edit, int nextIndex)
    {
        await RunSafelyAsync(async () =>
        {
            var ruleSet = RulePicker.SelectedItem as RoutingRuleSet ??
                throw new ArgumentException("Select a routing rule set first.");
            var json = edit(RoutingJsonEditor.Text ?? VisualRoutingEditor.DefaultJson);
            // Save immediately after a visual action so switching rule sets does
            // not discard edits. Raw JSON still requires the explicit Save button.
            ruleSet.Json = json;
            RoutingJsonEditor.Text = json;
            _settings.SelectedRoutingRuleId = ruleSet.Id;
            ActivateCustomRouting();
            await _store.SaveAsync(_settings);
            UpdatePendingCoreChanges();
            _creatingVisualRule = false;
            RefreshVisualRules(nextIndex);
            StatusLabel.Text = UiStrings.Instance["RoutingRuleSaved"];
        }, UiStrings.Instance["RoutingRuleError"]);
    }

    private void AddVisualRuleClicked(object sender, EventArgs e)
    {
        if (RulePicker.SelectedItem is not RoutingRuleSet) return;
        _creatingVisualRule = true;
        _updatingVisualRouting = true;
        try
        {
            VisualRuleList.SelectedItem = null;
            LoadVisualRule();
        }
        finally { _updatingVisualRouting = false; }
    }
    private async void ApplyVisualRuleClicked(object sender, EventArgs e)
    {
        var index = SelectedVisualRuleIndex;
        if (index < 0 && !_creatingVisualRule)
        {
            await DisplayAlert("VelaRay", UiStrings.Instance["RoutingChooseRule"], "OK");
            return;
        }
        var values = GetVisualForm();
        if (string.IsNullOrWhiteSpace(values.Domains) &&
            string.IsNullOrWhiteSpace(values.Ips) && string.IsNullOrWhiteSpace(values.Ports))
        {
            await DisplayAlert("VelaRay", UiStrings.Instance["RoutingRuleNeedsCondition"], "OK");
            return;
        }
        if (_creatingVisualRule)
            index = VisualRoutingEditor.Count(RoutingJsonEditor.Text ?? VisualRoutingEditor.DefaultJson);
        var chosenIndex = index;
        await ApplyVisualEditAsync(json => VisualRoutingEditor.Upsert(json, chosenIndex, values), chosenIndex);
    }
    private async void RemoveVisualRuleClicked(object sender, EventArgs e)
    {
        var index = SelectedVisualRuleIndex;
        if (index < 0 || _creatingVisualRule) return;
        await ApplyVisualEditAsync(json => VisualRoutingEditor.Remove(json, index), Math.Max(0, index - 1));
    }
    private async void MoveVisualRuleUpClicked(object sender, EventArgs e) => await MoveSelectedVisualRuleAsync(-1);
    private async void MoveVisualRuleDownClicked(object sender, EventArgs e) => await MoveSelectedVisualRuleAsync(1);
    private async Task MoveSelectedVisualRuleAsync(int offset)
    {
        await RunSafelyAsync(async () =>
        {
            var index = SelectedVisualRuleIndex;
            var count = VisualRoutingEditor.Count(RoutingJsonEditor.Text ?? VisualRoutingEditor.DefaultJson);
            if (index < 0 || index + offset < 0 || index + offset >= count) return;
            await ApplyVisualEditAsync(json => VisualRoutingEditor.Move(json, index, offset), index + offset);
        }, UiStrings.Instance["RoutingRuleError"]);
    }

    private async void RulePickerChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        if (RulePicker.SelectedItem is RoutingRuleSet rule) _settings.SelectedRoutingRuleId = rule.Id;
        ShowSelectedRule();
        await RunSafelyAsync(() => _store.SaveAsync(_settings), UiStrings.Instance["RoutingRuleError"]);
    }
    private async void AddRuleClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var rule = new RoutingRuleSet
            {
                Name = "Custom " + (_settings.RoutingRuleSets.Count + 1),
                Json = VisualRoutingEditor.DefaultJson
            };
            _settings.RoutingRuleSets.Add(rule);
            _settings.SelectedRoutingRuleId = rule.Id;
            ActivateCustomRouting();
            await _store.SaveAsync(_settings);
            UpdatePendingCoreChanges();
            RefreshRules();
            RouteRawModeSwitch.IsToggled = false;
        }, UiStrings.Instance["RoutingRuleError"]);
    }
    private async void SaveRuleClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var rule = RulePicker.SelectedItem as RoutingRuleSet ?? throw new ArgumentException("Select a routing rule set first.");
            var candidate = V5ConfigBuilder.ParseObject(RoutingJsonEditor.Text ?? "", "routing rules");
            if (ConfigFlavor.Normalize(_selected?.ConfigVersion) == ConfigFlavor.V4 && candidate["rules"] is null)
                throw new ArgumentException("V4 routing must contain 'rules'; V5-only 'rule' cannot be used directly.");
            if (ConfigFlavor.Normalize(_selected?.ConfigVersion) == ConfigFlavor.V5)
                V5ConfigBuilder.ConvertRoutingToV5(candidate);
            rule.Name = RuleNameEntry.Text?.Trim() ?? "Custom";
            rule.Json = RoutingJsonEditor.Text ?? VisualRoutingEditor.DefaultJson;
            _settings.SelectedRoutingRuleId = rule.Id;
            ActivateCustomRouting();
            await _store.SaveAsync(_settings);
            UpdatePendingCoreChanges();
            RefreshRules();
            StatusLabel.Text = L("路由规则已保存");
        }, "路由规则错误");
    }
    private async void DeleteRuleClicked(object sender, EventArgs e)
    {
        if (RulePicker.SelectedItem is not RoutingRuleSet rule) return;
        _settings.RoutingRuleSets.Remove(rule);
        _settings.SelectedRoutingRuleId = _settings.RoutingRuleSets.FirstOrDefault()?.Id;
        if (_settings.RoutingRuleSets.Count == 0) _settings.RoutingMode = "Smart";
        RefreshRules(); ShowOptions();
        await _store.SaveAsync(_settings);
            UpdatePendingCoreChanges();
    }

    private string SelectedPacPath
    {
        get
        {
            var name = SafePacName(_settings.SelectedPacFile);
            return Path.Combine(_store.PacDirectory, name);
        }
    }
    private static string SafePacName(string name)
    {
        var leaf = Path.GetFileName(name);
        if (leaf != name || leaf is "" or "." or ".." || !leaf.EndsWith(".pac", StringComparison.OrdinalIgnoreCase) && !leaf.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PAC 文件名仅支持当前目录下的 .pac 或 .js。");
        return leaf;
    }
    private void RefreshPacFiles()
    {
        Directory.CreateDirectory(_store.PacDirectory);
        if (!File.Exists(_store.PacFilePath)) File.WriteAllText(_store.PacFilePath, PacHttpServer.DefaultScript(_settings.HttpPort));
        var managed = Path.Combine(_store.PacDirectory, "managed.pac");
        if (!File.Exists(managed)) File.WriteAllText(managed, PacTemplateGenerator.Generate(SmartRoutingPolicy.EffectiveManagedPacTemplate(_settings),
            _settings.PacDirectWebsites, _settings.PacProxyWebsites, _settings.HttpPort));
        var names = Directory.GetFiles(_store.PacDirectory).Where(f => f.EndsWith(".pac", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName).Where(x => x != null).OrderBy(x => x).ToArray();
        if (!names.Contains(_settings.SelectedPacFile, StringComparer.OrdinalIgnoreCase)) _settings.SelectedPacFile = "managed.pac";
        _loading = true;
        PacPicker.ItemsSource = names;
        PacPicker.SelectedItem = _settings.SelectedPacFile;
        _loading = false;
        LoadPacFile();
    }
    private void LoadPacFile()
    {
        PacNameEntry.Text = _settings.SelectedPacFile;
        PacScriptEditor.Text = File.Exists(SelectedPacPath) ? File.ReadAllText(SelectedPacPath) : PacHttpServer.DefaultScript(_settings.HttpPort);
    }
    private void PacPickerChanged(object sender, EventArgs e)
    {
        if (_loading || PacPicker.SelectedItem is not string filename) return;
        _settings.SelectedPacFile = filename;
        LoadPacFile();
    }
    private async void AddPacClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var name = SafePacName(PacNameEntry.Text?.Trim() ?? "");
            var path = Path.Combine(_store.PacDirectory, name);
            if (File.Exists(path)) throw new IOException("已存在同名 PAC 文件。");
            await File.WriteAllTextAsync(path, PacHttpServer.DefaultScript(_settings.HttpPort));
            _settings.SelectedPacFile = name;
            await _store.SaveAsync(_settings);
            RefreshPacFiles();
        }, "新增 PAC 失败");
    }
    private async void SavePacClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            // Managed PAC may feed Smart routing: a raw edit can bypass the
            // generator's domain priority. Do not hot-replace its running file.
            // User can edit custom PAC scripts or apply the managed PAC lists
            // through ApplyPacTemplateClicked (which synchronizes core changes).
            if (_core.IsRunning && _runningProxyMode == "PAC" &&
                _settings.SelectedPacFile.Equals("managed.pac", StringComparison.OrdinalIgnoreCase) &&
                _settings.RoutingMode == "Smart")
            {
                StatusLabel.Text = UiStrings.Instance["ManagedPacRawLocked"];
                return;
            }
            await File.WriteAllTextAsync(SelectedPacPath, PacScriptEditor.Text ?? "");
            await _store.SaveAsync(_settings);
            // PAC server serves file afresh on every request; refresh WinINET PAC mode when active.
            if (_core.IsRunning && _settings.ProxyMode == "PAC")
            {
                _pac.ChangeFile(SelectedPacPath);
                _proxy.EnablePac(_runningPacPort);
            }
            StatusLabel.Text = L("PAC 已保存");
        }, "PAC 保存失败");
    }
    private void RefreshPacClicked(object sender, EventArgs e) => RefreshPacFiles();

    private void CapturePacTemplateOptions()
    {
        _settings.PacTemplate = (PacTemplatePicker.SelectedItem as PacTemplateChoice)?.Key ?? PacTemplateGenerator.DirectByDefault;
        _settings.PacDirectWebsites = PacDirectSitesEditor.Text ?? "";
        _settings.PacProxyWebsites = PacProxySitesEditor.Text ?? "";
    }

    private string BuildManagedPac()
    {
        CapturePacTemplateOptions();
        return PacTemplateGenerator.Generate(SmartRoutingPolicy.EffectiveManagedPacTemplate(_settings),
            _settings.PacDirectWebsites, _settings.PacProxyWebsites,
            _core.IsRunning && _runningHttpPort > 0 ? _runningHttpPort : Port(HttpPortEntry.Text, "HTTP"));
    }

    private async Task WriteManagedPacAsync()
    {
        var script = BuildManagedPac();
        var path = Path.Combine(_store.PacDirectory, "managed.pac");
        Directory.CreateDirectory(_store.PacDirectory);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, script);
        File.Move(tmp, path, overwrite: true);
    }

    private async void PreviewPacTemplateClicked(object sender, EventArgs e) => await RunSafelyAsync(async () =>
    {
        var script = BuildManagedPac();
        await Navigation.PushModalAsync(new PacPreviewPage(script));
    }, "PAC 模板预览失败");

    private async void ApplyPacTemplateClicked(object sender, EventArgs e) => await RunSafelyAsync(async () =>
    {
        CapturePacTemplateOptions();
        _settings.SelectedPacFile = "managed.pac";
        await _store.SaveAsync(_settings);
        // Changes to Smart PAC domains affect the generated V2Fly routing too.
        // Keep serving the old managed.pac until a user-approved reconnect.
        if (UpdatePendingCoreChanges())
        {
            _pacTemplateStatusKey = "RoutingPendingPacDeferred";
            PacTemplateStatusLabel.Text = UiStrings.Instance["RoutingPendingPacDeferred"];
            StatusLabel.Text = UiStrings.Instance["RoutingPendingPacDeferred"];
            return;
        }
        await WriteManagedPacAsync();
        RefreshPacFiles();
        if (_core.IsRunning && _settings.ProxyMode == "PAC")
        {
            _pac.ChangeFile(SelectedPacPath);
            _proxy.EnablePac(_runningPacPort);
        }
        _pacTemplateStatusKey = _core.IsRunning && _settings.ProxyMode == "PAC"
            ? "已生成并更新 Windows PAC 系统代理。浏览器可能需要重新发起请求以应用规则。"
            : "已保存 managed.pac。选择 PAC 模式并连接代理后生效。";
        PacTemplateStatusLabel.Text = _pacTemplateStatusKey == "RoutingPendingPacDeferred"
            ? UiStrings.Instance["RoutingPendingPacDeferred"] : L(_pacTemplateStatusKey);
        StatusLabel.Text = L("PAC 规则已生成");
        RefreshRoutingModePresentation();
    }, "生成 PAC 失败");

    private async void PickCustomClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = L("选择 jsonv5 配置") });
            if (pick is null) return;
            // Validate first, then copy to configs/custom. Store a relative path so
            // moving the entire portable VelaRay folder does not break this setting.
            var original = await File.ReadAllTextAsync(pick.FullPath);
            V5ConfigBuilder.ParseObject(original, "external config");
            var relative = await _store.ImportCustomConfigAsync(pick.FullPath);
            _settings.CustomConfigPath = relative;
            CustomConfigLabel.Text = relative;
            await _store.SaveAsync(_settings);
        }, "导入自定义配置失败");
    }
    private void OpenDataClicked(object sender, EventArgs e) => OpenDirectory(_store.DirectoryPath);
    private static void OpenDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }
    private void ClearLogClicked(object sender, EventArgs e)
    {
        // "Clear" removes the window's history; it never truncates disk logs.
        _runtimeLogs.ClearVisible();
        Interlocked.Exchange(ref _logUiDirty, 1);
        RefreshLogView();
    }

    private async void ExportLogClicked(object sender, EventArgs e)
    {
        await RunSafelyAsync(() =>
        {
            var path = _runtimeLogs.ExportCurrentFile();
            return DisplayAlert(L("日志"), L("保存至：") + path, "OK");
        }, "导出日志失败");
    }

    private void AppendLog(string line)
    {
        // Hide VelaRay's own local statistics API access noise from both the UI and
        // disk buffer. Do not suppress other core messages (including API errors).
        if (line.Contains("tcp:127.0.0.1:0 [velaray-stats-api]", StringComparison.OrdinalIgnoreCase))
            return;

        // Can be called by v2ray stdout/stderr background threads. Queue only;
        // a single worker checks for >=1,000 entries once per minute without blocking here.
        _runtimeLogs.Append(line);
        Interlocked.Exchange(ref _logUiDirty, 1);
    }

    private void StartLogRefreshTimer()
    {
        if (_logRefreshTimerStarted) return;
        _logRefreshTimerStarted = true;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(500), () =>
        {
            if (_logsDisposed) return false;
            if (Interlocked.Exchange(ref _logUiDirty, 0) != 0)
                RefreshLogView();
            return true;
        });
    }

    private void RefreshLogView()
    {
        // SnapshotVisible is bounded to 1,000 lines, regardless of uptime.
        LogTextLabel.Text = string.Join(Environment.NewLine, _runtimeLogs.SnapshotVisible());
        // Scroll after the content has been measured, not before the layout pass.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(40), ScrollLogToBottom);
    }

    private void ScrollLogToBottom()
    {
        if (_logsDisposed || LogScrollView.Height <= 0) return;
        _ = LogScrollView.ScrollToAsync(LogTailAnchor, ScrollToPosition.End, false);
    }

    public void DisposeLogging()
    {
        StopTrafficMonitoring();
        _logsDisposed = true;
        _core.LogReceived -= AppendLog;
        _runtimeLogs.Dispose();
    }

    private static async Task AlertAsync(string title, Exception ex) => await MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not null) await page.DisplayAlert(UiStrings.Instance.Translate(title), UiStrings.Instance.TranslateDiagnostic(ex.Message), "OK");
    });
    private async Task RunSafelyAsync(Func<Task> work, string title)
    {
        try { await work(); }
        catch (Exception ex) { AppendLog(L(title) + ": " + UiStrings.Instance.TranslateDiagnostic(ex.Message)); await AlertAsync(title, ex); }
    }
}
