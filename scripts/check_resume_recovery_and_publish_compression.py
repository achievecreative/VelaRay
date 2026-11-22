"""Static contracts for Windows wake recovery and compressed single-file publishing.

This does not replace compiling MAUI on Windows or testing actual sleep/resume.
"""
from pathlib import Path

root = Path(__file__).resolve().parents[1]
read = lambda p: (root / p).read_text(encoding="utf-8-sig")
app = read("src/VelaRay.Maui/App.xaml.cs")
page = read("src/VelaRay.Maui/MainPage.xaml.cs")
runner = read("src/VelaRay.Core/Runtime/V2RayCoreRunner.cs")
proxy = read("src/VelaRay.Maui/Services/WindowsSystemProxyService.cs")
publish = read("scripts/publish-windows.ps1")
ci = read(".github/workflows/windows.yml")

assert "PowerManager.SystemSuspendStatusChanged += OnSystemSuspendStatusChanged" in app
assert "PowerManager.SystemSuspendStatusChanged -= OnSystemSuspendStatusChanged" in app
assert "SystemSuspendStatus.Entering" in app
assert "SystemSuspendStatus.AutoResume" in app and "SystemSuspendStatus.ManualResume" in app
assert "NotifySystemSuspending" in app and "NotifySystemResumed" in app
assert "CaptureRunningConnectionForResume" in page
assert "_suspendedConnection ?? CaptureRunningConnectionForResume()" in page
assert "context.Revision != Interlocked.Read(ref _connectionRevision)" in page
assert "_connectionGate.WaitAsync(token)" in page
assert "_core.StartAsync(context.Executable, context.ConfigPath, context.Format, token)" in page
assert "NetworkInterface.GetIsNetworkAvailable()" in page
assert "_proxy.RefreshIfOwned()" in page
assert "context.ProxyMode == \"PAC\"" in page
assert "_core.RunningConfigPath" in page
assert "RunningConfigPath { get; private set; }" in runner
assert "RecordAppliedSnapshot()" in proxy
assert "bool RefreshIfOwned()" in proxy
assert "CancelResumeRecovery()" in page
assert "Interlocked.Increment(ref _connectionRevision)" in page
assert "-p:EnableCompressionInSingleFile=true" in publish
single = publish.index("if ($Mode -eq 'SingleFile') {")
flag = publish.index("'-p:EnableCompressionInSingleFile=true'")
assert single < flag < publish.index("& dotnet @args")
assert "./scripts/publish-windows.ps1 -Mode SingleFile" in ci
print("Windows resume + single-file compression source contract OK")
