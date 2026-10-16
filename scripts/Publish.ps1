[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0',
    [switch]$BuildInstaller,
    [ValidateSet('Bundled', 'FrameworkDependent')][string]$RuntimeMode
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK before running this script.'
}
if ($BuildInstaller) {
    if ($Runtime -ne 'win-x64') { throw 'The installer currently supports win-x64; use the portable ARM64 archive.' }
    if (-not (Get-Command ISCC.exe -ErrorAction SilentlyContinue)) {
        throw 'Install Inno Setup 6 and add ISCC.exe to PATH to build an installer.'
    }
}

if (-not $RuntimeMode) {
    if ([Console]::IsInputRedirected) {
        throw 'Specify -RuntimeMode Bundled or FrameworkDependent for unattended publishing.'
    }
    Write-Host 'Choose how to publish Lantern:'
    Write-Host '  1. Bundle .NET (no separate runtime installation needed)'
    Write-Host '  2. Without .NET (requires .NET 10 Desktop Runtime on the target PC)'
    do {
        $choice = (Read-Host 'Select 1 or 2 [1]').Trim()
        switch ($choice) {
            '' { $RuntimeMode = 'Bundled' }
            '1' { $RuntimeMode = 'Bundled' }
            '2' { $RuntimeMode = 'FrameworkDependent' }
            default { Write-Host 'Please enter 1 or 2.' }
        }
    } until ($RuntimeMode)
}
$selfContained = if ($RuntimeMode -eq 'Bundled') { 'true' } else { 'false' }
$outputPath = Join-Path $repository "artifacts/$Runtime/$RuntimeMode"
$archivePath = Join-Path $repository "artifacts/Lantern-$Version-$Runtime-$RuntimeMode.zip"
Write-Host "Publishing $RuntimeMode for $Runtime."
if ($RuntimeMode -eq 'FrameworkDependent') {
    Write-Host "Target PCs need the .NET 10 Desktop Runtime matching $Runtime (the CLI needs .NET 10)."
}

& dotnet test (Join-Path $repository 'Lantern.Tests/Lantern.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
& dotnet publish (Join-Path $repository 'Lantern/Lantern.csproj') -c Release -r $Runtime --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:Version=$Version -o (Join-Path $outputPath 'app')
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
& dotnet publish (Join-Path $repository 'Lantern.Harness/Lantern.Harness.csproj') -c Release -r $Runtime --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:Version=$Version -o (Join-Path $outputPath 'cli')
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
Compress-Archive -Path (Join-Path $outputPath 'app/*'), (Join-Path $outputPath 'cli') -DestinationPath $archivePath -Force
if ($BuildInstaller) {
    & ISCC.exe "/DAppVersion=$Version" "/DSourceDirectory=$outputPath" "/DPackageFlavor=$RuntimeMode" (Join-Path $repository 'packaging/Lantern.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
Write-Host "Portable package ready: $archivePath"
