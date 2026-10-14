[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0',
    [switch]$BuildInstaller
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $repository "artifacts/$Runtime"

& dotnet test (Join-Path $repository 'Lantern.Tests/Lantern.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
& dotnet publish (Join-Path $repository 'Lantern/Lantern.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o (Join-Path $outputPath 'app')
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
& dotnet publish (Join-Path $repository 'Lantern.Harness/Lantern.Harness.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o (Join-Path $outputPath 'cli')
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
Compress-Archive -Path (Join-Path $outputPath 'app/*'), (Join-Path $outputPath 'cli') -DestinationPath (Join-Path $repository "artifacts/Lantern-$Version-$Runtime.zip") -Force
if ($BuildInstaller) {
    if ($Runtime -ne 'win-x64') { throw 'The installer currently supports win-x64; use the portable ARM64 archive.' }
    & ISCC.exe "/DAppVersion=$Version" "/DSourceDirectory=$outputPath" (Join-Path $repository 'packaging/Lantern.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
