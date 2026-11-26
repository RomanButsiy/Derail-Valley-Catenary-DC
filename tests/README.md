# Test boundaries

## CCL pantograph contact

Run this test once per supported importer tag with .NET SDK 10 or later. Use a separate output directory for each tag so that MSBuild always compiles the selected source.

```powershell
git clone --depth 1 --branch v1.0.0 https://github.com/Knsgf/CCL-OCS ../CCL-OCS-1.0
git clone --depth 1 --branch v1.1.0 https://github.com/Knsgf/CCL-OCS ../CCL-OCS-1.1
foreach ($tag in '1.0','1.1') {
    $source = (Resolve-Path "../CCL-OCS-$tag").Path
    $out = (Join-Path (Get-Location).Path "../ccl-test-$tag")
    dotnet build tests/CclContact/CclContact.csproj -c Release "-p:CclSource=$source" --artifacts-path $out
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet "$out/bin/CclContact/release/CclContact.dll"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
```

The runner compiles the unmodified upstream importer controller and uses its normal reflection lookup, initialization, delegates and ticks against the production bridge and public network API. Game ports, transforms and the sampled world are test doubles. It checks live and dead overhead, a physical gap, load accounting for two collectors, world unload/reload, and original-engine detection.

In the published WE-4U InDev-0.3.1 `EU07/ccl_bundle`, the locomotive uses the pantograph controller proxy and the power bus bar. The sandbox and career 2WE3 v2.8.0 bundles both use the `WE6981A` and `WE6981B` livery prefixes recognized by the stock 2WE3 controller.

## FRC Visual2

Run `python tools/verify_network_release.py --frc-accepted-zip FRC_Visual2.zip --cw-accepted-zip CW_Visual7.zip`
to compare both review archives. The verifier restores the pre-refeed layouts from
`audit/pre_imw_refeed.json.gz` before checking FRC Visual2. It then restores the preceding
CW version of the shared CP approach from `audit/pre_frc_visual2.json.gz`.

Run `python tools/test_network_release.py` to check that the former neutral section on the
FRC approach to CP uses IMW1500 and that the Farm and OR neutral sections and water tower signs
match the previous layout. `python tools/validate_project.py` also checks the SM and FF station
sources and the imported gantry assets against the recorded v2.9.0 hashes.

## CW Visual7

Run `python tools/verify_network_release.py --cw-accepted-zip CW_Visual7.zip` to compare all six accepted CW payloads, including the preceding version of the shared CP approach. The network tests also verify the previous CP, IMW–MF and GF/6 m layouts across the four replaced assets.


## IMW–MF / player tracking update — 2026-11-15

```powershell
dotnet run --project tests/PlayerTracking -c Release
python tools/verify_network_release.py --imw-mf-accepted-zip IMW-MF-Visual8.zip
python tools/test_network_release.py
```

The 15 player-tracking tests compile the actual `OCS_ticker.cs` and
`player_tracking.cs` with small Unity lifetime/event doubles. They reproduce the
old destroyed-camera failure, test camera/player fallback and recovery, teleport
suspension, destroyed-host unsubscription, 20 load/unload cycles, old-world
isolation and disposal/replacement during a simulation callback. They are not a
Unity scene test and do not compile the whole net48 mod against game assemblies.
These tests were executed with .NET SDK 8.0.428; the source is unchanged between
the test runner and the core build.
The existing 19 CoreLogic and 10 AuthoringData checks also passed on that SDK.

The network verifier compares all 12 current payloads to accepted Visual8.
Four superseded CP-era payloads remain frozen in `audit/cp_pre_imw_mf.json.gz`
and are checked against their original CP hashes, while their active replacements
must match Visual8 separately. The unchanged CP assets and all 28 earlier
GF / 6 m overlay reconstructions remain checked against live assets.

The sections below describe the earlier preparation environment. Full core
Release/Debug compilation, Unity export and actual save reload still require
the local game installation.

## Executed offline during preparation

`python tools/validate_project.py --upstream <original source>` checks the
actual packaged source/data, asset GUID closure, preserved hashes, selected
source invariants, and prepares the optional adapter in a temporary directory.
This is not C# execution. The output is `audit/offline_validation.json`.

## Prepared, NOT executed in the preparation environment

```powershell
dotnet run --project tests/CoreLogic/CoreLogic.csproj -c Release
```

This 19-case console runner links the actual production `ElectricalMath`,
`SubstationState`, `CsvTable` and `CSV_struct` files. It uses a logger stub, not
Unity or game DLLs. It tests distance units/overflow direction, load accumulation,
10/30 s state restoration, invalid limits/current, CSV quoting/rows/finite data.
.NET SDK 8+ is required; RollForward=Major permits a newer installed runtime.

## Still needed locally

Core net48 Release **and** Debug compilation against installed game assemblies;
Unity export; PowerShell build/receipt checks; clean install; double-engine
conflict test; repeated world load/unload; in-game reference network inspection;
optional controller compile and contact/traction test; invalid profile rollback;
Debug workspace save and reload. No .NET/Unity/PowerShell executables or game
assemblies were available in the preparation environment.

No layout-construction certificate is implied: force direction, zigzag,
independent wire runs and two-pantograph neutral-section geometry are a separate
route-generation/audit task.


## DevTools.1

`python tools/test_authoring_tools.py` runs data fixtures and source-safety regression checks.
`dotnet run --project tests/AuthoringData -c Release -- <repository-root>` compiles and
runs the production C# data-only validator, without Unity/game assemblies.
`tools/Test-BuildConfigurations.ps1` performs both actual core builds on the user's
Windows/game environment. Neither the C# nor Windows commands were executed during package preparation.
