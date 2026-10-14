# Lantern

Lantern is a Windows desktop app for sending files and folders to paired PCs on your local network. It uses mutual TLS, verified device identities, explicit receive approval, bounded streaming and SHA-256 verification. Files appear only after the whole batch has been verified.

## Run

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet run --project Lantern
```

Open Lantern on both PCs. Select a nearby device, pair the device IDs and fingerprints on both PCs through a trusted channel, then choose files or folders and send. The recipient chooses a destination and accepts or declines the request. Discovery names alone never establish trust.

The main tabs cover nearby devices, file selection, activity and settings. Settings include receive location, naming, notifications, tray behavior, Windows startup, light/dark/system theme, pairing management and diagnostics. `Ctrl+O` adds files, `F5` refreshes the device view, `Ctrl+,` opens settings, and `Ctrl+Q` exits.

## Single EXE from Rider

Select **SingleExe** (requires .NET 10 Desktop Runtime) or **SingleExeWithRuntime** (.NET included) in Rider's solution build configuration selector, then click **Build Solution**. The EXE is written under `artifacts/single-exe/win-x64/<configuration>/Lantern.exe`.

For the custom publish step to run reliably, clear **Use ReSharper Build** under **Settings → Build, Execution, Deployment → Toolset and Build** once. See [Rider build instructions](docs/RIDER_BUILDS.md) for the exact steps and output paths.

## Validate and package

```powershell
dotnet build Lantern.sln -c Release -warnaserror
dotnet test Lantern.Tests -c Release
dotnet test Lantern.UiTests -c Release  # Windows required
./scripts/Publish.ps1                  # Windows portable packages
./scripts/Publish.ps1 -BuildInstaller  # Requires Inno Setup 6
```

The portable backend and its tests run on Linux as well. Windows UI execution, physical LAN operation, multi-monitor DPI, accessibility and installer execution require their own platform checks; a cross-build does not establish those results.

See [UI workflows](docs/UI.md), [validation](docs/VALIDATION.md), [phase progress](docs/PHASE_PROGRESS.md), [backend API/security boundaries](docs/FILE_TRANSFERS.md), and the [canonical specification](docs/PROJECT_SPECIFICATION.md).
