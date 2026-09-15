# Telemetry schema (v1)

Frozen at Milestone 2. Adding a column mid-semester invalidates every session
recorded before it, because the classifier trains on column order. If a channel
must be added, add it at the **end** and bump `SchemaVersion` in
`TelemetrySample.cs`.

Sample rate: **20 Hz**. Physics runs at 50 Hz; 20 Hz is enough to resolve a
braking reaction and cheap enough not to cost frames on the device.

## `session_<timestamp>.csv`

| Column | Unit | Source | Notes |
|---|---|---|---|
| `t` | s | recorder | seconds since session start |
| `session_id` | — | SessionManager | `session_YYYYMMDD_HHMMSS` |
| `country_profile` | — | CountryProfileManager | `australia` / `india` / `usa` |
| `scenario_id` | — | ScenarioManager | empty when no hazard is active |
| `pos_x`, `pos_y`, `pos_z` | m | IVehicleState | world position |
| `speed_kph` | km/h | IVehicleState | signed; negative in reverse |
| `steering_input` | −1..1 | IVehicleState | negative = left |
| `brake_input` | 0..1 | IVehicleState | |
| `accel_input` | 0..1 | IVehicleState | |
| `lane_offset` | m | ILaneReference | **positive = right of centreline** |
| `head_yaw` | ° | XR camera | relative to vehicle forward; negative = left |
| `head_pitch` | ° | XR camera | negative = looking up |
| `hazard_active` | 0/1 | ScenarioManager | |
| `collision` | 0/1 | IVehicleState | true on the sample a collision registers |
| `mirror_check` | 0/1 | MirrorCheckDetector | one-shot on a registered check |

## `session_<timestamp>_events.csv`

| Column | Notes |
|---|---|
| `t` | seconds since session start |
| `event` | stable token |
| `detail` | pipe-separated key=value; **no commas** |

Event tokens currently emitted:

```
recording_started      schema_v1
session_start          <session id>
session_end            <session id>
hazard_triggered       <scenario id>
hazard_resolved        <id>|react=<s>|collision=<bool>
collision              <impact kph>
gap_accepted           <seconds>
cyclist_manoeuvre      check=<bool>|side=<Left|Right>|t=<s>
cyclist_result         min_dist=<m>|check=<bool>
lead_brake_triggered   gap=<m>|headway=<s>|speed=<kph>
lead_brake_result      headway=<s>|min_dist=<m>|react=<s>|collision=<bool>
alert_shown            <priority>|<message>
```

## Derived features

Computed at session end by `FeatureExtractor`. The Python trainer must use
identical formulas — see the note in `train_classifier.py`.

| Feature | Formula | Unit | Direction |
|---|---|---|---|
| `lane_offset_rms` | `sqrt(mean(lane_offset²))` | m | higher → more risk |
| `steering_jerk` | RMS of d²(steering)/dt², ÷ reference (40) | 0..1 | higher → more risk |
| `mirror_check_freq` | checks ÷ minutes driven | /min | higher → **less** risk |
| `gap_acceptance` | minimum accepted gap at the conflict point | s | higher → **less** risk |
| `reaction_time` | mean over hazards of (first brake − trigger) | s | higher → more risk |

`gap_acceptance` and `reaction_time` are **−1 when not measured** (no
intersection crossed, no hazard occurred). The scorer treats −1 as neutral and
excludes that skill from the weakest-skill calculation. Do not coerce it to 0 —
that would score the driver as maximally aggressive for a situation they never
encountered.

## Privacy

Session files contain driving behaviour tied to an anonymous participant id.

- Participant ids only: `P01`, `P02`… never names
- `Sessions/` and `Profiles/` are in `.gitignore` — do not commit them
- Explain to participants what is recorded before they drive
- Obtain whatever consent or approval the course requires before testing
