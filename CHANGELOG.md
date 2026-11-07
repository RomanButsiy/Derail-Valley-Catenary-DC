# Changelog

## 0.2.0 — 2WE3 bridge e0b78cb / release tooling

- Pin optional bridge generation to author commit e0b78cb with 49 checked inputs.
- Preserve upstream gauges, resets, circuit/traction updates and delta-time handling.
- Keep the old simulate-argument correction only for the explicit 4d1a6a6 profile.
- Require SDK 10+ for C# 14; retain net48 and the separate controller-only design.
- Package bridge 0.2.0 with the exact core DLL hash and redact local source paths.
- Add paired Release ZIP packaging, hash checks and Ukrainian release/install guides.
- Accepted network geometry and core runtime are unchanged by this bridge update.
- Source preparation and packaging tests pass; Windows/Unity/game validation is pending.

## Scenery — GF station / 6 m Visual 2

- Integrated the accepted GF station, side rails and GF1100 supply.
- Preserved accepted 6 m route profiles, tunnel transitions and 161 frame-pole lifts.
- Integrated relocated N01, GF1500 feed towards FF and agreed deelectrification.
- Split station scenery into route/station/neutral assets: 48 layouts, 14,545 objects.
- Added exact overlay reconstruction checks and current release provenance.
- Requires a fresh Unity OCS export and local game retest; runtime code is unchanged.

## 0.1.0 — foundation / source-test candidate

- Extracted catenary-only runtime and Unity project from supplied `4d1a6a6`.
- Removed locomotive implementation/assets, CCL editor tooling, motor tests,
  obsolete packaging paths and original auto-updater metadata.
- Added independent assembly/UMM identity with a whitelist for legacy JSON labels.
- Preserved author's catenary assets and 31 reference layouts byte-for-byte.
- Added profile-based per-file selection/replacement and separate Debug workspace.
- Carried forward Release template/marker fixes; corrected source-distance math,
  per-world breaker lifecycle, limit serialization and reload cleanup.
- Added source-checked Unity export/build scripts, offline checks and pending
  production-logic C# tests.
- Added an optional local-only 2WE3 controller migration tool; no locomotive
  implementation is shipped in the core source/build.
- Recorded HB–CS–CMS–junction south of SM as the first **planned**, mainline-only
  corridor. No new route geometry is part of this version.

This version has not yet been built in Unity/Windows or tested in game.
