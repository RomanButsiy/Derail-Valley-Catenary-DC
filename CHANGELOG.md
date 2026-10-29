# Changelog

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
