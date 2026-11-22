using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using VelaRay.Core.Configuration;
using VelaRay.Core.Models;
using VelaRay.Core.Pac;
using VelaRay.Core.Platform;
using VelaRay.Core.Runtime;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}
static JsonNode Config(ClientSettings o, ServerProfile p) => JsonNode.Parse(V5ConfigBuilder.Build(o, p))!;


// Runtime logging is intentionally best-effort: at most 1,000 lines in the UI,
// at most 2,048 queued for disk, and no automatic I/O for partial batches.
Check(RuntimeLogStore.MaxVisibleLines == 1_000 &&
      RuntimeLogStore.DefaultMinimumWriteBatchLines == 1_000 &&
      RuntimeLogStore.DefaultFlushInterval == TimeSpan.FromMinutes(1),
      "runtime log defaults: 1k visible, 1k write threshold, 60-second disk checks");
var logTestRoot = Path.Combine(Path.GetTempPath(), "VelaRayLogs-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(logTestRoot);
try
{
    var automaticLogFile = Path.Combine(logTestRoot, "threshold.log");
    using (var logs = new RuntimeLogStore(automaticLogFile,
        flushInterval: TimeSpan.FromMilliseconds(75),
        maxPendingLines: 5, minimumWriteBatchLines: 3))
    {
        logs.Append("only first");
        logs.Append("only second");
        await Task.Delay(300);
        Check(!File.Exists(automaticLogFile) && logs.PendingCount == 2,
              "periodic logging skips a batch below the threshold");
        logs.Append("threshold reached");
        for (var i = 0; i < 60; i++)
        {
            if (File.Exists(automaticLogFile) &&
                File.ReadAllText(automaticLogFile).Contains("threshold reached")) break;
            await Task.Delay(50);
        }
        Check(File.Exists(automaticLogFile) &&
              File.ReadAllText(automaticLogFile).Contains("threshold reached"),
              "periodic logging writes a complete batch asynchronously");
        logs.Append("last unflushed entry");
        Check(logs.VisibleCount == 4, "recent runtime log entries remain visible");
    }
    Check(!File.ReadAllText(automaticLogFile).Contains("last unflushed entry"),
          "normal shutdown deliberately drops sub-threshold disk entries");

    var explicitLogFile = Path.Combine(logTestRoot, "manual.log");
    using (var logs = new RuntimeLogStore(explicitLogFile,
        flushInterval: TimeSpan.FromHours(1),
        maxPendingLines: 1_100, minimumWriteBatchLines: 1_000))
    {
        logs.Append("explicit export of one entry");
        var exported = logs.ExportCurrentFile();
        Check(File.ReadAllText(exported).Contains("explicit export of one entry"),
              "manual Export logs writes even when fewer than 1k entries exist");
        for (var i = 0; i < 1_005; i++) logs.Append("bounded-" + i);
        Check(logs.VisibleCount == 1_000 && logs.SnapshotVisible().Length == 1_000,
              "runtime UI history never exceeds 1k lines");
        Check(logs.PendingCount == 1_005,
              "large pending batches are queued without synchronous producer disk I/O");
    }

    // File persistence can be switched off while retaining only the latest UI lines.
    // Nothing should be written during a disabled session, including on shutdown.
    var disabledFile = Path.Combine(logTestRoot, "disabled.log");
    using (var logs = new RuntimeLogStore(disabledFile,
        flushInterval: TimeSpan.FromMilliseconds(50),
        maxPendingLines: 5, minimumWriteBatchLines: 3,
        enableFileLogging: false))
    {
        Check(!logs.FileLoggingEnabled, "disk logging can start disabled");
        for (var i = 0; i < 1_250; i++) logs.Append("UI only " + i);
        await Task.Delay(175);
        Check(logs.VisibleCount == 1_000 && logs.PendingCount == 0 && !File.Exists(disabledFile),
            "disabled logging retains only last 1k UI lines, never opens a log file");
        try
        {
            _ = logs.ExportCurrentFile();
            throw new InvalidOperationException("Export unexpectedly wrote while logging disabled");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("File logging is disabled"))
        {
            Check(true, "disabled logging blocks on-demand file export");
        }
        logs.SetFileLoggingEnabled(true);
        logs.Append("new persisted entry one");
        logs.Append("new persisted entry two");
        logs.SetFileLoggingEnabled(false);
        await Task.Delay(175);
        Check(!File.Exists(disabledFile) && logs.PendingCount == 0,
            "turning off disk logging drops pending partial batches");
        logs.SetFileLoggingEnabled(true);
        logs.Append("new persisted entry three");
        logs.Append("new persisted entry four");
        logs.Append("new persisted entry five");
        for (var i = 0; i < 40 && !File.Exists(disabledFile); i++)
            await Task.Delay(50);
        Check(File.Exists(disabledFile), "re-enabling disk logging persists a new complete batch");
        var text = File.ReadAllText(disabledFile);
        Check(text.Contains("new persisted entry five") && !text.Contains("new persisted entry one") &&
              !text.Contains("UI only"), "re-enabled logging never backfills disabled or dropped entries");
    }

    var overflowLogFile = Path.Combine(logTestRoot, "bounded.log");
    using (var logs = new RuntimeLogStore(overflowLogFile,
        flushInterval: TimeSpan.FromHours(1),
        maxPendingLines: 3, minimumWriteBatchLines: 3))
    {
        for (var i = 0; i < 8; i++) logs.Append("overflow-" + i);
        Check(logs.PendingCount == 3, "pending disk queue stays within its capacity");
    }
    var overflowData = File.ReadAllText(overflowLogFile);
    Check(overflowData.Contains("5 entries dropped") && overflowData.Contains("overflow-7"),
          "overflow is recorded when the full batch is eventually written");
}
finally
{
    Directory.Delete(logTestRoot, recursive: true);
}

// Portable V2Fly location: independent of the Windows UI and safe for future macOS/Linux frontends.
var currentExe = Environment.ProcessPath ?? Path.Combine(Path.GetTempPath(), "VelaRayTestHost");
var platformDataDir = DesktopDataDirectories.DefaultForCurrentPlatform(currentExe);
Check(Path.IsPathFullyQualified(platformDataDir), "platform settings folder is absolute");
if (OperatingSystem.IsWindows())
    Check(platformDataDir == Path.GetDirectoryName(Path.GetFullPath(currentExe)),
          "Windows preserves portable settings beside the exe");
else if (OperatingSystem.IsMacOS())
    Check(platformDataDir.EndsWith(Path.Combine("Application Support", "VelaRay")),
          "Mac Catalyst uses the user's writable Application Support folder");
else if (OperatingSystem.IsLinux())
    Check(platformDataDir.EndsWith("velaray"), "Linux frontend would use XDG config folder");
var locator = new PortableCoreLocator();
var portableRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "VelaRayPortableLocation"));
var expectedExecutable = OperatingSystem.IsWindows() ? "v2ray.exe" : "v2ray";
Check(locator.Locate(portableRoot, null) == Path.Combine(portableRoot, "v2ray-core", expectedExecutable),
      "portable V2Fly core path is OS-aware");
Check(locator.Locate(portableRoot, "custom" + Path.DirectorySeparatorChar + expectedExecutable) ==
      Path.Combine(portableRoot, "custom", expectedExecutable), "relative override resolves beside the application");

// Smart node probe classifies by TRANSPORT, not by guessed TCP support.
var tcpProbeNode = new ServerProfile { Protocol = "vmess", Transport = "tcp", Address = "tcp.example.org", Port = 443 };
var mkcpProbeNode = new ServerProfile { Protocol = "vmess", Transport = "mkcp", Address = "udp.example.org", Port = 443, Uuid = Guid.NewGuid().ToString() };
var quicProbeNode = new ServerProfile { Protocol = "trojan", Transport = "quic", Address = "quic.example.org", Port = 443, Password = "secret" };
var hy2ProbeNode = new ServerProfile { Protocol = "hysteria2", Transport = "hysteria2", Address = "hy2.example.org", Port = 443, TransportPassword = "test-secret" };
Check(SmartNodeLatencyProbe.Classify(tcpProbeNode) == NodeProbeTransport.Tcp,
      "TCP transport keeps TCP connect probe");
Check(SmartNodeLatencyProbe.Classify(mkcpProbeNode) == NodeProbeTransport.Udp &&
      SmartNodeLatencyProbe.Classify(quicProbeNode) == NodeProbeTransport.Udp &&
      SmartNodeLatencyProbe.Classify(hy2ProbeNode) == NodeProbeTransport.Udp,
      "mKCP / QUIC / native Hysteria2 use isolated UDP transport health checks");
Check(SmartNodeLatencyProbe.Classify(new ServerProfile { Transport = "invalid" }) == NodeProbeTransport.Unsupported,
      "unsupported transports are not silently probed as TCP");
Check(SmartNodeLatencyProbe.MaxUdpAttempts == 2 &&
      SmartNodeLatencyProbe.UdpRequestTimeout == TimeSpan.FromSeconds(5) &&
      SmartNodeLatencyProbe.MaxConcurrentProbes == 2,
      "UDP node probes are time-bounded and have limited concurrency");
foreach (var version in new[] { ConfigFlavor.V4, ConfigFlavor.V5 })
{
    hy2ProbeNode.ConfigVersion = version;
    var isolated = JsonNode.Parse(SmartNodeLatencyProbe.BuildIsolatedConfig(hy2ProbeNode, 23456))!;
    var inbound = isolated["inbounds"]!.AsArray();
    var outbound = isolated["outbounds"]!.AsArray();
    Check(inbound.Count == 1 && (string?)inbound[0]!["listen"] == "127.0.0.1" &&
          (string?)inbound[0]!["protocol"] == "http" && (int?)inbound[0]!["port"] == 23456,
          version + " probe HTTP inlet binds loopback only");
    Check(outbound.Count == 1 && (string?)outbound[0]!["protocol"] == "hysteria2" &&
          (string?)outbound[0]!["tag"] == "latency-probe-out",
          version + " probe has exactly the selected proxy outbound and no freedom fallback");
    Check(isolated["routing"] is null && isolated["router"] is null,
          version + " probe does not inherit routing or PAC rules");
}
Check(!System.Text.Json.JsonSerializer.Serialize(hy2ProbeNode).Contains("ProbeMethodDisplay") &&
      !System.Text.Json.JsonSerializer.Serialize(hy2ProbeNode).Contains("ProbeStatusText"),
      "probing visual state is not stored in server settings");

// TCP probe is a real network connect, but never requires starting V2Fly.
using (var lifetime = new ProbeTestProcessLifetime())
{
    var probe = new SmartNodeLatencyProbe(lifetime);
    var localListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    localListener.Start();
    try
    {
        var localPort = ((System.Net.IPEndPoint)localListener.LocalEndpoint).Port;
        var localNode = new ServerProfile { Protocol = "vmess", Transport = "tcp", Address = "127.0.0.1", Port = localPort };
        var actual = await probe.ProbeAsync(localNode, string.Empty);
        Check(actual.Outcome == NodeProbeOutcome.Success && actual.Milliseconds is >= 1 &&
              actual.Transport == NodeProbeTransport.Tcp, "TCP probe connects to a live local listener");
    }
    finally { localListener.Stop(); }
    var missingCore = await probe.ProbeAsync(hy2ProbeNode, Path.Combine(Path.GetTempPath(), "nonexistent-velaray-core.exe"));
    Check(missingCore.Outcome == NodeProbeOutcome.CoreUnavailable &&
          missingCore.Transport == NodeProbeTransport.Udp,
          "UDP probe reports missing core rather than a fake RTT");
}

// Reference-card properties are live-only and support the language/selection UI.
var cardProfile = new ServerProfile { Name = "Example node", Protocol = "vmess", Address = "edge.example.net", Port = 443 };
Check(cardProfile.ProtocolBadge == "vmess" && cardProfile.EndpointDisplay == "edge.example.net:443",
      "server card exposes protocol badge and host:port address");
