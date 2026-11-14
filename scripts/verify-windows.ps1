param(
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    python scripts/static_check.py
    if ($LASTEXITCODE -ne 0) { throw 'Static check failed' }
    python scripts/audit_ux_localization.py
    if ($LASTEXITCODE -ne 0) { throw 'UX/i18n/titlebar check failed' }
    python scripts/review_semantic_singleinstance_routing.py
    if ($LASTEXITCODE -ne 0) { throw 'Semantic i18n, routing and single-instance source review failed' }
    python scripts/check_connection_selection.py
    if ($LASTEXITCODE -ne 0) { throw 'Connection-selection regression check failed' }
    python scripts/check_core_updates_and_connection_action.py
    if ($LASTEXITCODE -ne 0) { throw 'Unified connection/core-update regression check failed' }
    python scripts/check_manual_core_update_policy.py
    if ($LASTEXITCODE -ne 0) { throw 'Manual-only V2Fly core check policy failed' }
    python scripts/check_header_action_placement.py
    if ($LASTEXITCODE -ne 0) { throw 'Top-right header action regression check failed' }
    python scripts/check_log_panel_action_cleanup.py
    if ($LASTEXITCODE -ne 0) { throw 'Log header actions and sidebar check-config regression failed' }
    python scripts/check_ui_logging_policy.py
    if ($LASTEXITCODE -ne 0) { throw 'Stable actions, language and bounded logging check failed' }
    python scripts/check_workspace_and_platform.py
    if ($LASTEXITCODE -ne 0) { throw 'Workspace and platform boundary check failed' }
    python scripts/check_pac_js.py
    if ($LASTEXITCODE -ne 0) { throw 'PAC test failed' }
    python scripts/check_smart_routing_contract.py
    if ($LASTEXITCODE -ne 0) { throw 'Smart routing source-contract check failed' }
    python scripts/check_smart_proxy_pac_sync.py
    if ($LASTEXITCODE -ne 0) { throw 'Smart Proxy fallback and PAC suffix integration check failed' }
    python scripts/check_routing_editor_policy_separation.py
    if ($LASTEXITCODE -ne 0) { throw 'Routing editor/policy separation check failed' }
    python scripts/check_proxy_mode_switch_and_pac_fallback.py
    if ($LASTEXITCODE -ne 0) { throw 'System proxy mode transition and PAC default regression failed' }
    python scripts/check_reference_ui_and_pending_pac.py
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot card layout / staged PAC apply check failed' }
    python scripts/check_six_ux_proxy_modes.py
    if ($LASTEXITCODE -ne 0) { throw 'Six UX improvements / PAC-Global-Manual mode contract failed' }
    python scripts/check_server_profile_model_boundary.py
    if ($LASTEXITCODE -ne 0) { throw 'Server profile and routing model boundary check failed' }
    python scripts/check_compact_sidebar_and_proxy_modes.py
    if ($LASTEXITCODE -ne 0) { throw 'Sidebar/compact server card/responsive proxy-mode UX regression failed' }
    python scripts/check_traffic_stats_and_78dp.py
    if ($LASTEXITCODE -ne 0) { throw 'Traffic statistics and 78dp server-card regression failed' }
    python scripts/check_server_card_text_alignment.py
    if ($LASTEXITCODE -ne 0) { throw 'Server-card name/address alignment regression failed' }
    python scripts/check_smart_node_latency.py
    if ($LASTEXITCODE -ne 0) { throw 'Smart node latency / isolated UDP probe source review failed' }
    python scripts/check_multiline_editor_scrollbars.py
    if ($LASTEXITCODE -ne 0) { throw 'Multiline Editor scrollbar regression check failed' }
    dotnet build src/VelaRay.Core/VelaRay.Core.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Portable core compilation failed' }
    dotnet --version
    dotnet workload list
    dotnet restore tests/VelaRay.SmokeTests/VelaRay.SmokeTests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test restore failed' }
    dotnet run --project tests/VelaRay.SmokeTests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'C# smoke tests failed' }
    # Real Windows OS behavior: kill-on-job-close and forced parent termination.
    dotnet run --project tests/VelaRay.WindowsLifetimeTests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Windows child-process crash survival test failed' }
    $project = 'src/VelaRay.Maui/VelaRay.Maui.csproj'
    dotnet restore $project -p:TargetFramework=net10.0-windows10.0.19041.0
    if ($LASTEXITCODE -ne 0) { throw 'MAUI restore failed' }
    foreach ($configuration in @('Debug', 'Release')) {
        dotnet build $project -c $configuration -f net10.0-windows10.0.19041.0 --no-restore
        if ($LASTEXITCODE -ne 0) { throw "$configuration MAUI compilation failed" }
    }
    if (-not $SkipPublish) {
        ./scripts/publish-windows.ps1 -Mode SingleFile
        if ($LASTEXITCODE -ne 0) { throw 'Single EXE publishing failed' }
    }
    Write-Host 'VelaRay verification stages completed.' -ForegroundColor Green
}
finally { Pop-Location }
