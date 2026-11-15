# Editing catenary layouts

Before changing trackside geometry, read `docs/CATENARY_AUTHORING_NOTES_UK.md`.
It records the owner's accepted design decisions and recurring regression cases.

- Preserve the latest accepted layout outside the requested area. Use the user's specified branch and record the base commit.
- Treat a pole, its physical mounting axis, cantilevers, fixed wire ends, guys and signs as dependent objects. Spatial deletion alone is not sufficient.
- Inspect actual asset geometry and runtime contact semantics, not just JSON coordinates or enum names.
- Validate rendered endpoints, runtime pickup continuity, electrical feeds and both locomotive orientations separately.
- For side rails, use a small number of long constant-offset sections with measured diagonal-pickup handovers. Do not default to rails on both sides of every track.
- Include reproducible source, explicit limits of offline checks and a concise installation guide with each review build. Do not call a layout game-tested without an actual game test.
- Record new accepted rules and new regression checks in the notes. Do not turn a successful schema check into a claim of visual correctness.