var selectionNotifications = 0;
cardProfile.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(ServerProfile.IsSelected)) selectionNotifications++;
};
Check(!cardProfile.IsSelected && !cardProfile.IsConnected,
      "server selection and running state initially stay independent");
cardProfile.IsSelected = true;
cardProfile.IsSelected = true;
Check(cardProfile.IsSelected && !cardProfile.IsConnected && selectionNotifications == 1,
      "selected border state notifies the UI once without changing the running node");
Check(!JsonSerializer.Serialize(cardProfile).Contains("IsSelected"),
      "initial server selection visual state is not persisted to node settings");
cardProfile.IsSelected = false;
Check(!cardProfile.IsSelected && selectionNotifications == 2,
      "deselecting the card restores its normal border immediately");
Check(cardProfile.LatencyDisplay == "—", "unmeasured latency is never a fabricated number");
cardProfile.TcpLatencyMs = 28;
Check(cardProfile.LatencyDisplay == "28 ms", "measured TCP connection time is formatted in milliseconds");
var probeFailureStateNotified = false;
cardProfile.PropertyChanged += (_, args) =>
{
    if (args.PropertyName == nameof(ServerProfile.IsProbeFailure)) probeFailureStateNotified = true;
};
cardProfile.MeasuredLatencyMs = null;
cardProfile.ProbeStatusText = "超时";
cardProfile.IsProbeFailure = true;
Check(cardProfile.LatencyDisplay == "超时" && cardProfile.IsProbeFailure && probeFailureStateNotified,
      "failed probes display Timeout with an observable red visual state");
Check(!JsonSerializer.Serialize(cardProfile).Contains("IsProbeFailure"),
      "probe failure visual state is not saved into the server configuration");
cardProfile.IsProbeFailure = false;
cardProfile.ProbeStatusText = "";
Check(!JsonSerializer.Serialize(cardProfile).Contains("TcpLatencyMs") &&
      !JsonSerializer.Serialize(cardProfile).Contains("EndpointDisplay"),
      "server card visual-only fields are never persisted");

// Routing rules are independent of the server-card view model; this catches
// accidental pasting of PropertyChanged/Protocol/Address/Port into RoutingRuleSet.
var routingModel = new RoutingRuleSet { Name = "Custom routing", Json = "{\"rule\":[]}" };
Check(routingModel.Name == "Custom routing" && !string.IsNullOrWhiteSpace(routingModel.Id),
      "routing rule model keeps its persisted Id/Name/Json fields");
var routingFields = typeof(RoutingRuleSet).GetProperties()
    .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
Check(routingFields.SequenceEqual(new[] { "Id", "Json", "Name" }),
      "routing rule model cannot accidentally acquire server-card UI properties");

// Running core and PAC must not diverge when Smart routing rules change.
var liveNode = new ServerProfile { Address = "smart.example.org", TransportPassword = "password" };
var liveSettings = new ClientSettings { Profiles = [liveNode], RoutingMode = "Smart", ProxyMode = "PAC" };
var liveJson = ConfigFlavor.Build(liveSettings, liveNode);
bool Pending() => RunningConfigChangeDetector.NeedsReconnect(liveSettings, liveJson,
    liveNode.Id, false, null, "PAC", liveSettings.PacPort);
Check(!Pending(), "freshly started Smart core and managed PAC have no pending routing changes");
liveSettings.PacTemplate = PacTemplateGenerator.ChinaSitesDirect;
Check(!Pending(), "PAC-only template change is eligible for hot reload");
liveSettings.PacDirectWebsites = "example.com";
Check(Pending(), "Smart PAC domain change is deferred until reconnect");
liveSettings.PacDirectWebsites = "";
liveSettings.SmartUnmatchedRoute = SmartRoutingPolicy.UnmatchedProxy;
Check(Pending(), "Smart unmatched Proxy fallback requires explicit reconnect");
liveSettings.SmartUnmatchedRoute = SmartRoutingPolicy.UnmatchedDirect;
liveSettings.RoutingMode = "ProxyAll";
Check(Pending(), "advanced core routing change is marked pending");
liveSettings.RoutingMode = "Smart";
Check(!Pending(), "restoring the running configuration clears pending state");
liveSettings.ProxyMode = "Global";
Check(Pending(), "switching Smart PAC to Global forces ProxyAll and requires reconnect");
liveSettings.ProxyMode = "PAC";
Check(!Pending(), "returning from Global to previously running Smart PAC clears pending state");
// A Save-triggered mode change may auto-restart ONLY if no unrelated settings
// or hidden Smart/Custom/PAC draft is applied with it.
var launchBaseline = JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(liveSettings))!;
liveSettings.ProxyMode = "Global";
Check(RunningConfigChangeDetector.IsModeOnlyChange(liveSettings, launchBaseline, liveJson,
    liveNode.Id, false, "PAC", launchBaseline.PacPort),
    "PAC Smart -> Global is safe to auto-restart with the originally running node");
liveSettings.SmartUnmatchedRoute = SmartRoutingPolicy.UnmatchedProxy;
Check(!RunningConfigChangeDetector.IsModeOnlyChange(liveSettings, launchBaseline, liveJson,
    liveNode.Id, false, "PAC", launchBaseline.PacPort),
    "pending Smart rule edits prevent automatic mode switch restart");
liveSettings.SmartUnmatchedRoute = launchBaseline.SmartUnmatchedRoute;
liveSettings.ProxyMode = "PAC";
var globalBaseline = JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(liveSettings))!;
globalBaseline.ProxyMode = "Global";
var globalJson = ConfigFlavor.Build(globalBaseline, liveNode);
Check(RunningConfigChangeDetector.IsModeOnlyChange(liveSettings, globalBaseline, globalJson,
    liveNode.Id, false, "Global", globalBaseline.PacPort),
    "Global -> PAC can auto-restart when routing selections are unchanged");
liveSettings.PacDirectWebsites = "example.net";
Check(!RunningConfigChangeDetector.IsModeOnlyChange(liveSettings, globalBaseline, globalJson,
    liveNode.Id, false, "Global", globalBaseline.PacPort),
    "Global routing must not hide staged PAC domain edits during auto restart");
liveSettings.PacDirectWebsites = "";
Check(!RunningConfigChangeDetector.IsModeOnlyChange(liveSettings, globalBaseline, globalJson,
    liveNode.Id, true, "Global", globalBaseline.PacPort),
    "external JSON can never be auto-restarted as an implicit proxy-only config");

Check(RunningConfigChangeDetector.NeedsReconnect(liveSettings, liveJson, liveNode.Id,
    false, null, "PAC", liveSettings.PacPort+1), "PAC listener port change requires reconnect");
var otherNode = new ServerProfile { Address = "other.example.org", TransportPassword = "password" };
liveSettings.Profiles.Add(otherNode);
liveSettings.SelectedProfileId = otherNode.Id;
Check(!Pending(), "selection of another editor node does not change the active core");

// Connection indicator is UI runtime state, not part of the portable node settings.
var indicatorProfile = new ServerProfile { Name = "Connected fixture" };
var indicatorChanges = 0;
indicatorProfile.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ServerProfile.IsConnected)) indicatorChanges++; };
indicatorProfile.IsConnected = true;
indicatorProfile.IsConnected = true;
Check(indicatorChanges == 1, "green connection indicator raises change notification once");
Check(!System.Text.Json.JsonSerializer.Serialize(indicatorProfile).Contains("IsConnected"),
      "connection indicator is not serialized into settings");
indicatorProfile.IsConnected = false;
Check(indicatorChanges == 2, "disconnect updates connection indicator");
// Explicit connection action: selecting another editor node must not connect automatically.
var activeIds = new HashSet<string>(StringComparer.Ordinal) { "running-node" };
Check(ConnectionActionResolver.Resolve(false, "running-node", activeIds, false, false) == ConnectionAction.Connect,
      "connection button says Connect when stopped");
Check(ConnectionActionResolver.Resolve(true, "running-node", activeIds, false, false) == ConnectionAction.Disconnect,
      "connection button says Disconnect when the selected node is running");
Check(ConnectionActionResolver.Resolve(true, "other-node", activeIds, false, false) == ConnectionAction.Switch,
      "connection button says Switch when another node is selected");
Check(ConnectionActionResolver.Resolve(true, "other-node", activeIds, true, true) == ConnectionAction.Disconnect,
      "an active external configuration can be explicitly disconnected");
Check(ConnectionActionResolver.Resolve(true, "other-node", activeIds, true, false) == ConnectionAction.Switch,
      "changing from external configuration to a selected server is an explicit switch");
Check(ConnectionActionResolver.Resolve(true, "running-node", activeIds, false, true) == ConnectionAction.Switch,
      "changing from server to external configuration is an explicit switch");

// Core updater selects official architecture-specific ZIP, prioritizes stable by
// default, verifies package digest format, and never accepts nonofficial URLs.
var hash = new string('A', 64);
string TestRelease(string version, bool pre, string host = "github.com") =>
    $$"""{"tag_name":"v{{version}}","draft":false,"prerelease":{{pre.ToString().ToLowerInvariant()}},"assets":[{"name":"v2ray-windows-64.zip","browser_download_url":"https://{{host}}/v2fly/v2ray-core/releases/download/v{{version}}/v2ray-windows-64.zip","digest":"sha256:{{hash}}"}]}""";
var mockReleases = "[" + TestRelease("5.56.0", true) + "," + TestRelease("5.53.0", false) + "]";
Check(CoreReleaseUpdater.SelectRelease(mockReleases, false, ["v2ray-windows-64.zip"])?.Version == new Version(5,53,0),
      "stable channel does not auto-select prerelease");
Check(CoreReleaseUpdater.SelectRelease(mockReleases, true, ["v2ray-windows-64.zip"])?.Version == new Version(5,56,0),
      "opt-in prerelease channel picks the latest version");
Check(CoreReleaseUpdater.SelectRelease("[" + TestRelease("5.56.0", false, "evil.example") + "]", true,
      ["v2ray-windows-64.zip"]) is null, "nonofficial core asset URL rejected");
Check(CoreReleaseUpdater.ParseOfficialChecksum("SHA2-256= " + hash) == hash,
      "V2Fly OpenSSL SHA2-256 digest parsed");
Check(CoreReleaseUpdater.ParseOfficialChecksum("MD5= 123456") is null,
      "SHA256 verification cannot fall back to weak hashes");
Check(CoreReleaseUpdater.ParseVersion("V2Ray 5.53.0 (V2Fly)") == new Version(5,53,0),
      "core CLI version parsed");
Check(CoreReleaseUpdater.UpdateAvailable(new Version(5,53,0),
      new CoreRelease("v5.56.0", new Version(5,56,0), true, "", hash, null)),
      "core update recognized only when newer");
Check(!CoreReleaseUpdater.UpdateAvailable(new Version(5,56,0),
      new CoreRelease("v5.53.0", new Version(5,53,0), false, "", hash, null)),
      "updater does not suggest a downgrade");
