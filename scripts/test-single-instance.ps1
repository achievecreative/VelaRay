# Run interactively on Windows after publishing. This deliberately launches two
# GUI instances and verifies that the second one exits while the first remains.
param(
    [string]$Executable = "publish/VelaRay-win-x64.exe"
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path $Executable).Path
$first = $null
$second = $null
try {
    $first = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 4
    $first.Refresh()
    if ($first.HasExited) { throw "First instance exited unexpectedly (code $($first.ExitCode))." }
    $second = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 4
    $first.Refresh(); $second.Refresh()
    if ($first.HasExited) { throw 'The first instance was terminated unexpectedly.' }
    if (-not $second.HasExited) { throw 'Second instance is still running: singleton guard failed.' }
    if ($second.ExitCode -ne 0) { throw "Second instance returned code $($second.ExitCode)." }
    Write-Host 'PASS: second launch exits and first UI remains running.' -ForegroundColor Green
    Write-Host 'Manually confirm that the first window is restored from the tray.'
}
finally {
    if ($second -and -not $second.HasExited) { Stop-Process -Id $second.Id -Force -ErrorAction SilentlyContinue }
    if ($first -and -not $first.HasExited) { Stop-Process -Id $first.Id -Force -ErrorAction SilentlyContinue }
}
