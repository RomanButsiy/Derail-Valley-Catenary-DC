# Test boundaries

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