// An invalid checksum must not create, overwrite or partially extract the core.
var failedUpdateRoot = Path.Combine(Path.GetTempPath(), "VelaRayCoreUpdate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(failedUpdateRoot);
try
{
    var coreExecutable = Path.Combine(failedUpdateRoot, OperatingSystem.IsWindows() ? "v2ray.exe" : "v2ray");
    using var fakeHttp = new HttpClient(new TestCoreArchiveHandler([1, 2, 3, 4]));
    var installer = new CoreReleaseUpdater(fakeHttp);
    bool rejected = false;
    try
    {
        await installer.InstallAsync(coreExecutable, new CoreRelease("v5.56.0", new Version(5,56,0), true,
            "https://github.com/v2fly/v2ray-core/releases/download/v5.56.0/v2ray-windows-64.zip",
            new string('0', 64), null));
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && !File.Exists(coreExecutable), "invalid SHA256 cannot install a core executable");
    Check(!Directory.EnumerateDirectories(failedUpdateRoot).Any(), "failed download staging is cleaned up");
}
finally { Directory.Delete(failedUpdateRoot, recursive: true); }
// The active badge reflects profiles actually emitted as outbounds by the builders.
var listA = new ServerProfile { Name = "A", Protocol = "hysteria2" };
var listB = new ServerProfile { Name = "B", Protocol = "vmess", Transport = "tcp" };
var badgeSettings = new ClientSettings { Profiles = [listA, listB] };
Check(ActiveProfileResolver.Resolve(badgeSettings, listA).Select(p => p.Id).SequenceEqual([listA.Id]),
      "normal connection marks only the launched server, not the UI selection later");
badgeSettings.BalanceAllNodes = true;
Check(ActiveProfileResolver.Resolve(badgeSettings, listA).Select(p => p.Id).ToHashSet().SetEquals([listA.Id, listB.Id]),
      "balancer marks every server included in the generated core configuration");
badgeSettings.UseCustomConfig = true;
Check(ActiveProfileResolver.Resolve(badgeSettings, listA).Count == 0,
      "external configuration does not misidentify the selected editor server as active");

// Persist the current VelaRay settings schema with independent V4/V5 format per server.
// There is deliberately no data-migration path for earlier settings schemas.
var settingsDir = Path.Combine(Path.GetTempPath(), "VelaRayCurrentSettings-" + Guid.NewGuid().ToString("N"));
var settingsStore = new ClientSettingsStore(settingsDir);
try
{
    settingsStore.EnsureDirectories();
    var savedSettings = new ClientSettings();
    savedSettings.IncludePrereleaseCoreUpdates = true;
    savedSettings.Profiles.Add(new ServerProfile { Name = "V4 node", ConfigVersion = ConfigFlavor.V4 });
    savedSettings.Profiles.Add(new ServerProfile { Name = "V5 node" });
    await settingsStore.SaveAsync(savedSettings);
    var saved = JsonNode.Parse(await File.ReadAllTextAsync(settingsStore.SettingsPath))!;
    Check(saved["ConfigVersion"] is null, "no global config version persisted");
    Check((string?)saved["Profiles"]![0]!["ConfigVersion"] == "V4" &&
          (string?)saved["Profiles"]![1]!["ConfigVersion"] == "V5", "per-node format persisted");
    var reloaded = await settingsStore.LoadAsync();
    Check(reloaded.Profiles.Select(x => x.ConfigVersion).SequenceEqual(["V4", "V5"]),
          "per-node formats round-trip without migration");
    Check(reloaded.IncludePrereleaseCoreUpdates, "core prerelease preference persists across launches");
}
finally { if (Directory.Exists(settingsDir)) Directory.Delete(settingsDir, true); }

// Round-trip V4/V5 detection of externally imported configurations.
Check(ConfigFlavor.DetectFromJson("{\"routing\":{}}") == "V4", "recognize V4 custom config");
Check(ConfigFlavor.DetectFromJson("{\"router\":{}}") == "V5", "recognize V5 custom config");
try { ConfigFlavor.DetectFromJson("{\"router\":{},\"routing\":{}}"); throw new Exception("mixed format was accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: mixed format rejected"); }
try { V5ConfigBuilder.Build(new ClientSettings(), new ServerProfile { Address = "hy2.example.org", TransportPassword = "" });
      throw new Exception("HY2 without password was accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: missing HY2 transport password rejected"); }
// Custom geo references must be preflight checked even outside BypassCN presets.
var missingGeoRoot = Path.Combine(Path.GetTempPath(), "VelaRay-GeoPreflight-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(missingGeoRoot);
try
{
    var corePath = Path.Combine(missingGeoRoot, "v2ray.exe");
    var geos = GeoDataAssets.MissingForConfig("{\"router\":{\"rule\":[{\"geoDomain\":[{\"filePath\":\"geosite.dat\",\"code\":\"cn\"}]}]}}", corePath);
    Check(geos.Count == 1 && geos[0] == "geosite.dat", "missing referenced V5 GeoSite asset detected");
    await File.WriteAllTextAsync(Path.Combine(missingGeoRoot, "geosite.dat"), "fake test fixture");
    Check(GeoDataAssets.MissingForConfig("{\"routing\":{\"rules\":[{\"domain\":[\"geosite:cn\"]}]}}", corePath).Count == 0,
          "found V4 geosite asset");
}
finally { Directory.Delete(missingGeoRoot, recursive: true); }

// V2Fly V5 must be selected explicitly for BOTH test and run, independent of
// .v5.json suffix and of the current working directory.
var spacedConfigPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "VelaRay Test", "config.v5.json"));
foreach (var command in new[] { "test", "run" })
{
    var args = V2RayCoreRunner.BuildConfigArguments(command, spacedConfigPath);
    Check(args.SequenceEqual(new[] { command, "-format", "jsonv5", "-c", spacedConfigPath }),
        $"{command} forces V2Fly jsonv5 with a single -c path argument");
}
foreach (var command in new[] { "test", "run" })
    Check(V2RayCoreRunner.BuildConfigArguments(command, spacedConfigPath, "V4")[2] == "json", $"{command} V4 config loader");
try
{
    V2RayCoreRunner.BuildConfigArguments("convert", spacedConfigPath);
    throw new Exception("Unsupported command accepted");
}
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: rejects non run/test commands"); }

var options = new ClientSettings { RoutingMode = "ProxyAll" };
var hy2 = new ServerProfile { Address = "hy2.example.org", Port = 443, TransportPassword = "secret", ServerName = "hy2.example.org" };
options.Profiles.Add(hy2);
options.SelectedProfileId = hy2.Id;
var config = Config(options, hy2);
var outbound = config["outbounds"]![0]!;
Check((string?)outbound["protocol"] == "hysteria2", "native HY2 outbound");
Check((string?)outbound["streamSettings"]!["transport"] == "hysteria2", "native HY2 transport");
Check((string?)outbound["streamSettings"]!["transportSettings"]!["password"] == "secret", "HY2 auth");
// Native HY2 must not require a UUID, even if a stale VMess field was imported.
Check(string.IsNullOrEmpty(hy2.Uuid), "native Hysteria2 UUID remains empty");
Check(outbound["settings"]!["uuid"] is null, "native Hysteria2 does not emit UUID");
Check((string?)outbound["streamSettings"]!["securitySettings"]!["server_name"] == "hy2.example.org", "HY2 V5 uses official server_name field");
Check(outbound["streamSettings"]!["securitySettings"]!["nextProtocol"] is null, "HY2 V5 does not force HTTP/1.1 ALPN");
hy2.Alpn = "http/1.1";
var regeneratedHy2 = Config(options, hy2)["outbounds"]![0]!;
Check(regeneratedHy2["streamSettings"]!["securitySettings"]!["nextProtocol"] is null, "legacy HTTP/1.1 ALPN cannot break HY2 QUIC");
var unrelatedInvalidVmess = new ServerProfile { Name = "Unselected VMess", Protocol = "vmess",
    Address = "unselected.example.org", Uuid = "", Transport = "tcp" };
options.Profiles.Add(unrelatedInvalidVmess);
Check(Config(options, hy2)["outbounds"]!.AsArray().Count == 3,
    "unused VMess node does not require UUID when validating native Hysteria2");
Check(JsonNode.Parse(V4ConfigBuilder.Build(new ClientSettings { RoutingMode = "ProxyAll", Profiles = [hy2, unrelatedInvalidVmess] }, hy2))!["outbounds"]![0]!["protocol"]!.ToString() == "hysteria2",
    "native Hysteria2 needs no UUID in V4 either");
options.BalanceAllNodes = true;
try { Config(options, hy2); throw new Exception("Invalid balanced VMess node was accepted"); }
catch (ArgumentException ex)
{
    Check(ex.Message.Contains("Unselected VMess") && ex.Message.Contains("UUID"),
        "invalid referenced VMess error identifies the correct node");
}
options.BalanceAllNodes = false;
options.Profiles.Remove(unrelatedInvalidVmess);

Check((bool?)config["inbounds"]![0]!["settings"]!["udpEnabled"] == true, "V5 socks UDP");
Check(config["log"]!["error"]!["level"]?.ToString() == "Warning", "V5 log level");

// Regression: changing a new native Hysteria2 profile to plain VMess must not
// retain the mandatory HY2 transport or TLS, which breaks non-TLS VMess/TCP.
var (fromHy2Transport, fromHy2Tls) = ProfileCapabilities.ProtocolSwitchDefaults(
    "hysteria2", "vmess", "hysteria2", true);
Check(fromHy2Transport == "tcp" && !fromHy2Tls,
    "switching native Hysteria2 to VMess defaults to plain TCP without TLS");
var (existingTransport, existingTls) = ProfileCapabilities.ProtocolSwitchDefaults(
    "vmess", "trojan", "tcp", true);
Check(existingTransport == "tcp" && existingTls,
    "existing explicit TCP/TLS settings are preserved when changing non-HY2 protocol");
var (hy2Transport, hy2Tls) = ProfileCapabilities.ProtocolSwitchDefaults(
    "vmess", "hysteria2", "tcp", false);
Check(hy2Transport == "hysteria2" && hy2Tls,
    "switching back to native Hysteria2 forces HY2 transport and TLS");

var pureTcpNode = new ServerProfile
{
    Protocol = "vmess", Address = "tcp.example.org", Port = 12345,
    Uuid = "934d4533-a99b-4d9e-981f-2d7b9497f088", Transport = "tcp",
    TlsEnabled = false, TcpHeaderJson = "{\"type\":\"none\"}"
};
var pureTcpSettings = new ClientSettings { RoutingMode = "ProxyAll" };
var v4PlainTcp = JsonNode.Parse(V4ConfigBuilder.Build(pureTcpSettings, new ServerProfile
{
    ConfigVersion = "V4", Protocol = pureTcpNode.Protocol, Address = pureTcpNode.Address,
    Port = pureTcpNode.Port, Uuid = pureTcpNode.Uuid, Transport = pureTcpNode.Transport,
    TlsEnabled = pureTcpNode.TlsEnabled, TcpHeaderJson = pureTcpNode.TcpHeaderJson
}))!["outbounds"]![0]!;
Check((string?)v4PlainTcp["streamSettings"]!["network"] == "tcp"
    && (string?)v4PlainTcp["streamSettings"]!["security"] == "none"
    && (string?)v4PlainTcp["settings"]!["vnext"]![0]!["users"]![0]!["id"] == pureTcpNode.Uuid,
    "V4 VMess/TCP emits raw TCP, no TLS and the correct UUID");
// Regression for V2Ray V4: header:{} is invalid because the TCP disguise
// loader requires header.type. Empty/plain headers must generate tcpSettings:{}.
foreach (var rawHeader in new[] { "", "  ", "{}", "{\"type\":\"none\"}" })
{
    pureTcpNode.TcpHeaderJson = rawHeader;
    var v4Tcp = V4ConfigBuilder.BuildOutbound(pureTcpNode)["streamSettings"]!["tcpSettings"]!;
    Check(v4Tcp is JsonObject v4TcpObject && v4TcpObject.Count == 0,
        "V4 plain TCP does not emit an empty header object: " + rawHeader);
    var v5Tcp = V5ConfigBuilder.BuildOutbound(pureTcpNode)["streamSettings"]!["transportSettings"]!;
    Check(v5Tcp is JsonObject v5TcpObject && v5TcpObject.Count == 0,
        "V5 plain TCP does not emit an empty header object: " + rawHeader);
}
pureTcpNode.TcpHeaderJson = "{\"type\":\"http\",\"request\":{\"path\":[\"/\"]}}";
Check((string?)V4ConfigBuilder.BuildOutbound(pureTcpNode)["streamSettings"]!["tcpSettings"]!["header"]!["type"] == "http",
    "V4 preserves explicit HTTP TCP disguise header");
