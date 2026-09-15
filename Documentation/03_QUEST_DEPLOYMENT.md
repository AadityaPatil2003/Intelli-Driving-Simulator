# Quest 3 deployment

## One-time device setup

1. Meta Horizon app on your phone → your headset → **Developer Mode: On**
   (needs a verified developer org; create one free at the Meta dev dashboard).
2. Connect the Quest by USB-C. Put the headset on and **Allow USB debugging** —
   tick "Always allow from this computer".
3. Verify from your machine:

```bash
adb devices
# List of devices attached
# 1WMHH8xxxxxxx   device
```

`adb` ships with the Unity Android SDK module, typically at:

```
<Unity>/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
```

Add it to PATH so everyone can run the commands below.

## Build from the Editor

1. `File → Build Settings` → Platform **Android** → Switch Platform
2. Texture Compression: **ASTC**
3. Scene list: `Main` at index 0, test scenes after it
4. Either:
   - **Build And Run** with the headset plugged in (fastest inner loop), or
   - **Build** to `Builds/IDS_<milestone>.apk` for a sideloadable artefact

## Build from the command line (for milestone builds)

```bash
"<UnityPath>/Unity" -quit -batchmode -logFile build.log \
  -projectPath . \
  -buildTarget Android \
  -executeMethod IDS.Editor.BuildScript.BuildQuestApk
```

`Assets/_Project/Scripts/Editor/BuildScript.cs` provides that method and writes
to `Builds/IntelliDrivingSimulator.apk`.

## Sideload

```bash
adb install -r Builds/IntelliDrivingSimulator.apk
# reinstall over an existing build without losing the app data:
adb install -r -d Builds/IntelliDrivingSimulator.apk
```

The app appears in the headset under **Apps → Unknown Sources**.

## Pull recorded telemetry off the device

Sessions are written to `Application.persistentDataPath`, which on Quest is:

```
/sdcard/Android/data/com.rmit.vroom.intellidrivingsimulator/files/
```

```bash
adb pull /sdcard/Android/data/com.rmit.vroom.intellidrivingsimulator/files/Sessions ./LocalSessions
adb pull /sdcard/Android/data/com.rmit.vroom.intellidrivingsimulator/files/Profiles ./LocalProfiles
```

Anushka's classifier trains on `./LocalSessions`. **Do not commit these files** —
they are participant data.

## Live debugging on device

```bash
adb logcat -s Unity:V CRASH:V DEBUG:V
# clear first if the log is noisy
adb logcat -c
```

## Performance check (required before every milestone)

In-headset: press the Meta button → **Performance** overlay, or use the Meta
Quest Developer Hub metrics view.

- Minimum acceptable: **72 FPS** sustained during normal driving (≈13.9 ms/frame)
- Desired: 72 FPS held through a hazard scenario
- Do not chase 90/120 FPS until every mandatory feature works

If you are below 72: check draw calls first, then realtime lights, then
transparency, then post-processing. Not shader complexity.

## Recording demo footage

```bash
# in-headset capture, then:
adb pull /sdcard/Oculus/VideoShots ./DemoFootage
```

Record with passthrough visible — the real hands on the real wheel is the whole
point of the project and it has to be in the 30-second video.

## Known failure modes

| Symptom | Cause | Fix |
|---|---|---|
| Black screen, app runs | two camera rigs, or camera clear flags wrong | one XR Origin; camera background solid black, alpha 0 |
| Passthrough not showing | ARSession/ARCameraManager missing, or Meta Quest passthrough feature not enabled in OpenXR settings | see `02_UNITY_SETUP.md` §5 |
| App installs but instantly closes | ARMv7 in target architectures, or Vulkan not selected | ARM64 only, Vulkan only |
| Hands not tracked | hand tracking off in headset settings | Settings → Movement tracking → Hand tracking |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | signed with a different keystore | `adb uninstall com.rmit.vroom.intellidrivingsimulator` then install |
| Editor Play mode has no controls | XR Device Simulator not in scene | add the simulator prefab to the test scene |
