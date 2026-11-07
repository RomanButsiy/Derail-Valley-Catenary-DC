# Catenary DC 0.2.0 + 2WE3 bridge

First packaged release of this fork's accepted GF / 6 m Visual 2 network,
with a separate optional 2WE3 controller built from Knsgf's
`e0b78cb7943ca4d7cde112065b863139dae0c7b0`.

- Network: HB–CS–CMS–SM, HB–GF–mines, completed GF station and connections;
  accepted tunnel/station heights, open-route 6 m profiles, side rails and feeds.
- Bridge: upstream instruments, gauge resets, circuit/traction updates and
  explicit simulation time-step handling, connected to this fork's vehicle API.
- Two independent UMM ZIPs. The bridge is built against the packaged core DLL.
  CCL and 2WE3CCL models remain separate dependencies.

Install the core ZIP; for 2WE3 also install the bridge, CCL and 2WE3CCL 2.3.7.
Read the attached **INSTALL_UK.md** before upgrading. Remove the original
`electric_sim` mod and old test scenery overrides. Do not install GitHub's
automatic Source code ZIP through UMM. Checksums and build provenance are attached.

Standard track layout only; Double-Tracked Valley is not supported by this profile.
No claim of VR/multiplayer compatibility.

## Maintainer: complete before publishing

- Derail Valley / UMM / CCL / 2WE3CCL versions tested: **fill in**.
- Release build, fresh install and reload results: **fill in**.
- Routes, neutral sections, side rails, A/B traction and gauge checks: **fill in**.
- Known issues from those tests: **fill in**.

Source preparation passed automated Python checks. The patch preparation
environment did not run .NET compilation, Unity export, PowerShell or the game.
Replace this maintainer section with the actual local validation results.

Original locomotive and overhead-equipment work by **Knsgf**.
See the bundled NOTICE, CC0 and MIT license files.
