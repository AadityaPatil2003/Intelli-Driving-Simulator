# Unity setup (do this once, in this order)

Owner: **Aaditya** does steps 1–6 and commits the result. Everyone else clones
after that and does steps 7–8 only.

## 1. Unity Hub modules

Install with the Editor:
- Android Build Support
- Android SDK & NDK Tools
- OpenJDK

Meta's Quest Unity requirements ask for a Unity 6 release (6000.0.x or later);
6.1 LTS is the safe choice. **Lock the exact version and record it here:**

```
Agreed Editor version: 6.1.x  <-- Aaditya: replace with the exact build
```

## 2. Create the project

Template: **Universal 3D (URP)**. Name: `IntelliDrivingSimulator`.

## 3. Editor settings that must be set before the first commit

`Edit → Project Settings → Version Control → Mode: Visible Meta Files`
`Edit → Project Settings → Editor → Asset Serialization: Force Text`

Without these two, every merge is unrecoverable. Set them first.

## 4. Packages (Window → Package Manager)

| Package | Why |
|---|---|
| XR Plug-in Management | switches XR providers per build target |
| OpenXR Plugin | the XR runtime we committed to |
| Unity OpenXR: Meta (`com.unity.xr.meta-openxr`) | Quest features incl. passthrough |
| AR Foundation | Meta OpenXR passthrough is delivered through AR Foundation's camera |
| XR Interaction Toolkit 3.x | interaction, XR Origin, device simulator |
| XR Hands | hand tracking, vendor-neutral |
| Input System | vehicle + UI input |
| TextMeshPro | all UI text |

Also enable, inside XR Interaction Toolkit's Samples: **XR Device Simulator**.
That is what lets three people develop without a headset.

## 5. XR configuration

`Project Settings → XR Plug-in Management`:
- Android tab → check **OpenXR**
- OpenXR → Android → Feature Groups → enable **Meta Quest**
- OpenXR → Android → add features: **Meta Quest: Passthrough**, **Hand
  Tracking Subsystem**, **Meta Hand Tracking Aim** (optional)
- Interaction Profiles → add **Oculus Touch Controller Profile** (needed for the
  controller fallback)

`Project Settings → Player → Android`:
- Scripting Backend: **IL2CPP**
- Target Architectures: **ARM64** only (untick ARMv7)
- Graphics APIs: **Vulkan** only
- Minimum API Level: **32** or higher
- Active Input Handling: **Both**
- Package Name: `com.rmit.vroom.intellidrivingsimulator`

`Project Settings → Quality` (Android tier):
- URP asset with HDR **off**, post-processing **off**, shadow distance low,
  MSAA 4x, no soft shadows
- `Project Settings → Player → Resolution and Presentation` → default orientation
  Landscape Left

## 6. Critical: exactly one camera rig

There must be **one** `XR Origin (XR Rig)` in the active scene. Do not also add
an `OVRCameraRig`, and do not leave a second `Main Camera` in the scene. Two rigs
is the single most common cause of "passthrough works for me but not on the
headset". Verify this in `XR_Test.unity` before anything else is built.

## 7. YAML merge tool (each member, once)

```bash
# macOS
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '/Applications/Unity/Hub/Editor/<VERSION>/Unity.app/Contents/Tools/UnityYAMLMerge merge -p %O %B %A %A'

# Windows (Git Bash)
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/<VERSION>/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p %O %B %A %A'
```

## 8. Create the country profile assets

`Tools → Intelli-Driving → Create Country Profiles` (menu item provided by
`Editor/CountryProfileCreator.cs`). This writes `Australia.asset`, `India.asset`
and `USA.asset` into `Assets/_Project/Data/CountryProfiles/`. Do this once;
Anushka owns the values afterwards.

## 9. Scenes to create

`Assets/_Project/Scenes/Main/Main.unity`
`Assets/_Project/Scenes/Testing/XR_Test.unity`
`Assets/_Project/Scenes/Testing/Vehicle_Test.unity`
`Assets/_Project/Scenes/Testing/Traffic_Test.unity`
`Assets/_Project/Scenes/Testing/Telemetry_Test.unity`
`Assets/_Project/Scenes/Testing/Scenario_Test.unity`
`Assets/_Project/Scenes/Testing/UI_Test.unity`

Add all of them to `Build Settings`, with `Main` at index 0.
