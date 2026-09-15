# Start here

## 1. Clone

```bash
git clone <repo-url> IntelliDrivingSimulator
cd IntelliDrivingSimulator
git lfs install
```

## 2. Open in the agreed Unity version

Check `ProjectSettings/ProjectVersion.txt` after Aaditya's first commit and
install **exactly** that Editor version via Unity Hub. Do not upgrade the Editor
mid-semester. If you think an upgrade is needed, raise it at the Tuesday
stand-up — it is a team decision, not an individual one.

Required Unity Hub modules: **Android Build Support**, **Android SDK & NDK
Tools**, **OpenJDK**.

## 3. Work in your own test scene

| Member | Test scene | Owns |
|---|---|---|
| Aaditya | `XR_Test.unity` | `Main.unity` (integration scene) |
| Ananya | `Vehicle_Test.unity`, `Traffic_Test.unity` | vehicle + environment prefabs |
| Anushka | `Telemetry_Test.unity` | telemetry / AI scripts + ScriptableObjects |
| Omkar | `Scenario_Test.unity`, `UI_Test.unity` | scenario + UI prefabs |

**Only Aaditya edits `Main.unity`.** Unity scenes merge badly. Everything you
build goes into a prefab or a script; Aaditya drops the prefab into `Main`.

## 4. Branch, work, PR

```bash
git checkout main && git pull
git checkout -b feat/<your-thing>
# ... work ...
git add -A && git commit -m "Add lane centreline system"
git push -u origin feat/<your-thing>
# open a PR, one teammate reviews, then merge
```

Full rules: `01_GIT_WORKFLOW.md`.

## 5. Commit your own work

Every member commits under their own GitHub account. The marker reads the commit
history as the record of individual contribution — do not let one person push
everyone's work. If you pair on something, use a co-author trailer:

```
Co-authored-by: Name <email@example.com>
```

## 6. Definition of done

A task is done when **all** of these are true:

- [ ] compiles with zero new Console errors
- [ ] works in your dedicated test scene
- [ ] produces the outputs named in your brief's acceptance test
- [ ] edge cases tried (no input, restart, replay, other country profile)
- [ ] tested on the Quest if it is XR-dependent
- [ ] committed on a feature branch and pushed
- [ ] PR reviewed by one teammate
- [ ] still works after Aaditya integrates it into `Main.unity`
