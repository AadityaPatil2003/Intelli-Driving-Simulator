# Intelli-Driving-Simulator

**Driver adaptation training for a new road system. Meta Quest 3.**

A VR training system for experienced drivers who relocate between countries with
different road systems. It does not teach people to drive. It identifies which of
an experienced driver's automatic habits fail to transfer: looking right when the
danger comes from the left, drifting toward the familiar lane, trusting the wrong
mirror, misjudging a gap sized for traffic from the other direction. It measures
them from telemetry, and coaches them.

Choosing a country is a data change, not a second scene. One codebase serves
Australia, India and the USA: the cabin, the traffic side, the priority mirror,
the junction conflict point, the signage and the scoring baseline all follow the
profile.

| | |
|---|---|
| **Course** | COSC3140 Mixed Reality · RMIT University · Semester 2, 2026 |
| **Team** | VrOoOm: Aaditya Patil · Ananya Rohatgi · Anushka Kamble · Omkar Rakesh Singh |
| **Target device** | Meta Quest 3 (standalone Android build) |
| **Engine** | Unity 6000.6.0f1 · URP 17.6 · OpenXR 1.18 · IL2CPP / ARM64 / Vulkan |
| **Milestone** | 3, final |
| **Deployed** | 12 October 2026 · `Builds/IntelliDrivingSimulator.apk` |

---

## Quick start

Everything below assumes a clean clone and nothing else.

### 1. Clone

```bash
git clone https://github.com/AadityaPatil2003/Intelli-Driving-Simulator.git
cd Intelli-Driving-Simulator
```

### 2. Open in Unity 6000.6.0f1

The exact version is pinned in `ProjectSettings/ProjectVersion.txt`. Use Unity
Hub to install that version **with the Android Build Support module, including
OpenJDK and the Android SDK & NDK Tools**. The project will not build for the
headset without them.

First open takes several minutes: the package manager resolves, then Unity
imports and compiles. Wait for the spinner in the bottom-right to stop.

**Expected result:** the Console shows warnings but **no errors**. If there are
compile errors, stop. Nothing below will work.

### 3. Generate the scene

```
Tools → Intelli-Driving → Build Main Scene
```

Confirm the dialog. The builder generates `Assets/_Project/Scenes/Main/Main.unity`
from scratch (XR rig, cabin, road, traffic, hazards, telemetry, seven UI panels
with their buttons wired), then validates its own output and prints a report.

**Expected result:** the report ends with `All checks passed.` If it ends with a
numbered list of problems instead, that list is the thing to read; each entry
names what is wrong and what it would cause.

The scene is generated rather than committed on purpose. A Unity scene is a
merge-conflict magnet, and a generated one makes a broken scene a one-click
rebuild instead of a day of repair.

### 4. Save, then build the APK

```
File → Save Project
Tools → Intelli-Driving → Build Quest APK
```

**Expected result:** `[Build] OK → Builds/IntelliDrivingSimulator.apk`

The build applies the Quest settings itself: ARM64, Vulkan only, IL2CPP,
managed stripping Low, ASTC textures, MSAA 4×, frame pacing off, APK not AAB.
`Tools → Intelli-Driving → Verify Quest Settings` is a read-only check; run it
*after* a build if you want confirmation.

### 5. Install on the headset

```bash
adb install -r Builds/IntelliDrivingSimulator.apk
```

**Expected result:** `Success`. Launch it from *Unknown Sources* in the Quest
library.

### One manual setting

**Project Settings → XR Plug-in Management → OpenXR → Android → Interaction
Profiles**: tick **Meta Quest Touch Plus Controller Profile**, keeping **Oculus
Touch Controller Profile** ticked as well.

Quest 3 ships Touch Plus controllers. If the runtime negotiates Touch Plus and
Unity has no layout registered for it, no device carries a `LeftHand` /
`RightHand` usage and every control reads zero, with nothing on screen to
explain why. This setting lives in an asset rather than a script, so the build
script cannot apply it for you.

---

## Controls

| Action | Control |
|---|---|
| Accelerate | **Right index trigger** (progressive) |
| Brake | **Left index trigger** (progressive) |
| Hold the steering wheel | **Grip button**, hand near the rim |
| Steer | Turn your hand while gripping. 140° of hand rotation is full lock |
| Steer without holding the wheel | **Left thumbstick**, left/right |
| Recentre your seat | **A** or **X**, works while driving |
| End the drive early | **Hold B or Y for 1 second** |
| Press a menu button | Point and pull either trigger |
| Press a menu button with no controller | Look at it for 1.3 s and the reticle fills |

Grip holds the wheel; triggers drive it. The two are kept separate deliberately.
When the grab accepted a trigger as well, holding the rim asserted throttle and
brake at the same time.

**Editor-only hotkeys:** `H` force-fires the armed hazard · `E` ends the drive ·
`R` restarts · `F1` prints session state.

---

## What a session does

```
Welcome → Country selection → Recentre → Ready → Drive (3 min) ⇄ Hazard
        → Analysing → Report → Complete
```

The car is only drawn when you are meant to be in it. The menu phases hide the
bodywork and float the panel in the open; the recentre and ready phases bring the
cabin back. This is not cosmetic. The dashboard cuts the driver's view at a fixed
−4.94°, so a full-size menu does not fit inside the cabin at any distance.

Outside the driving phases the car is held on a parking brake and the NPCs hold
still, so nothing moves while a participant is reading.

### Hazards

Two of three fire per session, chosen by the adaptive selector from the driver's
weakest skill. Each one briefs its procedure before it happens, cues the step as
it happens, and gives a verdict immediately afterwards.