Check((string?)V5ConfigBuilder.BuildOutbound(pureTcpNode)["streamSettings"]!["transportSettings"]!["header"]!["type"] == "http",
    "V5 preserves explicit non-default TCP disguise header");
foreach (var invalidHeader in new[] { "{\"request\":{}}", "{\"type\":\"\"}" })
{
    pureTcpNode.TcpHeaderJson = invalidHeader;
    try
    {
        V4ConfigBuilder.Build(new ClientSettings { RoutingMode = "ProxyAll" }, pureTcpNode);
        throw new Exception("V4 accepted a non-empty TCP header without a valid type");
    }
    catch (ArgumentException ex)
    {
        Check(ex.Message.Contains("'type'"), "Invalid V4 TCP header produces an actionable error");
    }
}
pureTcpNode.TcpHeaderJson = "{\"type\":\"none\"}";

var v5PlainTcp = JsonNode.Parse(V5ConfigBuilder.Build(pureTcpSettings, pureTcpNode))!["outbounds"]![0]!;
Check((string?)v5PlainTcp["streamSettings"]!["transport"] == "tcp"
    && (string?)v5PlainTcp["streamSettings"]!["security"] == "none"
    && ((JsonObject)v5PlainTcp["streamSettings"]!["transportSettings"]!).Count == 0
    && (string?)v5PlainTcp["settings"]!["uuid"] == pureTcpNode.Uuid,
    "V5 VMess/TCP emits raw TCP transport, no TLS and the correct UUID");

var vmess = new ServerProfile { Protocol = "vmess", Address = "vmess.example.org", Port = 1443,
    Uuid = "934d4533-a99b-4d9e-981f-2d7b9497f088", Transport = "hysteria2", TransportPassword = "hy2pass" };
options.Profiles.Add(vmess);
outbound = Config(options, vmess)["outbounds"]![0]!;
Check((string?)outbound["settings"]!["uuid"] == vmess.Uuid, "V5 VMess UUID without vnext");
Check((string?)outbound["streamSettings"]!["transport"] == "hysteria2", "VMess over HY2");

var ws = new ServerProfile { Protocol = "vmess", Address = "ws.example.org", Uuid = vmess.Uuid,
    Transport = "ws", TlsEnabled = true, WebSocketPath = "/ws", WebSocketHeadersJson = "{\"Host\":\"example.org\"}",
    PinnedCertificateSha256 = Convert.ToBase64String(new byte[32]) };
outbound = Config(options, ws)["outbounds"]![0]!;
Check((string?)outbound["streamSettings"]!["transportSettings"]!["headers"]!["Host"] == "example.org", "WebSocket headers");
Check((string?)outbound["streamSettings"]!["securitySettings"]!["pinnedPeerCertificateChainSha256"]![0] == ws.PinnedCertificateSha256, "TLS chain pin");

try
{
    Config(options, new ServerProfile { Protocol = "shadowsocks", Address = "ss.example.org", Password = "unused", Transport = "tcp" });
    throw new Exception("Shadowsocks was unexpectedly accepted");
}
catch (ArgumentException) { Console.WriteLine("PASS: Shadowsocks is no longer supported"); }
// Each selected server chooses its own config format without modifying global options.
var sharedModes = new ClientSettings();
var v4Selected = new ServerProfile { Name = "V4 server", ConfigVersion = ConfigFlavor.V4,
    Protocol = "hysteria2", Address = "v4.example.org", TransportPassword = "pass" };
var v5Selected = new ServerProfile { Name = "V5 server", ConfigVersion = ConfigFlavor.V5,
    Protocol = "hysteria2", Address = "v5.example.org", TransportPassword = "pass" };
sharedModes.Profiles.AddRange([v4Selected, v5Selected]);
Check(ConfigFlavor.DetectFromJson(ConfigFlavor.Build(sharedModes, v4Selected)) == ConfigFlavor.V4,
    "V4 node generates V4 configuration");
Check(ConfigFlavor.DetectFromJson(ConfigFlavor.Build(sharedModes, v5Selected)) == ConfigFlavor.V5,
    "V5 node generates V5 configuration");
Check(v4Selected.ConfigVersion == "V4" && v5Selected.ConfigVersion == "V5",
    "format change for selected node never affects other nodes");

Check(Config(options, vmess)["outbounds"]![0]!["settings"]!["security"] is null, "V5 VMess no legacy security field");

// Regression: V4 TLS and legacy VMess metadata must survive UI -> storage -> JSON -> share/import.
var v4Settings = new ClientSettings { RoutingMode = "ProxyAll" };
var oldVmess = new ServerProfile
{
    Protocol = "vmess", Address = "legacy.example.org", Port = 443,
    Uuid = vmess.Uuid, Transport = "ws", TlsEnabled = true,
    ConfigVersion = ConfigFlavor.V4, VmessAlterId = 16, VmessSecurity = "auto", AllowInsecureTls = true,
    ServerName = "legacy.example.org"
};
v4Settings.Profiles.Add(oldVmess);
var v4Outbound = JsonNode.Parse(V4ConfigBuilder.Build(v4Settings, oldVmess))!["outbounds"]![0]!;
Check((int?)v4Outbound["settings"]!["vnext"]![0]!["users"]![0]!["alterId"] == 16, "V4 VMess alterId exported");
Check((string?)v4Outbound["settings"]!["vnext"]![0]!["users"]![0]!["security"] == "auto", "V4 VMess auto encryption default");
Check((bool?)v4Outbound["streamSettings"]!["tlsSettings"]!["allowInsecure"] == true, "V4 opted-in allowInsecure is emitted");
oldVmess.AllowInsecureTls = false;
var strictTls = JsonNode.Parse(V4ConfigBuilder.Build(v4Settings, oldVmess))!["outbounds"]![0]!;
Check(strictTls["streamSettings"]!["tlsSettings"]!["allowInsecure"] is null, "V4 TLS verification stays on by default");
oldVmess.AllowInsecureTls = true;
try { V5ConfigBuilder.Build(new ClientSettings { RoutingMode = "ProxyAll" }, oldVmess);
      throw new Exception("V5 accepted VMess legacy nonzero alterId"); }
catch (NotSupportedException) { Console.WriteLine("PASS: V5 refuses incompatible legacy alterId"); }
oldVmess.VmessAlterId = 0;
var v5Secure = JsonNode.Parse(V5ConfigBuilder.Build(new ClientSettings { RoutingMode = "ProxyAll" }, oldVmess))!["outbounds"]![0]!;
Check(v5Secure["streamSettings"]!["securitySettings"]!["allowInsecure"] is null, "V5 never outputs forbidden allowInsecure");
Check(v5Secure["settings"]!["alterId"] is null, "V5 never outputs V4 alterId");
try { V4ConfigBuilder.Build(v4Settings, new ServerProfile { Protocol="vmess", Address="example.org", Uuid=vmess.Uuid, Transport="tcp", VmessAlterId=65536 });
      throw new Exception("Out-of-range alterId was accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: VMess alterId range is validated"); }

var upgrade = new ServerProfile { Protocol = "vmess", Address = "web.example.org", Uuid = vmess.Uuid,
    Transport = "httpupgrade", TlsEnabled = true, Http2Path = "/upgrade", Http2Hosts = "example.org" };
Check((string?)Config(options, upgrade)["outbounds"]![0]!["streamSettings"]!["transportSettings"]!["host"] == "example.org", "HTTPUpgrade transport host");
try { Config(options, new ServerProfile { Protocol = "vmess", Address = "old.example.org", Uuid = vmess.Uuid, Transport = "http" });
    throw new Exception("Legacy HTTP2 was silently accepted"); }
catch (NotSupportedException) { Console.WriteLine("PASS: deprecated V4 HTTP2 transport fails with explicit explanation"); }

var kcp = new ServerProfile { Protocol = "vmess", Address = "kcp.example.org", Uuid = vmess.Uuid,
    Transport = "mkcp", TlsEnabled = false, KcpMtu = 1280, KcpHeader = "dtls", KcpCongestion = true };
outbound = Config(options, kcp)["outbounds"]![0]!;
Check((int?)outbound["streamSettings"]!["transportSettings"]!["mtu"] == 1280, "mKCP MTU");
Check((string?)outbound["streamSettings"]!["transportSettings"]!["header"]!["type"] == "dtls", "mKCP header");

// System proxy must publish both loopback endpoints, not only HTTP.
Check(VelaRay.Core.Platform.WindowsProxyEndpoints.GlobalProxyServer(1088, 1080) ==
    "http=127.0.0.1:1088;https=127.0.0.1:1088;socks=127.0.0.1:1080",
    "Global WinINET map includes HTTP/HTTPS and SOCKS");
// Geo data selection is shared by V4 and V5, while PAC rules remain separate.
options.RoutingMode = "BypassCN";
options.GeoIpFile = GeoDataAssets.ChinaPrivateGeoIp;
var compactV4 = JsonNode.Parse(V4ConfigBuilder.Build(options, hy2))!;
Check((string?)compactV4["routing"]!["rules"]![1]!["ip"]![0] == "ext:geoip-only-cn-private.dat:private", "V4 compact geoip asset");
var compactV5 = Config(options, hy2);
Check((string?)compactV5["router"]!["rule"]![1]!["geoip"]![0]!["filePath"] == GeoDataAssets.ChinaPrivateGeoIp, "V5 compact geoip asset");
options.GeoIpFile = GeoDataAssets.FullGeoIp;
Check((string?)Config(options, hy2)["router"]!["rule"]![1]!["geoip"]![1]!["filePath"] == "geoip.dat", "V5 full geoip asset");
Check(GeoDataAssets.RequiredForDefaultRouting(options).SequenceEqual(new[] { "geosite.dat", "geoip.dat" }), "Geo asset requirements");
Check(GeoDataAssets.RequiredForDefaultRouting(new ClientSettings()).SequenceEqual(["geosite.dat", "geoip-only-cn-private.dat"]),
    "Smart default routing needs GeoSite and GeoIP assets");
Check(GeoDataAssets.RequiredForDefaultRouting(new ClientSettings { RoutingMode = "ProxyAll" }).Count == 0,
    "ProxyAll mode needs no Geo assets");
try { GeoDataAssets.NormalizeGeoIpFile("../../untrusted.dat"); throw new Exception("Unexpected custom Geo path accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: Geo data selection rejects path traversal"); }
options.GeoIpFile = GeoDataAssets.ChinaPrivateGeoIp;
options.BalanceAllNodes = true;
options.DnsServers = "1.1.1.1,8.8.8.8";
config = Config(options, hy2);
Check(config["router"]!["balancingRule"]!.AsArray().Count == 1, "multi-server balancer");
Check((string?)config["router"]!["rule"]!.AsArray().Last()!["balancingTag"] == "balance", "balancer catch-all");
Check(config["dns"]!["nameServer"]!.AsArray().Count == 2, "DNS V5 entries");
Check((string?)config["dns"]!["nameServer"]![0]!["address"]!["address"] == "1.1.1.1", "DNS nested endpoint");
Check((string?)config["router"]!["rule"]![0]!["geoDomain"]![0]!["code"] == "cn", "V5 geosite routing");
Check((string?)config["router"]!["rule"]![1]!["geoip"]![0]!["code"] == "private", "V5 private IP routing");
options.RoutingMode = "Custom";
var rule = new RoutingRuleSet { Name = "Legacy policy", Json = "{\"rules\":[{\"type\":\"field\",\"outboundTag\":\"main\",\"domain\":[\"domain:example.org\"]}]}" };
options.RoutingRuleSets.Add(rule);
options.SelectedRoutingRuleId = rule.Id;
config = Config(options, hy2);
Check((string?)config["router"]!["rule"]![0]!["balancingTag"] == "balance", "legacy main => balancer");
Check((string?)config["router"]!["rule"]![0]!["domain"]![0]!["type"] == "RootDomain", "V4 domain: prefix mapped to RootDomain");
Check((string?)config["router"]!["rule"]![0]!["domain"]![0]!["value"] == "example.org", "V4 domain match value");
var direct = new ClientSettings { RoutingMode = "Direct" };
var directJson = Config(direct, hy2);
Check((string?)directJson["router"]!["rule"]![0]!["tag"] == "direct", "V5 direct-only route");
try { Config(new ClientSettings(), new ServerProfile { Address = "hy2.example.org", PinnedCertificateSha256 = Convert.ToBase64String(new byte[32]) });
    throw new Exception("Incompatible HY2 pin accepted"); }
