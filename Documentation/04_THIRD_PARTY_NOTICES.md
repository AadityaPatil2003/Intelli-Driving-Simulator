# Third-party components and team decisions

Two purposes: licence compliance, and a record of which engineering decisions
were ours. The second matters for marking — the assessment cares which parts you
designed, so anything reused needs to be visible as reused.

## Rule

External code solves generic engineering problems. The project-specific
intelligence stays ours: the telemetry design, the risk model, the country
profiles, the adaptive loop, the scenarios.

We do **not** fork an entire driving simulator.

## Isolation pattern

External components never talk to our systems directly:

```
External vehicle controller
        ↓
VehicleInputAdapter  /  IVehicleState
        ↓
our telemetry → our features → our risk model
```

So an external component can be swapped out later without touching anything else.

## Register every reuse here

```
Component:
Repository:
Original author:
Licence:
Files used:
Files modified:
Purpose:
Changes made by Team VrOoOm:
Team member responsible:
Date added:
```

### Entries

**Unity packages** (OpenXR Plugin, XR Interaction Toolkit, XR Hands, AR
Foundation, Meta OpenXR support, TextMeshPro, Input System) are first-party
Unity/Meta packages installed through Package Manager under the Unity Companion
License and their own terms. They are declared here for completeness; they are
not code we copied.

<!-- Add one block per reused component below, using the template above. -->

## Team decisions log

Record decisions that a reader of the code would otherwise wonder about. Each
one is a sentence of justification you will want when writing the final report.

| Date | Decision | Reason | Owner |
|---|---|---|---|
| Wk 4 | Unity WheelCollider, not a custom physics solver | Custom vehicle dynamics absorbs a semester and produces a car that almost handles correctly. Adequate car + three finished scenarios beats a beautiful car with nothing to drive into. | Ananya |
| Wk 4 | Passthrough MR, not full VR | Makes the project genuinely mixed reality, gives real haptics via the physical prop at no hardware cost, and removes an entire car interior from the build. | Aaditya |
| Wk 4 | Telemetry-based behaviour model, not an LLM | The claim is diagnosing which habits fail to transfer; that diagnosis has to come from measured behaviour, not a conversation about behaviour. | Anushka |
| Wk 4 | Post-drive report feeds the next session | Makes it a trainer rather than a test. | Omkar |
| Wk 4 | One vehicle prefab + one scene, drive side as a data transform | Two vehicles and two maps doubles the work and then doubles the maintenance, and the copies drift apart. | Ananya |
| Wk 4 | Two-stage intelligence: rule scorer then classifier | The rule scorer works at M2 with no data and stays as the fallback, so the adaptive loop functions regardless of classifier accuracy. | Anushka |
| Wk 5 | Wheel steering approach: **TBD after the A6 spike** | Record the outcome and the reason here. | Aaditya |
