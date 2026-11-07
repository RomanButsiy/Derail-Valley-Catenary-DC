# External scenery

Place reviewed `scenery_*.json` files here. A case-insensitive basename matching a bundled asset REPLACES it; a new basename ADDS it. Never keep two casings of the same basename. The profile may restrict the allowed basenames. The core rejects unknown JSON `$type` classes and undefined/duplicate substations. This folder is empty in the foundation release.

When upgrading from GF station or Catenary 6 m Visual 2 test packages, move the old overrides out of this folder. See [the complete list](../docs/NETWORK_6M_RELEASE_UK.md). In particular, `scenery_gf_station_transit.json` is now split into bundled assets and must not remain active.
