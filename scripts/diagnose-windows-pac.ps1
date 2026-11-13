# Read-only inspection: verify the Windows system proxy points to the selected
# VelaRay PAC URL and inspect what its script returns for unmatched hosts.
$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
$settings = Get-ItemProperty -Path $key
Write-Host "Windows proxy state (current user):" -ForegroundColor Cyan
$settings | Select-Object ProxyEnable, ProxyServer, AutoConfigURL, AutoDetect | Format-List
if ([string]::IsNullOrWhiteSpace($settings.AutoConfigURL)) {
    Write-Host 'No automatic PAC URL configured. The current Windows system proxy is not in PAC mode.' -ForegroundColor Yellow
    exit 0
}
$uri = [uri]$settings.AutoConfigURL
if (-not $uri.IsLoopback -or $uri.Scheme -ne 'http') {
    Write-Host 'The PAC URL is not VelaRay loopback; skipping fetch.' -ForegroundColor Yellow
    exit 0
}
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.UseProxy = $false
$http = New-Object System.Net.Http.HttpClient($handler)
$http.Timeout = [TimeSpan]::FromSeconds(5)
try {
    $script = $http.GetStringAsync($uri).GetAwaiter().GetResult()
    Write-Host "PAC script reachable: $($script.Length) chars" -ForegroundColor Green
    Write-Host 'Note: the final return in FindProxyForURL determines the default route.'
    $script -split "`r?`n" | Where-Object { $_ -match "return 'DIRECT'|return 'PROXY" } | Select-Object -Last 8
}
catch {
    Write-Host "Cannot fetch PAC script: $_" -ForegroundColor Red
    exit 1
}
finally {
    $http.Dispose()
    $handler.Dispose()
}
