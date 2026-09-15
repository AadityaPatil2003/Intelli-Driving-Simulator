# Intelli-Driving-Simulator

**Mixed-reality driver adaptation for a new road.**

A passthrough mixed-reality training system for experienced drivers who relocate
between countries with different road systems. It does not teach people to drive.
It identifies which of an experienced driver's automatic habits fail to transfer —
looking right when the danger comes from the left, drifting toward the familiar
lane, misjudging a gap — and drills them out.

The learner's hands, the steering prop and the desk stay real. Only the road, the
traffic, the mirrors and the signage are virtual.

| | |
|---|---|
| **Course** | COSC3140 Mixed Reality · RMIT University · Semester 2, 2026 |
| **Team** | VrOoOm — Aaditya Patil · Ananya Rohatgi · Anushka Kamble · Omkar Rakesh Singh |
| **Target device** | Meta Quest 3 (standalone Android build) |
| **Engine** | Unity 6000.6.0f1 · URP · OpenXR |
| **Milestone** | 2 — halfway prototype |
| **Status** | Full training loop runs on desktop. Not yet verified on device. |

---

## Quick start

```bash
git clone https://github.com/AadityaPatil2003/Intelli-Driving-Simulator.git
cd Intelli-Driving-Simulator
```

1. Open the project in **Unity 6000.6.0f1** (the exact version is in
   `ProjectSettings/ProjectVersion.txt`).
2. Open `Assets/_Project/Scenes/Main/Main_Desktop.unity`.
3. Press **Play**.

