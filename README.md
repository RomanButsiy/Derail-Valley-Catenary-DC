# Derail-Valley-Catenary-DC

**Catenary-only foundation for a separately maintained Derail Valley DC network.**

Version **0.1.0 — source refactor / local-test candidate**. Based on the supplied
Knsgf `full_circuit` snapshot `4d1a6a691415b6f08f9a76fbafdc8357485e277a`.
This is not an official upstream release and not a prebuilt mod download.

**[Українська інструкція складання і тесту](docs/BUILD_UK.md)** ·
[Code review](docs/CODE_REVIEW_UK.md) · [Vehicle API](docs/VEHICLE_API.md) ·
[First corridor](plans/PHASE1_HB_CS_CMS_UK.md)

## What is in this repository

- Overhead equipment models, wires, poles, tensioning parts and CSV definitions.
- Rendering, contact queries, origin shifting, substations and the in-game Debug editor.
- A restricted reader for the original scenery JSON type names.
- Explicit network profiles and per-file replacement/addition under `Scenery/`.
- A vehicle-neutral API and one simulation tick; **no built-in locomotive controller**.
- The author's **31 original scenery files, unchanged**, as the `author-reference`
  test profile. They are a baseline, not the final industrial-network design.

The 2WE3 locomotive models, cabins, traction circuits, control stands, CCL editor
binaries, Blender source and traction-motor test rig are removed from the core.
The electricity-billing UI is vehicle-specific and is not part of the core.

## Important compatibility boundary

| Component | UMM ID | Assembly | Included by the main build |
|---|---|---|---|
| Catenary core | `DerailValleyCatenaryDC` | `CatenaryDC.dll` | Yes |
| Optional local 2WE3 controller adapter | `Catenary-DC` | `CatenaryDC.Legacy2WE3.dll` | **No** |
| Original author engine | `Catenary-DC` / `electric_sim` | `electric_sim.dll` | **Must not run alongside this core** |

The legacy controller ID is intentional: the supplied `WE6_pack.asset` lists
`Catenary-DC` as an additional dependency. The optional adapter satisfies that
model-pack dependency **without a second catenary engine**. It must replace,
not coexist with, the original `electric_sim` mod. It is locally generated from
the exact original author source; the locomotive implementation is not stored
in this repository. Keep the separately installed 2WE3CCL models and CCL for
vehicle tests. See [optional bridge instructions](compat/Legacy2WE3/README_UK.md).

For a network-only smoke test, use a stock diesel. Ordinary diesel/battery
vehicles do not gain pantographs just by installing this core. Removing the
original simulation and installing only the CCL model does not preserve 2WE3
traction; a compatible controller is required.

## Build

Required locally: Unity **2019.4.41f2**, .NET SDK **8 or newer** (C# 12), the
installed Derail Valley managed assemblies and working Unity Mod Manager.
The runtime target is **net48**, not .NET 8/10. Framework reference assemblies
are restored as a NuGet package. No game DLLs or third-party binaries ship here.

1. Open `unity_project` using Unity 2019.4.41f2.
2. Run **Catenary → Export OCS**. This exports only `catenary` and
   `catenary_parts` for Windows, plus a source/hash receipt.
3. Close the game and run `BUILD.cmd`, selecting the game root when prompted.
   `BUILD_DEBUG.cmd` builds the editor variant instead.

Equivalent PowerShell:

```powershell
.\tools\Build.ps1 `
  -GameDirectory 'C:\Program Files (x86)\Steam\steamapps\common\Derail Valley' `
  -Configuration Release
```

The script builds in a separate working directory and packages
`artifacts/<build>/ready-to-install/Derail-Valley-Catenary-DC`.
**It never installs, edits the game, or invokes the old package.ps1.**
Re-export Unity assets when models/CSV/scenery change. C#-only edits do not
require another export as long as the receipt remains valid.

## Test installation

Back up and move the original `Mods/Catenary-DC` outside `Mods`; do not merely
rename it inside the mods directory. Install the newly built core folder.
Do not transfer old `scenery_fm.json`, `scenery_or.json`, Debug workspace files
or settings blindly. Start with the unchanged `author-reference` profile.
Use a separate Sandbox save on the standard track layout. Double-Tracked Valley
is deliberately unsupported by this profile. VR/multiplayer are not tested.

**Do not install this source ZIP with UMM.** Only the build output is a mod.
After testing, restoration is simply moving the new core/optional adapter out
of `Mods` and restoring the complete previous mod folder, with the game closed.

## Profiles and editing

`network.json` is copied from `config/network.json` into the built mod:

```json
{
  "schema_version": 1,
  "name": "author-reference",
  "included_scenery": null,
  "load_external_scenery": true
}
```

`null` loads all bundled/external scenery basenames. An explicit array is an
allowlist. An external `Scenery/scenery_*.json` replaces the bundled file with
the same basename; a new basename adds a layout. Duplicate names, unknown feeds
and unsupported JSON types are rejected. Root-level old overrides are ignored
with a warning. A profile that omits a needed substation fails instead of
silently loading dead wires.

Debug tools are kept for authoring; their workspace is **UserData/**, not the
production scenery folder. Leave placement Disabled and Eraser off while
inspecting. Save/reload after electrical edits. Live editor modifications are
not a proof of a valid tension system or neutral section. Do not use old
editor exports as production data without review.

## Current fixes

- Release CS0571: wire template helper is available in both configurations;
  newest upstream Debug `ShortWireMarker` behavior remains unchanged.
- Release hides substation markers without removing electrical sources.
- Corrected Z-distance term in voltage drop; explicit numeric validation.
- Per-world, main-thread substation restoration; no delayed tasks after unload.
- Nominal load settings are serialized without repeatedly applying multipliers.
- Failed loads roll back bundles/listeners/objects; normal teardown clears state.
- Template dictionaries reset on reload; contact search includes long spans.
- Restricted JSON type migration and standalone CSV parsing.
- Empty tunnel-range guard and Debug monitor cleanup.

See the review for limitations. No new short-circuit solver, pantograph model,
network-wide power-flow solver or automatic geometry validator is claimed.

## First construction phase — not yet implemented

**HB → CS → CMS → central junction/ring south of SM.**
Mainline only, with station through routes and necessary switches; no new yard
I/O/L/S coverage in this phase. Planned district interfaces: N09 (HB/CMS) and
N04 (CMS/SM), no extra neutral at CS. The initial project contains **no newly
placed wires for this corridor**; `plans/` records scope rather than fake
runtime geometry. Prior FRC Rework.1/final.9 is not silently merged into this
new author-source baseline.

## Checks

```powershell
python .\tools\validate_project.py
# Additional byte-comparison and adapter preparation checks:
python .\tools\validate_project.py --upstream 'C:\DV-Work\AuthorSource'
# Pure production logic tests; no game DLLs needed:
dotnet run --project .\tests\CoreLogic\CoreLogic.csproj -c Release
```

Python checks were executed during preparation. **C# compilation, the C# test
runner, Unity export, Windows PowerShell and in-game behavior were not executed
in the preparation environment.** See `audit/offline_validation.json` for the
actual run. No test count is presented as runtime certification.

## Attribution

See [NOTICE.md](NOTICE.md), [CC0](LICENSE_CC0.txt) and [MIT](LICENSE_MIT.txt).
Keep those notices when distributing a derivative. The original layouts and
models are credited to Knsgf. No claims are made about upstream accepting this
network expansion into its main release.