catch (NotSupportedException) { Console.WriteLine("PASS: rejects unsupported HY2 certificate pinning"); }

try { Config(new ClientSettings(), new ServerProfile { Address = "hy2.example.org", TlsEnabled = false });
    throw new Exception("Invalid HY2 accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: rejects HY2 without TLS"); }

var snapshotDirectory = Path.Combine(Path.GetTempPath(), "VelaRay-Snapshot-" + Guid.NewGuid().ToString("N"));
try
{
    var snapshotStore = new ClientSettingsStore(snapshotDirectory);
    snapshotStore.EnsureDirectories();
    Check(snapshotStore.SettingsPath == Path.Combine(snapshotDirectory, "settings", "settings.v5.json"), "settings stored beside executable");
    Check(snapshotStore.GetCoreConfigPath(ConfigFlavor.V5) == Path.Combine(snapshotDirectory, "configs", "config.v5.json"), "generated config lives under configs");
    Check(snapshotStore.GetActiveConfigPath(ConfigFlavor.V5) == Path.Combine(snapshotDirectory, "configs", "config.active.v5.json"), "running snapshot lives under configs");
    Check(snapshotStore.PacDirectory == Path.Combine(snapshotDirectory, "pacs"), "PAC files live under pacs");
    Check(snapshotStore.ProxyBackupPath == Path.Combine(snapshotDirectory, "settings", "wininet-proxy-backup.json"), "Windows proxy backup lives under settings");
    await snapshotStore.SaveAsync(new ClientSettings { HttpPort = 18088, Language = "en-US" });
    var persistedSettings = await snapshotStore.LoadAsync();
    Check(persistedSettings.HttpPort == 18088, "portable client settings roundtrip");
    Check(persistedSettings.Language == "en-US", "language preference persists without affecting core JSON");
    var original = V5ConfigBuilder.Build(new ClientSettings(), hy2);
    await File.WriteAllTextAsync(snapshotStore.GetCoreConfigPath(ConfigFlavor.V5), original);
    var runningPath = await snapshotStore.CreateActiveConfigSnapshotAsync(snapshotStore.GetCoreConfigPath(ConfigFlavor.V5), ConfigFlavor.V5);
    Check(runningPath == snapshotStore.GetActiveConfigPath(ConfigFlavor.V5), "running snapshot has a distinct file path");
    Check(await File.ReadAllTextAsync(runningPath) == original, "running snapshot preserves generated JSON bytes");
    await File.WriteAllTextAsync(snapshotStore.GetCoreConfigPath(ConfigFlavor.V5), "{\"updated\":true}");
    Check(await File.ReadAllTextAsync(runningPath) == original, "preview regeneration cannot mutate running snapshot");
    var externalPath = Path.Combine(snapshotDirectory, "external.v5.json");
    await File.WriteAllTextAsync(externalPath, "{\"log\":{}}");
    await snapshotStore.CreateActiveConfigSnapshotAsync(externalPath, ConfigFlavor.V5);
    await File.WriteAllTextAsync(externalPath, "{\"log\":{\"level\":\"Info\"}}");
    Check(await File.ReadAllTextAsync(runningPath) == "{\"log\":{}}", "external config edits cannot mutate running snapshot");
    var importedRelative = await snapshotStore.ImportCustomConfigAsync(externalPath);
    Check(!Path.IsPathRooted(importedRelative) && importedRelative.StartsWith(Path.Combine("configs", "custom")), "custom V5 config copied to portable directory");
    Check(await File.ReadAllTextAsync(snapshotStore.ResolveFilePath(importedRelative)) == "{\"log\":{\"level\":\"Info\"}}", "imported custom config bytes preserved");
    try { snapshotStore.ResolveFilePath(Path.Combine("..", "secrets.json")); throw new Exception("Relative path traversal accepted"); }
    catch (ArgumentException) { Console.WriteLine("PASS: reject portable path traversal"); }
}
finally
{
    if (Directory.Exists(snapshotDirectory)) Directory.Delete(snapshotDirectory, recursive: true);
}
Check(!ProfileCapabilities.Transports(ConfigFlavor.V4, "vmess").Contains("http"),
    "V4 HTTP/2 is not a new profile transport choice");
Check(new ClientSettings().Language == "zh-CN", "Chinese is the default language");
Console.WriteLine("All model/configuration/snapshot smoke tests passed.");

// PAC template generation is pure and reusable by a future Mac Catalyst front-end.
var generatedPac = PacTemplateGenerator.Generate(PacTemplateGenerator.DirectByDefault,
    "example.cn\n*.intranet.example", "example.com\nhttps://github.com/project", 1088);
Check(generatedPac.Contains("function FindProxyForURL"), "PAC JavaScript entry point");
Check(generatedPac.Contains("example.cn") && generatedPac.Contains("github.com"), "custom direct/proxy websites");
Check(generatedPac.Contains("return 'DIRECT'"), "PAC default direct template");
Check(generatedPac.IndexOf("velaRayDirectSites)", StringComparison.Ordinal) <
      generatedPac.IndexOf("velaRayProxySites)", StringComparison.Ordinal), "custom direct has priority");
Check(PacTemplateGenerator.Generate(PacTemplateGenerator.ChinaSitesDirect, "", "", 1088).Contains("baidu.com"),
    "China-site direct baseline template");
try
{
    PacTemplateGenerator.Generate(PacTemplateGenerator.ProxyByDefault, "host/path", "", 1088);
    throw new Exception("Invalid PAC domain should fail");
}
catch (ArgumentException) { Console.WriteLine("PASS: invalid PAC domain rejected"); }
// Comment lines are preserved in user settings but ignored consistently by both
// managed PAC and V4/V5 Smart routing. Do not split HTTPS URLs at the // token.
var annotatedDirectSites = """
    // Local network, do not proxy; extra commas are part of this comment
    example.cn // Team-owned site
    https://internal.example.com/docs
    """;
var annotatedProxySites = """
    // External services; https:// URLs are permitted here
    google.com // Search site
    https://github.com/project
    // This comment has comma, semicolon; and invalid / paths
    """;
Check(PacTemplateGenerator.ParseWebsites(annotatedDirectSites)
      .SequenceEqual(["example.cn", "internal.example.com"]),
      "PAC DIRECT website comments ignored, URL hosts retained");
Check(PacTemplateGenerator.ParseWebsites(annotatedProxySites)
      .SequenceEqual(["google.com", "github.com"]),
      "PAC PROXY website comments ignored, URL hosts retained");
Check(PacTemplateGenerator.ParseWebsites("example.com, second.example.com; third.example.com")
      .SequenceEqual(["example.com", "second.example.com", "third.example.com"]),
      "Existing comma and semicolon PAC website separators still work");
var commentedPac = PacTemplateGenerator.Generate(PacTemplateGenerator.DirectByDefault,
    annotatedDirectSites, annotatedProxySites, 1088);
Check(commentedPac.Contains("google.com") && commentedPac.Contains("internal.example.com") &&
      !commentedPac.Contains("Search site") && !commentedPac.Contains("Team-owned site"),
      "PAC JavaScript contains only website rules, never comment text");
var commentedSettings = new ClientSettings
{
    PacDirectWebsites = annotatedDirectSites,
    PacProxyWebsites = annotatedProxySites,
    RoutingMode = "Smart"
};
Check(SmartRoutingPolicy.BuildV4(commentedSettings).ToJsonString().Contains("google.com") &&
      !SmartRoutingPolicy.BuildV4(commentedSettings).ToJsonString().Contains("Search site"),
      "V4 Smart routing excludes PAC comments");
Check(SmartRoutingPolicy.BuildV5(commentedSettings).ToJsonString().Contains("google.com") &&
      !SmartRoutingPolicy.BuildV5(commentedSettings).ToJsonString().Contains("Search site"),
      "V5 Smart routing excludes PAC comments");
var roundtripComments = System.Text.Json.JsonSerializer.Deserialize<ClientSettings>(
    System.Text.Json.JsonSerializer.Serialize(commentedSettings))!;
Check(roundtripComments.PacDirectWebsites == annotatedDirectSites &&
      roundtripComments.PacProxyWebsites == annotatedProxySites,
      "PAC comments are preserved unchanged in user settings");
try
{
    PacTemplateGenerator.ParseWebsites("// comment\ninvalid/path");
    throw new Exception("Invalid active website silently ignored");
}
catch (ArgumentException) { Console.WriteLine("PASS: active PAC website validation still enforced"); }

var pacSettings = new ClientSettings
{
    PacTemplate = PacTemplateGenerator.DirectByDefault,
    PacDirectWebsites = "example.cn",
    PacProxyWebsites = "example.com",
    SelectedPacFile = "managed.pac"
};
var parsedSettings = System.Text.Json.JsonSerializer.Deserialize<ClientSettings>(
    System.Text.Json.JsonSerializer.Serialize(pacSettings))!;
Check(parsedSettings.PacTemplate == pacSettings.PacTemplate &&
      parsedSettings.PacProxyWebsites == "example.com", "PAC settings roundtrip");

// Visual routing edits preserve order, emit V4/V5 native field names, and
// preserve unrelated advanced raw JSON properties.
var visualRoute = VisualRoutingEditor.DefaultJson;
visualRoute = VisualRoutingEditor.Upsert(visualRoute, 0, new VisualRoutingEditor.Rule
{
    Target = "direct", Domains = "geosite:cn\ndomain:example.cn", Ips = "geoip:private",
    Ports = "80,443,1000-2000", Network = "tcp,udp"
});
visualRoute = VisualRoutingEditor.Upsert(visualRoute, 1, new VisualRoutingEditor.Rule
{
    Target = "proxy", Domains = "full:example.org", Network = "tcp"
});
Check(VisualRoutingEditor.Count(visualRoute) == 2, "visual routing creates ordered rules");
Check(VisualRoutingEditor.Read(visualRoute, 0).Domains.Contains("geosite:cn"), "visual V4 geosite round-trip");
Check(VisualRoutingEditor.Read(visualRoute, 0).Ips.Contains("geoip:private"), "visual V4 geoip round-trip");
Check((string?)JsonNode.Parse(visualRoute)!["rules"]![0]!["outboundTag"] == "direct", "visual routing V4 destination");
visualRoute = VisualRoutingEditor.Move(visualRoute, 1, -1);
Check((string?)JsonNode.Parse(visualRoute)!["rules"]![0]!["outboundTag"] == "proxy", "visual rule move changes priority");
visualRoute = VisualRoutingEditor.Remove(visualRoute, 0);
Check(VisualRoutingEditor.Count(visualRoute) == 1, "visual routing delete rule");
var visualV5 = "{\"domainStrategy\":\"AsIs\",\"rule\":[]}";
visualV5 = VisualRoutingEditor.Upsert(visualV5, 0, new VisualRoutingEditor.Rule
{
    Target = "direct", Domains = "domain:example.com\ngeosite:cn",
    Ips = "geoip:private\n192.168.1.0/24", Ports = "443", Network = "udp"
});
var v5Rule = JsonNode.Parse(visualV5)!["rule"]![0]!;
Check((string?)v5Rule["tag"] == "direct" && (string?)v5Rule["networks"] == "udp", "visual V5 outbound and network fields");
Check((string?)v5Rule["geoDomain"]![0]!["code"] == "cn", "visual V5 geosite object");
Check((string?)v5Rule["geoip"]![1]!["cidr"]![0]!["ipAddr"] == "192.168.1.0", "visual V5 CIDR object");
Check(VisualRoutingEditor.Read(visualV5, 0).Ips.Contains("192.168.1.0/24"), "visual V5 CIDR roundtrip");
visualV5 = VisualRoutingEditor.SetStrategy(visualV5, "IPIfNonMatch");
Check((string?)JsonNode.Parse(visualV5)!["domainStrategy"] == "IpIfNonMatch", "visual strategy uses V5 casing");
var advancedRoute = "{\"rules\":[{\"type\":\"field\",\"outboundTag\":\"direct\",\"inboundTag\":[\"socks-in\"],\"domain\":[\"domain:example.com\"]}]}";
advancedRoute = VisualRoutingEditor.Upsert(advancedRoute, 0, new VisualRoutingEditor.Rule
{
    Target = "proxy", Domains = "full:example.org", Network = "tcp,udp"
});
Check((string?)JsonNode.Parse(advancedRoute)!["rules"]![0]!["inboundTag"]![0] == "socks-in",
    "visual routing preserves raw-only advanced filters");
