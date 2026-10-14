# Build a single EXE from Rider

Open `Lantern.sln` in Rider with the .NET 10 SDK installed. The solution includes two packaging configurations, so no custom run configuration or publish-profile setup is required.

1. For reliable custom build steps, open **Settings → Build, Execution, Deployment → Toolset and Build** and clear **Use ReSharper Build** once. Rider then delegates the full build to MSBuild. See [JetBrains' build process documentation](https://www.jetbrains.com/help/rider/Build_Process.html).
2. In Rider's **solution build configuration selector** (the selector for Debug/Release, beside the Build button), select **SingleExe** or **SingleExeWithRuntime** and **Any CPU**. This is separate from the Run/Debug configuration selector. See [JetBrains' build configuration documentation](https://www.jetbrains.com/help/rider/Build_Configurations.html).
3. Click **Build Solution** (the hammer), use **Build → Build Solution**, or the corresponding build shortcut. After a successful build, the Build output prints `Single executable ready:` and the file path.

| Configuration | Target PC requirement | Output, relative to the repository root |
| --- | --- | --- |
| `SingleExe` | .NET **10 Desktop Runtime**, x64, installed | `artifacts/single-exe/win-x64/SingleExe/Lantern.exe` |
| `SingleExeWithRuntime` | .NET included; no separate .NET installation | `artifacts/single-exe/win-x64/SingleExeWithRuntime/Lantern.exe` |

Copy the EXE from **artifacts**, which is the distribution output. The intermediate `bin`/`obj` folders still contain compiler outputs and are not the single-file distribution. Both variants target 64-bit Windows by default. The small EXE is approximately 0.5 MiB; the bundled build approximately 111 MiB in the checked SDK build. Sizes can change with app and runtime versions.

The framework-dependent variant needs the Windows **Desktop** Runtime; having only an ASP.NET or base .NET runtime is insufficient for WinForms. The self-contained EXE bundles native runtime libraries and may extract required native components to the current user's temporary `.net` cache at launch. Settings, identity, trust, logs and downloads remain user data outside the EXE.

**Debug** and **Release** retain their normal build behavior. Select Debug again for routine IDE debugging. The packaging configurations enable release optimization, embed debug symbols, and leave trimming disabled for WinForms compatibility. They build only the desktop app and its core dependency; they do not run tests automatically.

If an EXE was manually deleted and Rider reports everything up to date, use **Rebuild Solution**; also check that ReSharper Build is disabled. Do not delete or move `Lantern.exe` while that exact copy is running during a rebuild.

## Equivalent commands

```powershell
dotnet build Lantern.sln -c SingleExe
dotnet build Lantern.sln -c SingleExeWithRuntime
```

For Windows ARM64, the command-line builds also accept `-r win-arm64`; their outputs use `artifacts/single-exe/win-arm64/…` instead. An x64 IDE configuration does not automatically select ARM64.

## Verification and implementation

`Directory.Build.props` defines both configuration names and release optimization/embedded symbols for referenced assemblies. The desktop project selects the runtime and bundling options, then runs the .NET SDK publish target after a normal build. It reuses compiled outputs with `NoBuild=true`, prevents publishing from recursively triggering itself, and skips design-time builds. Debug/Release builds do not invoke this target.

Both x64 solution builds were executed with warnings treated as errors in the managed Linux SDK environment. Each distribution directory contains exactly one `Lantern.exe`; PE headers identify x64 Windows GUI executables. Bundle manifests confirm external .NETCore/WindowsDesktop framework requirements for `SingleExe` and included frameworks/WinForms assemblies for `SingleExeWithRuntime`. This validates artifact generation; Rider itself and Windows execution were not available in that environment. Windows CI repeats both solution builds and checks the one-file output contract.
