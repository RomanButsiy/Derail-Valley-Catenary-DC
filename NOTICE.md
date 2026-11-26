# Attribution and scope

Derail-Valley-Catenary-DC is an independent catenary-focused derivative of
Knsgf's **Derail_Valley_electric_concept**, based on the user-supplied archive
`Derail_Valley_electric_concept-full_circuit(1)(1).zip`.

- Upstream: https://github.com/Knsgf/Derail_Valley_electric_concept
- ZIP commit comment: `4d1a6a691415b6f08f9a76fbafdc8357485e277a`
- Archive SHA-256: `22b9faeed65d60d45a99f675e91f3b27d76ecab0ec6ad1f917dfdeff2fe8d169`
- Original source/asset hashes are recorded in `audit/upstream.json`.

The retained overhead-equipment models, textures, material definitions, scenery,
editor and much of the runtime originate with Knsgf. This is not an official
update endorsed by the original author. This project does not claim that its
future routes are contributions accepted into the original main release.

The original `LICENSE_CC0.txt` and `LICENSE_MIT.txt` are preserved verbatim.
Respect the individual upstream file notices. New catenary-core changes and
tooling are supplied under CC0; the MIT notices remain applicable to upstream
MIT-covered code and are retained. Game binaries, Unity Editor, UMM and other
third-party packages retain their own licenses and are not redistributed here.

The core does NOT contain the 2WE3 vehicle model, cab, traction circuit,
control stand, Custom Car Loader binaries or the author's Blender locomotive
file. `compat/Legacy2WE3` is an optional local migration recipe: it reads the
user's exact pinned author source and stages a separate controller outside the core.
That controller uses the original vehicle implementation and its notices.
It does not install or redistribute the CCL model pack.

The bridge generator for 0.3.0 uses author commit
`03d9de65c874797718accae865295862ab588d62` (v2.8.0). The 52 selected input
hashes are recorded in `compat/Legacy2WE3/upstreams/03d9de6.json`. Instruments,
circuits and vehicle simulation remain Knsgf's implementation. The generator
also accepts the historical revisions `e0b78cb` and `4d1a6a6` explicitly.

The bridge exposes the contact API used by Knsgf's Custom Car Loader Catenary
Support. Players obtain that importer from Knsgf and the WE-4U model and
electrics from Comrad501. Their binaries, locomotive assets and traction code
for WE-4U are distributed by their respective authors.

The FF passenger station layout and its slim-gantry registration arms come
from Knsgf's v2.9.0, commit `ba44c0df9c0b62760bbb4aefb015839c166293e9`.
The SM station layout was compared with the same release. The models and
meshes for the two new arms retain their original Unity asset identities.