try
{
    VisualRoutingEditor.Read("{\"rules\":[{\"type\":\"field\",\"balancerTag\":\"balance\"}]}", 0);
    throw new Exception("Balancer rule was silently mapped to a normal outbound");
}
catch (NotSupportedException) { Console.WriteLine("PASS: balancer routes require raw JSON editor"); }
try
{
    VisualRoutingEditor.Upsert(VisualRoutingEditor.DefaultJson, 0, new VisualRoutingEditor.Rule { Ports = "70000" });
    throw new Exception("Invalid ports were accepted");
}
catch (ArgumentException) { Console.WriteLine("PASS: visual routing rejects invalid port ranges"); }

Check(VisualRoutingEditor.Read("{\"rules\":[{\"type\":\"field\",\"outboundTag\":\"direct\",\"port\":443}]}", 0).Ports == "443",
    "visual editor accepts legacy numeric V4 port values");
try
{
    VisualRoutingEditor.Upsert(VisualRoutingEditor.DefaultJson, 0, new VisualRoutingEditor.Rule { Ips = "10.0.0.0/bad" });
    throw new Exception("Malformed CIDR prefix was accepted");
}
catch (ArgumentException) { Console.WriteLine("PASS: visual routing rejects malformed CIDR prefix"); }


// Smart preset: explicit managed-PAC proxy allowlist > private/local > CN domain/IP > direct fallback.
Check(new ClientSettings().RoutingMode == "Smart", "new installations default to Smart routing (without migration)");
var smartSettings = new ClientSettings
{
    RoutingMode = "Smart", GeoIpFile = "geoip-only-cn-private.dat",
    PacDirectWebsites = "direct.example.com\n127.0.0.1",
    PacProxyWebsites = "proxy.example.com\n8.8.8.8\ndirect.example.com"
};
var smartV4 = SmartRoutingPolicy.BuildV4(smartSettings);
var smartV4Rules = smartV4["rules"]!.AsArray();
Check((string?)smartV4["domainStrategy"] == "IPIfNonMatch", "Smart V4 domain strategy");
Check((string?)smartV4Rules[0]!["outboundTag"] == "direct" &&
      smartV4Rules[0]!["domain"]!.ToJsonString().Contains("domain:direct.example.com"),
      "explicit managed PAC direct exception precedes proxy override");
Check(smartV4Rules.Any(r => (string?)r?["outboundTag"] == "proxy" &&
      r?["domain"]?.ToJsonString().Contains("domain:proxy.example.com") == true),
      "explicit managed PAC proxy domain stays ahead of CN geosite");
Check(smartV4Rules.Any(r => r?["ip"]?.ToJsonString().Contains("private") == true),
      "Smart V4 includes private destination IPs");
Check(smartV4Rules.Any(r => r?["domain"]?.ToJsonString().Contains("geosite:cn") == true),
      "Smart V4 includes China domains");
Check(smartV4Rules.Any(r => r?["ip"]?.ToJsonString().Contains(":cn") == true),
      "Smart V4 includes China destination IPs");
Check(!smartV4Rules.Any(r => r?["network"] is not null && r?["domain"] is null && r?["ip"] is null),
      "Smart V4 omits unconditional catchall that would defeat IPIfNonMatch");
Check(new ClientSettings().PacTemplate == PacTemplateGenerator.DirectByDefault,
      "New managed PAC defaults to direct, not proxy by default");
Check(SmartRoutingPolicy.Describe(smartSettings).Last().Contains("其余流量 → 直连"),
      "Built-in routing preview explicitly shows default direct");
Check(SmartRoutingPolicy.Describe(smartSettings, english: true).Last().Contains("Everything else → Direct"),
      "English built-in routing preview explicitly shows default direct");
var smartV5 = SmartRoutingPolicy.BuildV5(smartSettings);
var smartV5Rules = smartV5["rule"]!.AsArray();
Check((string?)smartV5["domainStrategy"] == "IpIfNonMatch", "Smart V5 strategy uses V5 casing");
Check(smartV5Rules.Any(r => (string?)r?["tag"] == "proxy" &&
      r?["domain"]?.ToJsonString().Contains("RootDomain") == true),
      "Smart V5 managed PAC proxy uses RootDomain matcher");
Check(smartV5Rules.Any(r => r?["geoDomain"]?.ToJsonString().Contains("geosite.dat") == true),
      "Smart V5 GeoSite category preserved");
Check(smartV5Rules.Any(r => r?["geoip"]?.ToJsonString().Contains("geoip-only-cn-private.dat") == true),
      "Smart V5 GeoIP references selected compact data file");
Check(PacTemplateGenerator.EffectiveProxyWebsites(smartSettings.PacDirectWebsites,
      smartSettings.PacProxyWebsites).SequenceEqual(["proxy.example.com", "8.8.8.8"]),
      "managed PAC proxy list excludes explicit direct conflicts");
var smartServer = new ServerProfile { Address = "smart.example.org", TransportPassword = "password" };
smartSettings.Profiles.Add(smartServer);
var smartConfigV4 = JsonNode.Parse(V4ConfigBuilder.Build(smartSettings, smartServer))!;
var smartConfigV5 = JsonNode.Parse(V5ConfigBuilder.Build(smartSettings, smartServer))!;
Check((string?)smartConfigV4["routing"]!["domainStrategy"] == "IPIfNonMatch",
      "V4 full config integrates Smart routing");
Check((string?)smartConfigV5["router"]!["domainStrategy"] == "IpIfNonMatch",
      "V5 full config integrates Smart routing");
Check((string?)smartConfigV4["outbounds"]![0]!["tag"] == "direct" &&
      (string?)smartConfigV4["outbounds"]![1]!["tag"] == "proxy",
      "Smart V4 puts direct first for unmatched-traffic fallback");
Check((string?)smartConfigV5["outbounds"]![0]!["tag"] == "direct" &&
      (string?)smartConfigV5["outbounds"]![1]!["tag"] == "proxy",
      "Smart V5 puts direct first for unmatched-traffic fallback");
Check((string?)SmartRoutingPolicy.BuildV4(smartSettings)["rules"]![0]!["outboundTag"] == "direct" &&
      (string?)SmartRoutingPolicy.BuildV4(smartSettings)["rules"]![2]!["outboundTag"] == "proxy",
      "Smart V4 preserves PAC direct exceptions then explicit proxy rules");
Check((string?)SmartRoutingPolicy.BuildV5(smartSettings)["rule"]![0]!["tag"] == "direct" &&
      (string?)SmartRoutingPolicy.BuildV5(smartSettings)["rule"]![2]!["tag"] == "proxy",
      "Smart V5 preserves PAC direct exceptions then explicit proxy rules");
var extraServer = new ServerProfile { Address = "smart2.example.org", TransportPassword = "password" };
smartSettings.Profiles.Add(extraServer);
smartSettings.BalanceAllNodes = true;
var balancedSmartV4 = JsonNode.Parse(V4ConfigBuilder.Build(smartSettings, smartServer))!;
var balancedSmartV5 = JsonNode.Parse(V5ConfigBuilder.Build(smartSettings, smartServer))!;
Check((string?)balancedSmartV4["routing"]!["domainStrategy"] == "IPIfNonMatch",
      "V4 smart balancing keeps IPIfNonMatch for China IP matches");
Check((string?)balancedSmartV5["router"]!["domainStrategy"] == "IpIfNonMatch",
      "V5 smart balancing keeps IpIfNonMatch for China IP matches");
Check((string?)balancedSmartV4["outbounds"]![0]!["tag"] == "direct" &&
      (string?)balancedSmartV5["outbounds"]![0]!["tag"] == "direct",
      "Balancing does not change Smart direct fallback in V4/V5");
Check(balancedSmartV4["routing"]!["rules"]!.AsArray().Count == smartV4Rules.Count &&
      balancedSmartV5["router"]!["rule"]!.AsArray().Count == smartV5Rules.Count,
      "Balancing does not add a catch-all proxy/balancer rule to Smart routing");
Check(balancedSmartV4["routing"]!["rules"]!.AsArray().Any(x => (string?)x?["balancerTag"] == "balance") &&
      balancedSmartV5["router"]!["rule"]!.AsArray().Any(x => (string?)x?["balancingTag"] == "balance"),
      "Only PAC proxy exceptions are assigned to Smart balancing");
Check(!balancedSmartV4["routing"]!["rules"]!.AsArray().Any(x => x?["balancerTag"] is not null && x?["domain"] is null && x?["ip"] is null) &&
      !balancedSmartV5["router"]!["rule"]!.AsArray().Any(x => x?["balancingTag"] is not null && x?["domain"] is null && x?["geoip"] is null),
      "Balanced Smart mode does not proxy unmatched traffic");
// An empty PAC proxy allowlist must not accidentally proxy the whole internet.
var emptySmart = new ClientSettings { RoutingMode = "Smart", PacProxyWebsites = "" };
var emptyV4 = JsonNode.Parse(V4ConfigBuilder.Build(emptySmart, smartServer))!;
var emptyV5 = JsonNode.Parse(V5ConfigBuilder.Build(emptySmart, smartServer))!;
Check((string?)emptyV4["outbounds"]![0]!["tag"] == "direct" &&
      !emptyV4["routing"]!["rules"]!.AsArray().Any(x => (string?)x?["outboundTag"] == "proxy"),
      "Smart V4 without managed PAC proxy entries routes everything directly");
Check((string?)emptyV5["outbounds"]![0]!["tag"] == "direct" &&
      !emptyV5["router"]!["rule"]!.AsArray().Any(x => (string?)x?["tag"] == "proxy"),
      "Smart V5 without managed PAC proxy entries routes everything directly");
Check(new ClientSettings { RoutingMode = "ProxyAll" }.RoutingMode == "ProxyAll" &&
      (string?)JsonNode.Parse(V4ConfigBuilder.Build(new ClientSettings { RoutingMode = "ProxyAll" }, smartServer))!["outbounds"]![0]!["tag"] == "proxy" &&
      (string?)JsonNode.Parse(V5ConfigBuilder.Build(new ClientSettings { RoutingMode = "ProxyAll" }, smartServer))!["outbounds"]![0]!["tag"] == "proxy",
      "ProxyAll retains proxy-first fallback, independent of Smart mode");
// Opt-in Smart unmatched->Proxy: existing profiles must remain Direct by default.
Check(new ClientSettings().SmartUnmatchedRoute == SmartRoutingPolicy.UnmatchedDirect,
      "New and legacy settings retain direct Smart fallback");