No headset required. That is deliberate — see [Desktop-first development](#desktop-first-development).

### Controls

| Key | Action |
|---|---|
| `W` / `↑` | accelerate |
| `S` / `Space` / `↓` | brake |
| `A` `D` / `←` `→` | steer |
| right mouse (hold) | look around |
| `H` | force-trigger the armed hazard |
| `E` | end the drive and show the post-drive report |
| `R` | restart the session |
| `F1` | print session state to the Console |

### What you should see

Drive forward. Around 120 m a pedestrian steps into the road. Brake or swerve.
Press `E`. The report shows a composite risk score, five per-skill sub-scores,
your weakest skill and a recommended next session — all computed from the
telemetry of the run you just did. Nothing on that screen is hard-coded; drive
badly and it says so.

### Where the data goes

```
<Application.persistentDataPath>/Sessions/session_<timestamp>.csv
<Application.persistentDataPath>/Sessions/session_<timestamp>_events.csv
<Application.persistentDataPath>/Profiles/<driverId>.json
```

On macOS that is `~/Library/Application Support/<company>/IntelliDrivingSimulator/`.
On Quest, pull it with:

```bash
adb pull /sdcard/Android/data/com.rmit.vroom.intellidrivingsimulator/files/Sessions ./LocalSessions
```

Column-by-column schema: [`Documentation/05_TELEMETRY_SCHEMA.md`](Documentation/05_TELEMETRY_SCHEMA.md).

### Rebuilding the scene from scratch

The integration scene is **generated, not hand-assembled**:

```
Tools → Intelli-Driving → Create Country Profiles      (once)
Tools → Intelli-Driving → Build Playable Desktop Scene
```

Wiring ~30 GameObjects and ~60 inspector references by hand is hours of work and
every missed reference is a silent null at runtime. Generating it means the scene
can be rebuilt in one click whenever it breaks, and it is reproducible on every
machine. See `Assets/_Project/Scripts/Editor/DesktopSceneBootstrapper.cs`.

---

## Feature status

Legend: ✅ working in `Main_Desktop` · 🟡 implemented, not yet in the main scene ·
⬜ planned

| Feature | Owner | Status | Landed |
|---|---|---|---|
| Vehicle: steer, accelerate, brake, collide (WheelCollider) | Ananya | ✅ | 14 Sep 2026 |
| Lane centreline reference, signed lateral offset | Ananya | ✅ | 15 Sep 2026 |
| Fixed training route with lane markings and kerbs | Ananya | ✅ | 15 Sep 2026 |
| Input abstraction: keyboard / controller / hand-tracked wheel | Aaditya | ✅ | 14 Sep 2026 |
| Session state machine and cross-stream event bus | Aaditya | ✅ | 14 Sep 2026 |
| Telemetry recording at 20 Hz to CSV | Anushka | ✅ | 14 Sep 2026 |
| Feature extraction: lane RMS, steering jerk, mirror freq, gap | Anushka | ✅ | 14 Sep 2026 |
| Rule-based composite risk scoring | Anushka | ✅ | 15 Sep 2026 |
| Driver profile persistence (JSON) | Anushka | ✅ | 15 Sep 2026 |
| Adaptive scenario selection | Anushka | ✅ | 15 Sep 2026 |
| Country profiles (Australia / India / USA) as data | Anushka | ✅ | 15 Sep 2026 |
| Hazard scenario framework | Omkar | ✅ | 15 Sep 2026 |
| Pedestrian step-out scenario | Omkar | ✅ | 15 Sep 2026 |
| In-drive HUD | Omkar | ✅ | 15 Sep 2026 |
| Post-drive report dashboard | Omkar | ✅ | 15 Sep 2026 |
| Rate-limited live alerts | Omkar | ✅ | 15 Sep 2026 |
| Generated desktop integration scene | Aaditya | ✅ | 15 Sep 2026 |
| Cyclist blind-spot scenario | Omkar | 🟡 | 15 Sep 2026 |
| Sudden lead-vehicle braking scenario | Omkar | 🟡 | 15 Sep 2026 |
| Waypoint traffic and lead-vehicle control | Ananya | 🟡 | 15 Sep 2026 |
| Both cabin configurations from one prefab | Ananya | 🟡 | 15 Sep 2026 |
| Quest 3 passthrough | Aaditya | 🟡 | written, unverified on device |
| Hand tracking and wheel calibration | Aaditya | 🟡 | written, unverified on device |
| Offline-trained logistic classifier | Anushka | 🟡 | experimental |
| User testing, 4–6 participants | Omkar | ⬜ | Week 12 |
| Packaged Quest APK | Aaditya | ⬜ | Week 12 |

🟡 on the XR rows means the code exists and compiles but has not been run on a
headset. We would rather say that than claim it works.

---

## How a session works

| Step | What happens |
|---|---|
| 1 | Headset on, passthrough active — the real room stays visible |
| 2 | Country profile selected — cabin side and rule set chosen from data |
| 3 | Calibration — the virtual wheel bound to the real prop |
| 4 | Guided drive — telemetry logged from the first metre |
| 5 | Hazard injected — chosen adaptively, not in a fixed order |
| 6 | Post-drive report — which habits did not transfer |
| 7 | Next session targets the weakness the report found |

The report is not the end of the session. It is the input to the next one.

---

## The intelligence — no LLM

The hazard you meet next is decided by measured behaviour, not by a conversation
about behaviour.

```
telemetry → feature extraction → risk score → adaptive selection → driver profile
```

| Feature | Formula | Direction |
|---|---|---|
| `lane_offset_rms` | `sqrt(mean(lane_offset²))` | higher → more risk |
| `steering_jerk` | RMS of d²(steering)/dt², normalised | higher → more risk |
| `mirror_check_freq` | checks ÷ minutes driven | higher → **less** risk |
| `gap_acceptance` | minimum accepted gap at the conflict point | higher → **less** risk |
| `reaction_time` | mean of (first brake − hazard trigger) | higher → more risk |

Composite risk weights: lane 30% · steering 20% · mirror 20% · gap 15% ·
hazard response 15%.

Two-stage delivery: a deterministic rule-based scorer ships first and stays in the
build as the guaranteed path; an offline-trained logistic regression is layered on
top as an experiment. If the classifier is weak, the system still works.

### Stated limitations

- Mirror checks are inferred from **head orientation**. Quest 3 has no eye
  tracking, so this is a proxy, not gaze measurement.
- The scoring weights are **team design parameters**, not values derived from
  road-safety research. The risk bands are simulator feedback categories, not
  certification.
- `gap_acceptance` and `reaction_time` are `-1` when the session did not contain
  the situation. The scorer treats that as neutral rather than coercing it to 0.
- The classifier is trained on a small number of self-recorded sessions and is
  presented as a proof of concept.

---

## Novelty: one codebase, both road systems

Right-hand drive and left-hand drive are **not** two vehicles and two maps. The
drive side is a data-driven scene transform: one vehicle prefab, one scene, with
cabin anchors mirrored and traffic direction switched from a `CountryProfile`
ScriptableObject in `Assets/_Project/Data/CountryProfiles/`.

| | Australia (VIC) | India | USA / UAE |
|---|---|---|---|
| Cabin side | Right | Right | Left |
| Traffic side | Left | Left | Right |
| Roundabout | Give way right | Give way right | Give way left |
| Turn on red | Not permitted | Not permitted | Permitted |
| Hook turns / trams | Both (Melbourne) | Neither | Neither |

---

## Technical requirements

| Component | Requirement |
|---|---|
| Unity Editor | **6000.6.0f1** — all developers must match exactly |
| Unity Hub modules | Android Build Support, Android SDK & NDK Tools, OpenJDK |
| Render pipeline | Universal Render Pipeline 17.6.0 |
| XR runtime | OpenXR Plugin 1.18.0 — *not* the deprecated Oculus XR Plugin |
| XR management | XR Plug-in Management 4.7.0 |
| Quest support | Unity OpenXR: Meta 2.6.1 · AR Foundation 6.6.2 |
| Interaction | XR Interaction Toolkit 3.6.0 · XR Hands 1.9.0 |
| UI | TextMeshPro · uGUI 2.6.0 |
| Input | Input System 1.20.0 |
| Graphics API | Vulkan only |
| Scripting backend | IL2CPP, ARM64 only |
| Minimum Android API | 32 |
| Target device | Meta Quest 3, developer mode enabled |
| Classifier training | Python 3.10+, `pandas`, `numpy`, `scikit-learn` |

Full setup walkthrough: [`Documentation/02_UNITY_SETUP.md`](Documentation/02_UNITY_SETUP.md).

### Building for the Quest

```bash
# Editor: Tools → Intelli-Driving → Build Quest APK
# or headless:
Unity -quit -batchmode -projectPath . -buildTarget Android \
      -executeMethod IDS.Editor.BuildScript.BuildQuestApk

adb install -r Builds/IntelliDrivingSimulator.apk
```

Deployment, profiling and known failure modes:
[`Documentation/03_QUEST_DEPLOYMENT.md`](Documentation/03_QUEST_DEPLOYMENT.md).

### Training the experimental classifier

```bash
pip install pandas numpy scikit-learn
python Tools/train_classifier.py --sessions ./LocalSessions --out driver_model.json
adb push driver_model.json /sdcard/Android/data/com.rmit.vroom.intellidrivingsimulator/files/
```

The model can be swapped on the device without rebuilding the APK.

---

## Architecture

```
SessionManager
   ├── CountryProfileManager      which road system are we in
   ├── CalibrationManager         wheel centre and seat reference
   ├── VehicleInputAdapter        wheel | controller | keyboard → steer/accel/brake
   │      └── VehicleController   WheelCollider physics, exposes IVehicleState
   ├── TelemetryRecorder          samples at 20 Hz → CSV
   │      └── FeatureExtractor    the four core features
   ├── RiskScoringSystem          RuleBasedRiskScorer | ClassifierRiskScorer
   ├── DriverProfileManager       JSON profile, weakest skill
   ├── AdaptiveScenarioSelector   picks the next hazard from the profile
   ├── ScenarioManager            Pedestrian | Cyclist | LeadBrake
   ├── AlertManager               rate-limited in-drive alerts
   └── PostDriveReportPanel       the report that feeds the next session
```

The four development streams communicate **only** through the interfaces in
`Assets/_Project/Scripts/Core/Interfaces/`:

| Interface | Implemented by | Exposes |
|---|---|---|
| `IVehicleState` | Ananya | speed, steering, brake, position, collisions |
| `ILaneReference` | Ananya | signed lane offset, lane forward, off-road test |
| `IInputProvider` | Aaditya | steering and pedals from any input source |
| `IHandTrackingProvider` | Aaditya | palm poses and tracking state |
| `ITelemetrySink` | Anushka | discrete event logging from any stream |
| `IRiskScorer` | Anushka | features in, composite risk and sub-scores out |
| `IScenario` | Omkar | hazard lifecycle: arm, trigger, tick, resolve, reset |

No stream references another stream's concrete class. That is what let four people
build in parallel and still integrate.

### Desktop-first development

The Quest 3 units are shared across the cohort and available only in the lab, so
three of the four streams would be blocked waiting for hardware. `KeyboardInputProvider`
and the generated desktop scene are maintained as a first-class path, not a
throwaway: the keyboard goes through the **same** `VehicleInputAdapter` the
hand-tracked wheel will use, so swapping the input source changes nothing
downstream.

---

## Repository layout

```
Assets/_Project/
├── Scripts/
│   ├── Core/          session state machine, event bus, shared types, interfaces
│   ├── XR/            passthrough, hand tracking, calibration, input providers
│   ├── Vehicle/       vehicle controller, lane reference, cabin configuration
│   ├── Traffic/       waypoint NPC vehicles, pedestrian/cyclist movement
│   ├── Telemetry/     recorder, feature extraction, mirror and gap detectors
│   ├── AI/            country profiles, risk scoring, driver profile, adaptation
│   ├── Scenarios/     hazard framework and the three hazards
│   ├── UI/            HUD, alerts, panels, post-drive report
│   ├── Dev/           desktop chase camera and session hotkeys
│   └── Editor/        scene bootstrapper, profile generator, build script
├── Scenes/
│   ├── Main/          Main_Desktop.unity — the integrated scene
│   └── Testing/       per-stream isolated test scenes
└── Data/CountryProfiles/   Australia.asset · India.asset · USA.asset

Documentation/     setup, deployment, git workflow, telemetry schema,
                   acceptance tests, user-testing protocol
Tools/             offline classifier training
```

---

## Team and ownership

| Member | Stream | Owns |
|---|---|---|
| Aaditya Patil | Project Manager & XR Integration | Unity baseline, OpenXR, passthrough, hand tracking, calibration, input, integration scene, APK |
| Ananya Rohatgi | Vehicle & Environment | Vehicle physics, road, lane system, intersections, traffic, cabin configuration |
| Anushka Kamble | AI & Driver Behaviour | Telemetry, feature extraction, risk scoring, country profiles, driver profile, adaptive selection, classifier |
| Omkar Rakesh Singh | Scenarios, UI & Evaluation | Hazard framework, three scenarios, alerts, HUD, post-drive report, user testing, demo media |

Each member commits their own work under their own account. Branch-per-feature,
pull request, one teammate reviews, project manager integrates. Conventions:
[`Documentation/01_GIT_WORKFLOW.md`](Documentation/01_GIT_WORKFLOW.md).

---

## Documentation

| File | Contents |
|---|---|
| [`00_START_HERE.md`](Documentation/00_START_HERE.md) | first clone, scene ownership, definition of done |
| [`01_GIT_WORKFLOW.md`](Documentation/01_GIT_WORKFLOW.md) | branches, commits, PRs, merge-conflict rules |
| [`02_UNITY_SETUP.md`](Documentation/02_UNITY_SETUP.md) | packages, XR config, player and quality settings |
| [`03_QUEST_DEPLOYMENT.md`](Documentation/03_QUEST_DEPLOYMENT.md) | device setup, building, sideloading, profiling |
| [`04_THIRD_PARTY_NOTICES.md`](Documentation/04_THIRD_PARTY_NOTICES.md) | reuse register and team decisions log |
| [`05_TELEMETRY_SCHEMA.md`](Documentation/05_TELEMETRY_SCHEMA.md) | CSV schema, event tokens, derived features |
| [`06_ACCEPTANCE_TESTS.md`](Documentation/06_ACCEPTANCE_TESTS.md) | per-week acceptance gates and test matrix |
| [`07_USER_TESTING_PROTOCOL.md`](Documentation/07_USER_TESTING_PROTOCOL.md) | consent, session script, measures |

---

## Scope boundaries

Deliberately **not** built: an open-world city, realistic full-vehicle mechanical
simulation, autonomous traffic AI, multiplayer, cloud accounts, LLM integration,
weather simulation, or anything presented as licensing or road-safety
certification software.

**Feature freeze: Week 10.** After that, only bug fixes, performance, usability,
testing and the classifier experiment.

---

## Third-party components

Registered in [`Documentation/04_THIRD_PARTY_NOTICES.md`](Documentation/04_THIRD_PARTY_NOTICES.md).
The vehicle-physics spike used the MIT-licensed UnityCar controller
(`CarControl.cs`, author DeathwatchGaming), evaluated in `Vehicle_Test.unity`.
The integrated vehicle uses the project's own `VehicleController`, because the
external controller does not implement the `IVehicleState` contract the telemetry
layer depends on.

Unity packages (OpenXR Plugin, XR Interaction Toolkit, XR Hands, AR Foundation,
Meta OpenXR support, TextMeshPro, Input System) are first-party packages installed
through Package Manager under their own licences.

---

## Acknowledgements

Australian Bureau of Statistics, *Overseas Migration 2024–25*, for the migration
figure. VicRoads, *Convert an overseas licence*, for the conversion requirement.

## AI tool use

An AI assistant (Anthropic Claude) was used to help structure the repository
scaffolding and documentation, and to assist with formatting and wording. The
project concept and framing, the decision to support both cabin configurations,
the telemetry measurements and risk-model design, the technology-stack decisions,
the role allocation and the development timeline are the team's own work.
