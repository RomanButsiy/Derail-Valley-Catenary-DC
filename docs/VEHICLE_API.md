# Vehicle adapter boundary (API v1)

Core UMM ID: `DerailValleyCatenaryDC`. Assembly: `CatenaryDC.dll`.
All calls and event subscriptions must occur on Unity's main thread.
This is a new source-level contract, not a claim of binary compatibility with
`electric_sim.catenary.overhead_equipment`.

`CatenaryDC.Api.CatenaryApi` exposes:

- `ApiVersion`, `IsReady`, `LastError`, `LoadedObjectCount`;
- `QueryRelativeContact(vehicle, pantographBase, stripEnd1, stripEnd2, loadCurrent)`;
- `SimulationStep`, `WorldActivated`, `WorldDeactivated` events.

`QueryRelativeContact` returns `(float? contact_height, float contact_voltage)`:
height in vehicle-local coordinates as in the original API, voltage in V.
No contact/not-ready returns `(null, 0)`. A dead wire can have a non-null height
and zero voltage. Invalid transforms/non-finite current raise an exception.

The call also accumulates the supplied current (A) into the contacted source.
It is not a side-effect-free telemetry query: query each active collector once
per physics tick with the appropriate share of current. A diagnostics probe
should use zero current. Do not charge the same vehicle load once per visual
sample or once per pantograph without splitting it.

`SimulationStep` executes before the core commits the accumulated load to
substations. Attach/detach on world entry/exit and on controller disposal.
Do not create another network ticker. Exceptions in one subscriber are logged
and do not prevent other subscribers from being notified.

The core still uses the author's simplified contact-source model, not a graph
solver. Multiple coincident energized wires/raised collectors need separate
geometry/electrical validation. The API does not implement short-circuit safety,
vehicle billing, controller protections, pantograph animation or traction.

Debug network Reload reconstructs the network. A controller can stay subscribed
to SimulationStep across that operation; it must still detach on actual world
unload. The optional bridge follows that distinction.

The optional adapter retains the old namespace only inside its separate assembly
for the copied vehicle implementation. Its UMM ID `Catenary-DC` satisfies the
stock CCL model dependency, but its assembly is `CatenaryDC.Legacy2WE3`, never
`electric_sim`. Running the original engine alongside the core is rejected.