var smartProxySettings = new ClientSettings
{
    RoutingMode = "Smart", SmartUnmatchedRoute = SmartRoutingPolicy.UnmatchedProxy,
    PacTemplate = PacTemplateGenerator.DirectByDefault,
    PacDirectWebsites = "internal.google.com\nlocal.example.com",
    PacProxyWebsites = "google.com"
};
smartProxySettings.Profiles.Add(smartServer);
Check(SmartRoutingPolicy.ProxyUnmatched(smartProxySettings) &&
      SmartRoutingPolicy.EffectiveManagedPacTemplate(smartProxySettings) == PacTemplateGenerator.DirectByDefault,
      "Smart Proxy fallback does not override the PAC Direct-by-default selection");
smartProxySettings.PacTemplate = PacTemplateGenerator.ProxyByDefault;
Check(SmartRoutingPolicy.EffectiveManagedPacTemplate(smartProxySettings) == PacTemplateGenerator.ProxyByDefault,
      "PAC Proxy-by-default remains an explicit separate choice");
smartProxySettings.PacTemplate = PacTemplateGenerator.DirectByDefault;
var generatedPac = PacTemplateGenerator.Generate(
    SmartRoutingPolicy.EffectiveManagedPacTemplate(smartProxySettings),
    smartProxySettings.PacDirectWebsites, smartProxySettings.PacProxyWebsites, 10809);
Check(System.Text.RegularExpressions.Regex.IsMatch(generatedPac, @"return 'DIRECT';\s*}\s*$"),
      "Smart Proxy fallback and a DirectByDefault PAC still return DIRECT for unmatched websites");
smartProxySettings.PacTemplate = PacTemplateGenerator.ProxyByDefault;
var proxyDefaultPac = PacTemplateGenerator.Generate(
    SmartRoutingPolicy.EffectiveManagedPacTemplate(smartProxySettings),
    smartProxySettings.PacDirectWebsites, smartProxySettings.PacProxyWebsites, 10809);
Check(System.Text.RegularExpressions.Regex.IsMatch(proxyDefaultPac, @"return 'PROXY 127\.0\.0\.1:10809';\s*}\s*$"),
      "Only explicit ProxyByDefault PAC sends unmatched websites to the local proxy");
smartProxySettings.PacTemplate = PacTemplateGenerator.DirectByDefault;
Check(SmartRoutingPolicy.Describe(smartProxySettings).Last().Contains("其余流量 → 代理") &&
      SmartRoutingPolicy.Describe(smartProxySettings, english: true).Last().Contains("Everything else → Proxy"),
      "Smart preview reports proxy fallback in both languages");
var smartProxyV4 = JsonNode.Parse(V4ConfigBuilder.Build(smartProxySettings, smartServer))!;
var smartProxyV5 = JsonNode.Parse(V5ConfigBuilder.Build(smartProxySettings, smartServer))!;
Check((string?)smartProxyV4["outbounds"]![0]!["tag"] == "proxy" &&
      (string?)smartProxyV4["outbounds"]![1]!["tag"] == "direct" &&
      (string?)smartProxyV5["outbounds"]![0]!["tag"] == "proxy" &&
      (string?)smartProxyV5["outbounds"]![1]!["tag"] == "direct",
      "Smart Proxy fallback uses proxy-first outbounds in V4 and V5");
Check((string?)smartProxyV4["routing"]!["domainStrategy"] == "IPIfNonMatch" &&
      (string?)smartProxyV5["router"]!["domainStrategy"] == "IpIfNonMatch" &&
      smartProxyV4["routing"]!["rules"]!.AsArray().Any(r => r?["domain"]?.ToJsonString().Contains("geosite:cn") == true) &&
      smartProxyV5["router"]!["rule"]!.AsArray().Any(r => r?["geoDomain"]?.ToJsonString().Contains("cn") == true),
      "Smart Proxy fallback keeps V4/V5 China direct exceptions and IPIfNonMatch");
Check(smartProxyV4["routing"]!["rules"]!.AsArray().Any(r => r?["domain"]?.ToJsonString().Contains("domain:internal.google.com") == true) &&
      smartProxyV5["router"]!["rule"]!.AsArray().Any(r => r?["domain"]?.ToJsonString().Contains("RootDomain") == true),
      "PAC direct overrides and domain suffix matching also exist in core routes");
smartProxySettings.PacTemplate = PacTemplateGenerator.ChinaSitesDirect;
Check(SmartRoutingPolicy.EffectiveManagedPacTemplate(smartProxySettings) == PacTemplateGenerator.ChinaSitesDirect,
      "Smart Proxy fallback retains China-sites PAC exclusions");
smartProxySettings.RoutingMode = "Custom";
Check(SmartRoutingPolicy.EffectiveManagedPacTemplate(smartProxySettings) == PacTemplateGenerator.ChinaSitesDirect,
      "Custom mode never overrides managed PAC template");
smartProxySettings.RoutingMode = "Smart";
var serializedSmartProxy = JsonSerializer.Serialize(smartProxySettings);
Check(JsonSerializer.Deserialize<ClientSettings>(serializedSmartProxy)?.SmartUnmatchedRoute ==
      SmartRoutingPolicy.UnmatchedProxy, "Smart fallback choice persists across JSON settings round-trip");
smartProxySettings.Profiles.Add(extraServer);
smartProxySettings.BalanceAllNodes = true;
var balancedProxyV4 = JsonNode.Parse(V4ConfigBuilder.Build(smartProxySettings, smartServer))!;
var balancedProxyV5 = JsonNode.Parse(V5ConfigBuilder.Build(smartProxySettings, smartServer))!;
Check(((string?)balancedProxyV4["outbounds"]![0]!["tag"])?.StartsWith("node-") == true &&
      ((string?)balancedProxyV5["outbounds"]![0]!["tag"])?.StartsWith("node-") == true &&
      (string?)balancedProxyV4["routing"]!["domainStrategy"] == "IPIfNonMatch" &&
      (string?)balancedProxyV5["router"]!["domainStrategy"] == "IpIfNonMatch",
      "Balanced Smart Proxy fallback uses first node without breaking GeoIP evaluation");
Check(!balancedProxyV4["routing"]!["rules"]!.AsArray().Any(x => x?["balancerTag"] is not null && x?["domain"] is null && x?["ip"] is null) &&
      !balancedProxyV5["router"]!["rule"]!.AsArray().Any(x => x?["balancingTag"] is not null && x?["domain"] is null && x?["geoip"] is null),
      "Balanced Smart Proxy mode does not add catch-all before GeoIP rules");
Check(PacTemplateGenerator.ParseWebsites("google.com\n*.google.com\nhttps://www.google.com/test")
      .SequenceEqual(["google.com", "www.google.com"]),
      "PAC accepts a root domain and wildcard entries and deduplicates equivalent entries");
// Global mode means remote proxy for ALL traffic that enters the local V2Ray
// listeners; the saved PAC/Manual routing policy remains unchanged.
foreach (var savedMode in new[] { "Smart", "BypassCN", "Direct", "Custom" })
{
    var globalSettings = new ClientSettings
    {
        ProxyMode = "Global", RoutingMode = savedMode,
        SmartUnmatchedRoute = "Direct", Profiles = [smartServer]
    };
    Check(EffectiveRoutingMode.Resolve(globalSettings) == "ProxyAll",
        $"Global overrides stored {savedMode} routing only when generating core config");
    foreach (var generated in new[] {
        JsonNode.Parse(V4ConfigBuilder.Build(globalSettings, smartServer))!,
        JsonNode.Parse(V5ConfigBuilder.Build(globalSettings, smartServer))!
    })
    {
        var outbounds = generated["outbounds"]!.AsArray();
        Check((string?)outbounds[0]!["tag"] == "proxy" &&
              (string?)outbounds[1]!["tag"] == "direct",
              $"Global with stored {savedMode} uses proxy as default outbound before direct");
        var rules = (generated["routing"]?["rules"] ?? generated["router"]?["rule"])!.AsArray();
        Check(rules.Count == 0, $"Global suppresses {savedMode} direct exceptions in V4/V5 routing");
    }
    Check(globalSettings.RoutingMode == savedMode,
        $"Global generated config never overwrites stored {savedMode} preference");
    Check(GeoDataAssets.RequiredForDefaultRouting(globalSettings).Count == 0,
        "Global-generated ProxyAll does not require unused Smart/BypassCN Geo assets");
    globalSettings.ProxyMode = "PAC";
    Check(EffectiveRoutingMode.Resolve(globalSettings) == savedMode,
        "returning to PAC restores the selected routing policy");
}
{
    // ProxyAll is already in effect: switching the Windows entry mode to Global
    // must not forcibly restart the core if it does not change the JSON.
    var sameSettings = new ClientSettings { ProxyMode = "PAC", RoutingMode = "ProxyAll", Profiles = [smartServer] };
    var existingJson = ConfigFlavor.Build(sameSettings, smartServer);
    sameSettings.ProxyMode = "Global";
    Check(!RunningConfigChangeDetector.NeedsReconnect(sameSettings, existingJson,
        smartServer.Id, false, null, "PAC", sameSettings.PacPort),
        "PAC ProxyAll -> Global ProxyAll can update WinINET without a core restart");
}
{
    var globalBalance = new ClientSettings
    {
        ProxyMode = "Global", RoutingMode = "Smart", BalanceAllNodes = true,
        Profiles = [smartServer, extraServer]
    };
    var builtV4 = JsonNode.Parse(V4ConfigBuilder.Build(globalBalance, smartServer))!;
    var builtV5 = JsonNode.Parse(V5ConfigBuilder.Build(globalBalance, smartServer))!;
    Check(builtV4["routing"]!["rules"]!.AsArray().Any(r => r?["balancerTag"] is not null) &&
          builtV5["router"]!["rule"]!.AsArray().Any(r => r?["balancingTag"] is not null),
          "Global can balance proxy outbounds without Smart China-direct rules");
}
// V2Fly traffic statistics must remain optional, local-only, and routing-safe.
var testTrafficStats = """
{
  "stat": [
    { "name": "outbound>>>proxy>>>traffic>>>uplink", "value": "4096" },
    { "name": "outbound>>>proxy>>>traffic>>>downlink", "value": 8192 },
    { "name": "outbound>>>node-server-1>>>traffic>>>uplink", "value": "1024" },
    { "name": "outbound>>>direct>>>traffic>>>uplink", "value": "999999" },
    { "name": "outbound>>>block>>>traffic>>>downlink", "value": "200" },
    { "name": "inbound>>>socks-in>>>traffic>>>downlink", "value": "123456" }
  ]
}
""";
var measuredTraffic = CoreTrafficStatistics.Parse(testTrafficStats);
Check(measuredTraffic == new TrafficCounters(5_120, 8_192),
    "traffic query counts proxy outbounds only (excludes direct, block and inbound)");
var rates = new TrafficRateCalculator();
var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
var firstReading = rates.Update(new TrafficCounters(1_000, 2_000), t0);
var secondReading = rates.Update(new TrafficCounters(3_000, 6_000),
    t0 + System.Diagnostics.Stopwatch.Frequency);
Check(firstReading.UplinkBytesPerSecond == 0 && firstReading.DownlinkBytesPerSecond == 0 &&
      Math.Abs(secondReading.UplinkBytesPerSecond - 2_000) < 1 &&
      Math.Abs(secondReading.DownlinkBytesPerSecond - 4_000) < 1,
      "traffic rate is bytes divided by elapsed monotonic seconds");
var generatedV4 = V4ConfigBuilder.Build(new ClientSettings { RoutingMode = "ProxyAll" }, smartServer);
var injectedV4 = JsonNode.Parse(CoreTrafficStatistics.Enable(generatedV4, ConfigFlavor.V4, 52984))!;
Check((string?)injectedV4["api"]!["tag"] == CoreTrafficStatistics.ApiTag &&
      injectedV4["stats"] is JsonObject &&
      (bool?)injectedV4["policy"]!["system"]!["statsOutboundUplink"] == true &&
      (string?)injectedV4["inbounds"]![2]!["listen"] == "127.0.0.1" &&
      (string?)injectedV4["routing"]!["rules"]![0]!["outboundTag"] == CoreTrafficStatistics.ApiTag,
      "V4 traffic API stays localhost and routes to StatsService before all proxy rules");
