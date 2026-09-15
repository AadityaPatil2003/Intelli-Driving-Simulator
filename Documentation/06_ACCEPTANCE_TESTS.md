# Acceptance tests

Run these literally. "It looked like it worked" is how a build that fails in the
lab gets committed.

## Week 5 — foundation sprint

| # | Test | Owner | Pass |
|---|---|---|---|
| 1 | Quest shows passthrough + one virtual cube, head tracking correct | Aaditya | ☐ |
| 2 | Car drives, steers, brakes on a plane in desktop Play mode | Ananya | ☐ |
| 3 | Telemetry schema committed, `CountryProfile` class exists | Anushka | ☐ |
| 4 | `HazardScenarioBase` compiles, UI wireframes committed | Omkar | ☐ |

Gate: **all four**. If passthrough is not working, nothing else proceeds.

## Week 6 — core systems

| # | Test | Pass |
|---|---|---|
| 1 | Desktop: drive one minute, `session_*.csv` written with increasing `t` | ☐ |
| 2 | `lane_offset` matches a tape-measured offset within ~0.2 m | ☐ |
| 3 | Pedestrian scenario triggers once, moves, resolves, resets, replays | ☐ |
| 4 | Quest: passthrough + both hands tracked, poses follow palms | ☐ |
| 5 | Calibration gizmo lands on the real wheel | ☐ |

## Week 7 — first vertical slice (the most important checkpoint)

One continuous run, no editor intervention:

```
launch → profile → calibrate → drive → pedestrian hazard
       → telemetry written → risk score computed → report shown
```

| # | Test | Pass |
|---|---|---|
| 1 | Whole loop completes without a Console error | ☐ |
| 2 | Report values change between a careful drive and a careless one | ☐ |
| 3 | Nothing on the report is hard-coded (check by driving badly on purpose) | ☐ |
| 4 | Session can be restarted and run again without a scene reload | ☐ |

## Week 8 — Milestone 2

The M2 acceptance test is: **a lecturer puts on the headset and completes a
session unaided.**

| # | Test | Pass |
|---|---|---|
| 1 | APK installs from `adb install -r` on a lab headset | ☐ |
| 2 | App launches; passthrough visible | ☐ |
| 3 | Country profile selectable | ☐ |
| 4 | Calibration completes (or falls back gracefully with a clear message) | ☐ |
| 5 | Vehicle drivable via the chosen input path | ☐ |
| 6 | One drivable route, car stays on it | ☐ |
| 7 | Telemetry file created on device, pullable via adb | ☐ |
| 8 | One hazard triggers and its outcome is recorded | ☐ |
| 9 | Rule-based risk score generated | ☐ |
| 10 | Basic dashboard appears with real numbers | ☐ |
| 11 | 72 FPS sustained during normal driving | ☐ |

## Week 10 — feature freeze

| # | Test | Pass |
|---|---|---|
| 1 | Both cabin configurations toggle from the profile screen | ☐ |
| 2 | All three hazards trigger reliably, five runs each | ☐ |
| 3 | Adaptive selector picks a hazard matching the recorded weakest skill | ☐ |
| 4 | Driver profile persists across an app restart | ☐ |
| 5 | Alerts fire, are readable, and do not spam | ☐ |
| 6 | Full dashboard shows all five sub-scores | ☐ |

After this: bugs, performance, usability, testing, classifier, polish. **No new
features.**

## Scenario test matrix — every hazard, every time

| Condition | Expected |
|---|---|
| Driver reacts correctly | success recorded, no collision |
| Driver does not react | failure recorded, not a crash |
| Collision | collision recorded, session continues |
| Driver reacts very early | response still logged, no double-trigger |
| Driver stops before the trigger | scenario stays stable, does not fire at 0 km/h |
| Restart mid-hazard | scenario resets cleanly |
| Replay | works identically the second time |
| Different country profile | no exception, kerb side correct |

## Quest device checks — every build

- [ ] launches without a crash
- [ ] passthrough visible
- [ ] head and hand tracking correct
- [ ] calibration works
- [ ] steering responds
- [ ] all UI text readable at its intended distance
- [ ] no discomfort in a three-minute session
- [ ] 72 FPS sustained, including during a hazard
- [ ] no severe errors in `adb logcat -s Unity:V`
