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

The optional bridge generator now defaults to author commit
`e0b78cb7943ca4d7cde112065b863139dae0c7b0`. Its 49 selected input hashes are in
`compat/Legacy2WE3/upstreams/e0b78cb.json`. The original author owns the
instruments, circuits, traction and vehicle simulation implementation carried
into that bridge. This derivative adapts contact queries and lifecycle hooks
to the independent catenary API. It does not import the newer author's OCS,
scenery, Double-Tracked Valley power cheat or vehicle model assets into the core.
The older `4d1a6a6` controller remains an explicitly selected compatibility path;
the catenary foundation provenance above remains unchanged.