var generatedV5 = V5ConfigBuilder.Build(new ClientSettings { RoutingMode = "Smart" }, smartServer);
var injectedV5 = JsonNode.Parse(CoreTrafficStatistics.Enable(generatedV5, ConfigFlavor.V5, 52985))!;
Check((string?)injectedV5["services"]!["commander"]!["tag"] == CoreTrafficStatistics.ApiTag &&
      injectedV5["services"]!["stats"] is JsonObject &&
      (bool?)injectedV5["services"]!["policy"]!["system"]!["stats"]!["outboundDownlink"] == true &&
      (string?)injectedV5["inbounds"]![2]!["listen"] == "127.0.0.1" &&
      (string?)injectedV5["router"]!["rule"]![0]!["tag"] == CoreTrafficStatistics.ApiTag,
      "V5 traffic API uses jsonv5 services and a top-priority localhost-only route");
Check(JsonNode.Parse(generatedV4)!["api"] is null && JsonNode.Parse(generatedV5)!["services"] is null,
      "stats injection never mutates the originally generated config");
try { CoreTrafficStatistics.Enable(generatedV4, ConfigFlavor.V4, 80); throw new Exception("missed invalid-port test"); }
catch (ArgumentOutOfRangeException) { }
Console.WriteLine("Traffic statistics: optional V4/V5 API and monotonic speed calculation regressions passed.");

// A rule edited in Routing & PAC must not change the next normal core connection.
var savedRule = new RoutingRuleSet
{
    Name = "Selected policy",
    Json = "{\"rules\":[{\"type\":\"field\",\"outboundTag\":\"direct\",\"domain\":[\"domain:old.example\"]}]}"
};
var separateRoutingSettings = new ClientSettings
{
    RoutingMode = "Custom",
    RoutingRuleSets = [savedRule],
    SelectedRoutingRuleId = savedRule.Id
};
CustomRoutingDeployment.MigrateLegacy(separateRoutingSettings);
separateRoutingSettings.Profiles.Add(hy2);
var activeRoutingJson = ConfigFlavor.Build(separateRoutingSettings, hy2);
Check(separateRoutingSettings.AppliedRoutingRuleJson == savedRule.Json &&
      separateRoutingSettings.EditingRoutingRuleId == savedRule.Id,
      "legacy settings migration pins the selected custom rule before editing");
var originalV4Routing = V4ConfigBuilder.BuildRouting(separateRoutingSettings).ToJsonString();
var originalV5Routing = Config(separateRoutingSettings, hy2)["router"]!.ToJsonString();
savedRule.Json = "{\"rules\":[{\"type\":\"field\",\"outboundTag\":\"direct\",\"domain\":[\"domain:new.example\"]}]}";
separateRoutingSettings.EditingRoutingRuleId = savedRule.Id;
Check(V4ConfigBuilder.BuildRouting(separateRoutingSettings).ToJsonString() == originalV4Routing &&
      Config(separateRoutingSettings, hy2)["router"]!.ToJsonString() == originalV5Routing,
      "editing a selected rule does not change V4/V5 core JSON even after ordinary reconnect");
Check(!RunningConfigChangeDetector.NeedsReconnect(separateRoutingSettings, activeRoutingJson, hy2.Id,
          false, null, "PAC", separateRoutingSettings.PacPort),
      "editing a saved rule draft does not set the running-core pending flag");
var persistedRouting = JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(separateRoutingSettings))!;
CustomRoutingDeployment.MigrateLegacy(persistedRouting);
Check(V4ConfigBuilder.BuildRouting(persistedRouting).ToJsonString() == originalV4Routing,
      "applied snapshot persists across settings save/reload");
CustomRoutingDeployment.Apply(separateRoutingSettings, savedRule.Id);
Check(V4ConfigBuilder.BuildRouting(separateRoutingSettings).ToJsonString() != originalV4Routing &&
      Config(separateRoutingSettings, hy2)["router"]!.ToJsonString() != originalV5Routing,
      "explicit Proxy settings application updates the V4/V5 snapshot");
Check(RunningConfigChangeDetector.NeedsReconnect(separateRoutingSettings, activeRoutingJson, hy2.Id,
          false, null, "PAC", separateRoutingSettings.PacPort),
      "explicit promotion flags a running core for manual reconnect");
var activeJsonBeforeDeletion = CustomRoutingDeployment.ResolveJson(separateRoutingSettings);
separateRoutingSettings.RoutingRuleSets.Clear();
Check(CustomRoutingDeployment.ResolveJson(separateRoutingSettings) == activeJsonBeforeDeletion &&
      V4ConfigBuilder.BuildRouting(separateRoutingSettings).ToJsonString().Contains("new.example"),
      "deleting an editor rule does not erase the selected snapshot");
Console.WriteLine("Custom routing editing, snapshot persistence, and explicit application regression tests passed.");

// Explicit application proxies are unconditional remote-only listeners.
// PAC routes through its own inbound and continues to obey Smart or Custom rules.
var explicitSettings = new ClientSettings
{
    RoutingMode = "Smart", SmartUnmatchedRoute = "Direct", ProxyMode = "PAC",
    SocksPort = 1080, HttpPort = 1088, PacProxyPort = 1089,
    PacPort = 18888, PacDirectWebsites = "google.com", Profiles = [smartServer]
};
Check(new ClientSettings().SocksPort == 1080 &&
      new ClientSettings().HttpPort == 1088 &&
      new ClientSettings().PacProxyPort == 1089,
      "three independent proxy listeners use editable default ports");
Check(JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(explicitSettings))?.PacProxyPort == 1089,
      "dedicated PAC proxy port survives settings serialization");
foreach (var v5 in new[] { false, true })
{
    var json = JsonNode.Parse(v5
        ? V5ConfigBuilder.Build(explicitSettings, smartServer)
        : V4ConfigBuilder.Build(explicitSettings, smartServer))!;
    var routes = (v5 ? json["router"]!["rule"] : json["routing"]!["rules"])!.AsArray();
    var forced = routes[0]!;
    Check((string?)forced[v5 ? "tag" : "outboundTag"] == "proxy" &&
          forced["inboundTag"]!.AsArray().Select(x => (string?)x).SequenceEqual(
              new[] { "socks-in", "http-in" }),
          $"{(v5 ? "V5" : "V4")} explicit SOCKS5 and HTTP are remote-only, ahead of Smart routing");
    var pacIngress = json["inbounds"]!.AsArray().SingleOrDefault(x =>
        (string?)x?["tag"] == ExplicitProxyPolicy.PacHttpTag);
    Check(json["inbounds"]!.AsArray().Count == 3 &&
          (string?)pacIngress?["listen"] == "127.0.0.1" &&
          (int?)pacIngress?["port"] == 1089,
          $"{(v5 ? "V5" : "V4")} independent PAC HTTP proxy stays loopback-only");
    Check(routes.Skip(1).Any(x => x?.ToJsonString().Contains("google.com") == true),
          $"{(v5 ? "V5" : "V4")} PAC routing still honors explicit direct domain rules");
}
// Moving between PAC, Manual and Global does not alter the listener layout.
foreach (var mode in new[] { "PAC", "Manual", "Global" })
{
    explicitSettings.ProxyMode = mode;
    foreach (var config in new[] { V4ConfigBuilder.Build(explicitSettings, smartServer),
                                    V5ConfigBuilder.Build(explicitSettings, smartServer) })
    {
        var json = JsonNode.Parse(config)!;
        Check(json["inbounds"]!.AsArray().Count == 3,
              $"{mode} keeps the three proxy inbounds available");
        var rules = (json["routing"]?["rules"] ?? json["router"]?["rule"])!.AsArray();
        if (mode == "Global")
            Check(rules.Count == 0, "Global mode uses ProxyAll with no redundant override");
        else
            Check(rules[0]!["inboundTag"]!.AsArray().Select(x => (string?)x)
                  .SequenceEqual(new[] { "socks-in", "http-in" }),
                  $"{mode} forces only explicit inbound traffic remotely");
    }
}
explicitSettings.ProxyMode = "PAC";
explicitSettings.PacProxyPort = 11999;
foreach (var config in new[] { V4ConfigBuilder.Build(explicitSettings, smartServer),
                                V5ConfigBuilder.Build(explicitSettings, smartServer) })
{
    var json = JsonNode.Parse(config)!;
    Check(json["inbounds"]!.AsArray().Any(x => (int?)x?["port"] == 11999),
          "custom PAC proxy port is written to the core configuration");
}
Check(PacTemplateGenerator.Generate(PacTemplateGenerator.ProxyByDefault, "", "", 11999)
      .Contains("PROXY 127.0.0.1:11999"),
      "PAC script PROXY directive uses only the configurable dedicated PAC proxy port");
explicitSettings.PacProxyPort = explicitSettings.HttpPort;
try
{
    _ = V4ConfigBuilder.Build(explicitSettings, smartServer);
    throw new Exception("Expected duplicate PAC proxy port failure");
}
catch (ArgumentException ex) when (ex.Message.Contains("ports must be distinct"))
{
    Check(true, "PAC proxy port conflict is rejected before core startup");
}
explicitSettings.PacProxyPort = 1089;
explicitSettings.BalanceAllNodes = true;
explicitSettings.Profiles.Add(extraServer);
foreach (var v5 in new[] { false, true })
{
    var json = JsonNode.Parse(v5
        ? V5ConfigBuilder.Build(explicitSettings, smartServer)
        : V4ConfigBuilder.Build(explicitSettings, smartServer))!;
    var rule = (v5 ? json["router"]!["rule"] : json["routing"]!["rules"])![0]!;
    Check((string?)rule[v5 ? "balancingTag" : "balancerTag"] == "balance",
          "explicit proxy routing continues to respect the enabled node balancer");
}
// Legacy settings: the old manual HTTP port is reused as the new PAC proxy
// port only when an explicit new-style port is not already present.
var legacyRoot = Path.Combine(Path.GetTempPath(), "VelaRayLegacyPorts-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new ClientSettingsStore(legacyRoot);
    Directory.CreateDirectory(store.SettingsDirectory);
    await File.WriteAllTextAsync(store.SettingsPath, "{\"ExplicitHttpPort\":12089,\"ForceExplicitProxy\":false}");
    var migrated = await store.LoadAsync();
    Check(migrated.PacProxyPort == 12089, "older dedicated HTTP port is migrated to PAC proxy port");
    await store.SaveAsync(migrated);
    var saved = await File.ReadAllTextAsync(store.SettingsPath);
    Check(saved.Contains("\"PacProxyPort\": 12089") &&
          !saved.Contains("ExplicitHttpPort") && !saved.Contains("ForceExplicitProxy"),
          "removed explicit-proxy switch and legacy property are not persisted again");
}
finally { if (Directory.Exists(legacyRoot)) Directory.Delete(legacyRoot, recursive: true); }
Console.WriteLine("Dedicated explicit SOCKS5/HTTP and PAC-only HTTP routing regression tests passed.");

Console.WriteLine("Smart Proxy fallback/PAC synchronization regression tests passed.");
Console.WriteLine("Smart routing V4/V5 direct-fallback regression tests passed.");
Console.WriteLine("Smart routing V4/V5 model regression tests passed.");

sealed class TestCoreArchiveHandler(byte[] bytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
}

sealed class ProbeTestProcessLifetime : IChildProcessLifetime
{
    public void Attach(System.Diagnostics.Process process) =>
        throw new InvalidOperationException("Unit-only process lifetime must never spawn V2Fly.");
    public void Dispose() { }
}
