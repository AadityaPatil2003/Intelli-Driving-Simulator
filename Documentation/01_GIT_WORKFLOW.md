# Git workflow

## Branches

`main` is always buildable. Nobody commits to it directly.

```
feat/unity-foundation        Aaditya
feat/xr-foundation           Aaditya
feat/quest-passthrough       Aaditya
feat/hand-tracking           Aaditya
feat/wheel-calibration       Aaditya
feat/session-manager         Aaditya
spike/car-controller         Ananya
feat/vehicle-controller      Ananya
feat/training-road           Ananya
feat/lane-reference          Ananya
feat/traffic                 Ananya
feat/cabin-config            Ananya
feat/telemetry-schema        Anushka
feat/telemetry-recorder      Anushka
feat/feature-extraction      Anushka
feat/country-profiles        Anushka
feat/risk-scoring            Anushka
feat/driver-profile          Anushka
feat/adaptive-selector       Anushka
feat/classifier              Anushka
feat/scenario-framework      Omkar
feat/pedestrian-scenario     Omkar
feat/basic-ui                Omkar
feat/alerts                  Omkar
feat/cyclist-scenario        Omkar
feat/lead-brake-scenario     Omkar
feat/post-drive-report       Omkar
```

Bug fixes use `fix/<thing>`. Throwaway investigations use `spike/<thing>` and
are allowed to be ugly — say so in the PR.

## Commit messages

Imperative, specific, one logical change:

```
Configure Quest OpenXR project settings
Add passthrough test scene
Implement hand-tracking provider
Add lane centreline reference system
Implement session telemetry recorder
Add rule-based composite risk scoring
Implement pedestrian step-out scenario
```

Not: `changes`, `update`, `test`, `stuff`, `final`, `final2`, `working`,
`asdf`, or one 40-file commit called `week 9`.

## Pull requests

```
feature branch → local test → push → PR → one teammate reviews
→ Aaditya integration-tests against Main → merge
```

PR description template:

```markdown
## What
One paragraph.

## Acceptance test run
- [ ] <the acceptance criterion from my brief>
- [ ] tested in <scene>
- [ ] tested on Quest / not XR-dependent

## Interfaces touched
Anything in Core/Interfaces? If yes, name who else is affected.

## Known gaps
Be honest here.
```

## Scene and prefab conflicts

Unity `.unity` and `.prefab` files are YAML and merge badly. Rules:

1. Only Aaditya edits `Main.unity`.
2. Do not edit a prefab someone else owns — ask them.
3. Never edit the same prefab on two branches at once.
4. Configure the YAML merge tool once (`02_UNITY_SETUP.md`) so that when a
   conflict does happen it is recoverable.

## Never commit

- `Library/`, `Temp/`, `Build/`, `Logs/`, `UserSettings/`
- Recorded session CSVs or driver profiles — that is participant data
- APKs (attach to a GitHub Release instead)
- Final demo video in normal Git history (use LFS or a Release asset)
