# External scenery

Place reviewed `scenery_*.json` files here. A case-insensitive basename matching a bundled asset REPLACES it; a new basename ADDS it. Never keep two casings of the same basename. The profile may restrict the allowed basenames. The core rejects unknown JSON `$type` classes and undefined/duplicate substations. This folder is empty in the foundation release.

When upgrading from GF station or Catenary 6 m Visual 2 test packages, move the old overrides out of this folder. See [the complete list](../docs/NETWORK_6M_RELEASE_UK.md). In particular, `scenery_gf_station_transit.json` is now split into bundled assets and must not remain active.

The accepted CP approaches (Visual3) are also bundled. After rebuilding and
installing this version, move the eight CP test overrides out of this folder;
see [the CP upgrade list](../docs/CP_RELEASE_UK.md#збирання-та-встановлення).

The accepted IMW–MF Visual8 layouts are now bundled too. After installing the
rebuilt mod, move all twelve IMW–MF review overrides outside the mod, including
`scenery_mf-industrial.json` and `scenery_mf-military.json`.
See [the complete IMW–MF list](../docs/IMW_MF_RELEASE_UK.md).

CW Visual7 is included in the rebuilt bundle. Move its six external review
overrides outside the mod when upgrading. [CW file list](../docs/CW_RELEASE_UK.md)

After installing the rebuilt FRC bundle, move the four external FRC review
overrides outside the mod. An older City West override for the shared CP
approach also takes precedence over the new bundle. [FRC file list](../docs/FRC_RELEASE_UK.md)
