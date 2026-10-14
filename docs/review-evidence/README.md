# Review evidence, 2026-10-08

`observations.json` records the final 17 diagnostic observations: 16 reproduced defect observations and one positive concurrent-send check. `Probes/Program.cs` contains the diagnostic source. A `reproduced: true` value confirms the named observation on the reviewed source; for defect names this does **not** mean a correctness test passed.

`phase12-baseline.trx` contains the 32/32 existing test result with collection parallelism disabled. `phase12-default-parallel.trx` contains the default-settings result: 30 passed and 2 discovery bind failures. Both ran a source-linked `net10.0` copy of the backend and existing tests, not the WindowsDesktop runtime. Review probes are separate from the existing test suite and are not included in the solution.

## Diagnostic probe reproduction (Linux, .NET 10)

From the repository root, choose an isolated scratch directory, then run:

```bash
mkdir -p work/lantern-review
export LANTERN_REVIEW_WORKDIR="$PWD/work/lantern-review"
dotnet run --project docs/review-evidence/Probes/Probes.csproj -- --sockets
```

The socket option runs one additional local TCP lifecycle check; without it, the probe uses memory streams, reflection and isolated identity fixtures only. The fixture uses Linux XDG application-data behavior and refuses to run identity checks if it cannot select the isolated path. Run it only in the disposable scratch directory. It deliberately creates invalid identity-file fixtures under that directory and changes its process XDG_CONFIG_HOME; it must not target real user settings.

The probes emit JSON and exit zero when all named baseline observations are reproduced. After defects are fixed, they may exit nonzero; turn relevant cases into assertions of correct behavior in the real test suite rather than maintaining a suite that expects bugs forever. Reflection is used to observe private code in this review, not recommended as the long-term test API.

The Core project source-links the existing backend with unchanged namespaces and the assembly name expected by InternalsVisibleTo. It is evidence infrastructure, not an implemented architectural extraction.

## Existing test reproduction

On Windows, use the real solution:

```powershell
dotnet build Lantern.sln
dotnet test Lantern.sln
```

Default test execution may reproduce shared-port collisions. The successful review run used this setting:

```xml
<RunSettings>
  <xUnit>
    <ParallelizeTestCollections>false</ParallelizeTestCollections>
    <MaxParallelThreads>1</MaxParallelThreads>
  </xUnit>
</RunSettings>
```

Save it in a temporary `.runsettings` file and pass `--settings` when comparing serialized behavior. This external workaround does not repair the production tests.

On Linux, the original WinForms-referencing tests cannot run directly without WindowsDesktop. The workspace already provided `.lantern-setup/Tests/Tests.csproj`, linking the same source and xUnit packages against a `net10.0` backend. The review used SDK `/workspace/.dotnet/dotnet`, DOTNET_ROOT `/workspace/.dotnet`, DOTNET_CLI_HOME `/workspace/.lantern-setup/cli`, and NUGET_PACKAGES `/workspace/.nuget/packages`, with `--no-restore`. A fresh environment should use a similarly explicit source-linked test project or implement the separately proposed Core extraction; do not interpret cross-compilation as Windows runtime validation.

## Build evidence

The original solution completed an incremental Linux cross-build using:

```bash
dotnet build Lantern.sln --no-restore -p:EnableWindowsTargeting=true -m:1 --nologo
```

Reported result: Build succeeded, 0 warnings, 0 errors. This did not execute WinForms, measure performance, or test two-machine LAN broadcast.
