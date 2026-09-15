# User testing protocol

Owner: **Omkar**. Target: 4–6 participants, Weeks 11–12. Start recruiting in
Week 9.

## Before anyone tests

- [ ] Obtain whatever consent or ethics approval the course requires. Ask the
      lecturer in Week 9, not Week 11.
- [ ] Anonymous ids only: `P01`, `P02`… Never record names.
- [ ] Tell each participant, before they drive, exactly what is recorded:
      vehicle position, speed, steering, braking, lane position, head
      orientation. No camera footage, no audio.
- [ ] Seated, away from obstacles, supervised.
- [ ] State clearly that they can stop at any time for any reason.

## Recruiting

Target users are people who learned to drive under different road conventions.
Recruit within the cohort and among people you know. Note each participant's
prior driving system (as a country, not a name) — it is the most interesting
variable you have, and it makes the results readable.

## Session script (~20 minutes)

1. **Brief** (3 min) — what the system is, what is recorded, that they can stop.
2. **Fit and comfort check** (2 min) — headset fit, passthrough visible, real
   hands visible on the real wheel. Note anything that felt wrong here.
3. **Calibration** (2 min) — note whether it worked first time. Calibration
   failure rate is a finding.
4. **Familiarisation drive** (3 min) — no hazards. Let them get used to steering.
5. **Recorded session** (5–6 min) — hazards armed. **Fixed random seed, recorded
   in your notes**, so the session is reproducible.
6. **Report** (2 min) — let them read it. Ask them to say aloud what they think
   it is telling them, before you explain anything. Whether it is
   self-explanatory is the thing you are testing.
7. **Debrief** (3 min) — the questions below.

## During the drive, note by hand

This is the most valuable data you will collect and it is easy to forget:

- Did they look the wrong way first at the intersection?
- Did they check the kerb-side mirror before moving over for the cyclist?
- Where did they drift within the lane?
- Did they notice each alert? Did they act on it?
- Any sign of discomfort?

Then **compare your notes against the recorded telemetry**. A mismatch between
what you saw and what the system recorded is a more important finding than any
rating scale, because it goes to whether the measurement works at all.

## Quantitative items (1–5, strongly disagree → strongly agree)

1. The system was easy to understand.
2. The controls felt responsive.
3. The hazards were understandable.
4. The scenarios felt believable.
5. The feedback was clear.
6. The feedback felt useful.
7. I understood what I should improve.
8. The mixed-reality setup felt comfortable.

Also record: task completion, scenario outcomes, hazard reaction times, any
discomfort reported (and when in the session).

## Qualitative questions

- What part was most useful?
- What was confusing?
- Which scenario felt least realistic?
- Was the post-drive feedback clear?
- What should be improved before you would use it again?
- Did anything feel different from driving in the country you learned in?

The last one is the one most likely to produce a quotable answer for the report.

## Results table

| ID | Prior driving system | Composite risk | Weakest skill | Collisions | Mean reaction (s) | Mirror check on cyclist | Discomfort | Notes |
|---|---|---|---|---|---|---|---|---|
| P01 | | | | | | | | |
| P02 | | | | | | | | |
| P03 | | | | | | | | |
| P04 | | | | | | | | |

## What to claim, and what not to

**Do not claim:** that the trainer improves real driving. That needs a much
larger longitudinal study, and a marker will spot the overreach instantly.

**Do claim, with n=4–6:** usability of the interface; whether alerts were
noticed and understood; comfort of the passthrough setup; whether the telemetry
reflected observed behaviour; whether participants understood their report.

If a result is weak or a participant could not complete a task, report it. A
limitation you identified yourself reads as competence. The same limitation
found by the marker reads as an oversight.
