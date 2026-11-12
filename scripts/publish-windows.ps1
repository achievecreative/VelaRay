param(
    [ValidateSet('Folder', 'SingleFile')]
    [string] $Mode = 'SingleFile',
    [ValidateSet('win-x64')]
    [string] $Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src/VelaRay.Maui/VelaRay.Maui.csproj'
$output = Join-Path $root "publish/$Runtime-$Mode"

# Publish ONLY the MAUI frontend. v2ray.exe and Geo data are independent user-managed files.
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
$args = @(
    'publish', $project,
    '-c', 'Release', '-f', 'net10.0-windows10.0.19041.0', '-r', $Runtime,
    "-p:RuntimeIdentifierOverride=$Runtime",
    '-p:WindowsPackageType=None',
    '-p:WindowsAppSDKSelfContained=true',
    '-p:SelfContained=true',
    '-o', $output
)
if ($Mode -eq 'SingleFile') {
    $args += @(
        '-p:PublishSingleFile=true',
        '-p:IncludeAllContentForSelfExtract=true',
        '-p:EnableMsixTooling=true'
    )
}
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)" }
$exe = Join-Path $output 'VelaRay.exe'
if (-not (Test-Path $exe -PathType Leaf)) { throw 'Publish did not create VelaRay.exe' }
if ($Mode -eq 'SingleFile') {
    $otherRuntimeFiles = @(Get-ChildItem $output -File -Recurse | Where-Object {
        $_.Name -ne 'VelaRay.exe' -and $_.Extension -notin @('.pdb', '.xml')
    })
    if ($otherRuntimeFiles.Count -gt 0) {
        throw ('Single-file publication left runtime sidecars: ' + (($otherRuntimeFiles | Select-Object -ExpandProperty Name) -join ', '))
    }
    $distribution = Join-Path $root "publish/VelaRay-$Runtime.exe"
    Copy-Item $exe $distribution -Force
    Write-Host "Single EXE frontend: $distribution" -ForegroundColor Green
    Write-Host 'V2Fly v2ray.exe and .dat files are NOT embedded; install them separately under v2ray-core/.'
} else {
    Write-Host "Folder publish: $output" -ForegroundColor Green
}
Write-Host 'Portable settings/configs/pacs/logs are saved beside the real VelaRay.exe (writable folder required).'