| Hazard | Probes | Procedure taught |
|---|---|---|
| Pedestrian step-out | Hazard response | Ease off on the approach, scan both footpaths, brake, do not swerve |
| Cyclist blind spot | Mirror awareness | Kerb-side mirror within 3 s *before* moving out, 1.5 m clearance |
| Lead-vehicle braking | Hazard response | Two-second gap, watch brake lights, brake in a straight line |

### What is measured

| Skill | From |
|---|---|
| Lane keeping | RMS lateral offset from the lane centreline |
| Steering smoothness | RMS rate of change of steering input |
| Mirror awareness | Head-orientation dwell per mirror zone, 0.18 s, with cooldown |
| Gap selection | Time-to-collision of the nearest conflicting vehicle at junction entry |
| Hazard response | Trigger instant to first brake input |

Composite risk 0-100, weighted lane 30 · steering 20 · mirror 20 · gap 15 ·
response 15. **These weights are design parameters, not validated road-safety
values.** Mirror checking is a head-orientation proxy, since Quest 3 has no eye
tracking, and is reported as such.

Telemetry is written at 20 Hz to
`Application.persistentDataPath/Sessions/session_<timestamp>.csv`, with a
matching `_events.csv`. Pull it off the device with:

```bash
adb pull /sdcard/Android/data/com.VrOoOm.IntelliDrivingSimulator/files/Sessions
```

---

## Training mode and assessment mode

`DriveCoach` on `_Services/Coach` has a `coachingEnabled` flag, **on by default**.

- **On**. Every hazard briefs, cues and gives a verdict. This is the training
  experience.
- **Off**. Silent. Use this for a measured baseline run, and for the first run
  of any user study: if the simulator tells a participant to check the left
  mirror, you are measuring your own prompting rather than the habit they
  arrived with.

The intended study design is two runs per participant, a silent baseline then a
coached run, with the improvement between them as the evidence.

---

## Repository layout

```
Assets/_Project/
├── Data/CountryProfiles/     Australia.asset · India.asset · USA.asset
├── Scenes/Main/              Main.unity, generated not hand-authored
└── Scripts/                  69 files, 13,397 lines
    ├── AI/                   Risk scoring, driver profile, adaptive selector
    ├── Core/                 SessionManager, events, service registry, shared types
    ├── Dev/                  Desktop-only hotkeys and chase camera
    ├── Editor/               Scene builder, build script, profile creator
    ├── Scenarios/            HazardScenarioBase + three hazards + queue
    ├── Telemetry/            Recorder, feature extractor, mirror and gap detectors
    ├── Traffic/              NPC vehicle and path followers
    ├── UI/                   Session UI, HUD, alerts, coach, report panel
    ├── Vehicle/              Vehicle controller, mirrors, cabin and environment config
    └── XR/                   Head and controller anchors, steering wheel, pointer, recentre
```

### Branches

| Branch | What it holds |
|---|---|
| `main` | Current VR build |
| `archive/pre-vr-rebuild` | The complete **mixed-reality** implementation, passthrough working, as it stood on 6 October before the VR rebuild |
| `feat/*` | Per-feature branches from the M1-M2 period |

The archive branch is kept deliberately. The project was pitched as passthrough
mixed reality and shipped as VR; that branch is the evidence that the MR path was
built and worked, not abandoned on paper.

---

## Team and contribution

The table records what each member owned and which parts of the repository carry
their work. Work was divided by subsystem rather than by volume of commits: some
subsystems landed as a few large commits and others as many small ones, so
ownership of code and artefacts is the accurate record of who did what. Two
members committed under two identities each (a local Git config and a GitHub web
identity), so the history lists six author names for four people.

| Member | Role | Owned |
|---|---|---|
| **Aaditya Patil** | Project manager · XR integration | XR runtime, device builds, scene generation, build pipeline, system integration, submission |
| **Ananya Rohatgi** | Vehicle & environment | WheelCollider vehicle, road and district, lane structure, intersection, traffic, cabin and environment country flip |
| **Omkar Rakesh Singh** | Scenarios, UI & evaluation | Three hazard scenarios, session UI, live alerts, post-drive report panel, user-testing protocol, demo media |
| **Anushka Kamble** | AI & driver behaviour · documentation | Telemetry schema and recorder, feature extraction, rule-based scoring, driver profile, country rule data, milestone report, README |

---

## Known limitations

These are stated rather than discovered.

- **This is VR, not mixed reality.** Passthrough was implemented and verified on
  device on 25 September and removed during the October rebuild. The reasons are
  in the report; the working MR implementation is on `archive/pre-vr-rebuild`.
- **Mirror checking is inferred from head orientation**, not eye gaze.
- **The scoring weights are our design choice**, tuned on internal sessions, not
  derived from road-safety research.
- **The classifier was scoped out.** `ClassifierRiskScorer` exists and reports
  itself unusable without a trained model, so the rule-based scorer runs instead.
  No training pipeline was written. This was a planned P1 cut, not a failure.
- **No formal participant evaluation has been completed.** Internal testing drove
  the defect audit; the 4-6 participant protocol is specified and not yet run.
- **The October telemetry set is invalid and was discarded.** An auto-drive
  fallback held the accelerator at a constant 0.35 and wrote it to CSV as though
  a participant had produced it. Every session recorded before 6 October carries
  that artefact. The fallback has been removed. The car now does not move when
  no input is available, which is visible immediately.
- **Frame rate has not been profiled.** The 72 FPS target is a target, not a
  measurement.

---

## Licence and attribution

Coursework for COSC3140 at RMIT University. All source in `Assets/_Project/` is
the team's own work. The environment is built from Unity primitives, so there are
no third-party art assets to attribute.
