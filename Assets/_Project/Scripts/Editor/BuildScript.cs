#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace IDS.EditorTools
{
    /// <summary>
    /// One-command Quest builds, so a milestone build is reproducible rather than
    /// depending on whatever the Build Settings dialog happened to be set to when
    /// someone last touched it.
    ///
    ///     Tools → Intelli-Driving → Build Quest APK
    ///
    /// Or from the command line:
    ///
    ///     Unity -quit -batchmode -projectPath . -buildTarget Android \
    ///           -executeMethod IDS.EditorTools.BuildScript.BuildQuestApk
    ///
    /// Every setting applied here is one that, set wrongly, produces a build that
    /// installs and launches and then fails on the device in a way that looks like
    /// a bug in the application.
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDir = "Builds";
        private const string ApkName = "IntelliDrivingSimulator.apk";

        [MenuItem("Tools/Intelli-Driving/Build Quest APK", false, 40)]
        public static void BuildQuestApk()
        {
            Directory.CreateDirectory(OutputDir);

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] no enabled scenes in Build Settings. Run " +
                               "Tools → Intelli-Driving → Build Main Scene first.");
                return;
            }

            Debug.Log($"[Build] scene 0 (the one that launches): {scenes[0]}");

            ApplyQuestSettings();

            string path = Path.Combine(OutputDir, ApkName);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] OK → {path} " +
                          $"({summary.totalSize / (1024f * 1024f):F1} MB, " +
                          $"{summary.totalTime.TotalSeconds:F0}s)");
                Debug.Log($"[Build] sideload with:  adb install -r {path}");
            }
            else
            {
                Debug.LogError($"[Build] FAILED: {summary.result}, " +
                               $"{summary.totalErrors} error(s). Scroll up for the " +
                               "first compile error — everything after it is noise.");
            }
        }

        /// <summary>
        /// The settings that have to be right or the build fails on the device in
        /// confusing ways. Enforced here rather than trusted to stay set by hand.
        /// </summary>
        private static void ApplyQuestSettings()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(android,
                Il2CppCompilerConfiguration.Release);

            // Low, not Medium or High: IL2CPP's managed stripping removes types
            // that are only ever reached by reflection, and Unity's JSON
            // serialisation of the driver profile is exactly that. Stripped, the
            // profile silently loads as empty on device and works in the editor.
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;

            // SetGraphicsAPIs is ignored while "Auto Graphics API" is on. Turning
            // it off first is what makes the Vulkan-only list actually take.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan });

            // The XR compositor paces frames. Android's optimized frame pacing on
            // top of it fights the compositor and shows up as periodic stutter
            // that looks like a performance problem in the application.
            PlayerSettings.Android.optimizedFramePacing = false;

            // Never blit: the XR display subsystem owns the final surface, and an
            // extra full-screen blit per frame is pure cost at headset resolution.
            PlayerSettings.Android.blitType = AndroidBlitType.Never;

            PlayerSettings.SetMobileMTRendering(android, true);
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            // Active Input Handling is not set here: changing it needs a full
            // editor restart, so a build step is the wrong place to do it.
            // Everything in this project reads the Input System directly, so it
            // should be "Input System Package (New)". VerifySettings checks it.

            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.buildAppBundle = false;   // an APK, for sideloading

            // Debug symbol generation is deliberately not set here. Unity 6.6
            // deprecated EditorUserBuildSettings.androidCreateSymbols in favour of
            // a type that lives in the Android editor extension, and symbols
            // affect only an optional side-car zip, never the APK. Not worth a
            // version-specific API reference.

            Debug.Log("[Build] applied: IL2CPP/Release, ARM64, Vulkan only, " +
                      "stripping Low, ASTC, MT rendering, frame pacing off, " +
                      "blit never, APK (not AAB)");
        }

        [MenuItem("Tools/Intelli-Driving/Verify Quest Settings", false, 41)]
        public static void VerifySettings() => Verify(0);

        /// <summary>
        /// Checks every Quest setting and offers to apply the ones a build would
        /// apply anyway.
        ///
        /// It offers rather than just reporting because every setting here except
        /// Active Input Handling is one ApplyQuestSettings() already sets, so a
        /// report that says "wrong" and stops sends you looking for a problem that
        /// a single click resolves. Reporting a fixable fault without offering the
        /// fix is how a check becomes something people stop running.
        ///
        /// `pass` guards the re-check after applying: if a setting somehow does not
        /// take, this stops rather than looping on a dialog.
        /// </summary>
        private static void Verify(int pass)
        {
            var android = NamedBuildTarget.Android;
            var problems = new List<string>();
            var ok = new List<string>();

            // `requirement` reads as a statement of fact when it passes and as a
            // requirement when it fails, so `actual` must say what it IS. A
            // failure that only restates the desired state reads as though the
            // correct setting is the problem.
            void Check(bool good, string requirement, string actual)
            {
                if (good) ok.Add(requirement);
                else problems.Add($"{requirement}\n   currently: {actual}");
            }

            Check(PlayerSettings.GetScriptingBackend(android) == ScriptingImplementation.IL2CPP,
                  "scripting backend must be IL2CPP",
                  $"{PlayerSettings.GetScriptingBackend(android)} — Mono cannot "
                  + "produce an ARM64 Android player");

            Check(PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64,
                  "target architecture must be ARM64 only",
                  $"{PlayerSettings.Android.targetArchitectures} — Quest 3 is ARM64; "
                  + "anything else doubles build time for nothing");

            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            Check(apis.Length == 1 && apis[0] == GraphicsDeviceType.Vulkan,
                  "graphics API must be Vulkan only",
                  $"[{string.Join(", ", apis)}]");

            Check(!PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android),
                  "Auto Graphics API must be off",
                  "on — while it is on, the explicit API list is ignored");

            Check(PlayerSettings.Android.minSdkVersion >= AndroidSdkVersions.AndroidApiLevel32,
                  "minimum API level must be 32 or higher",
                  $"{(int)PlayerSettings.Android.minSdkVersion} — Quest 3 runs "
                  + "Android 12L (API 32)");

            Check(!PlayerSettings.Android.optimizedFramePacing,
                  "optimized frame pacing must be off",
                  "on — it fights the XR compositor and causes periodic stutter");

            Check(PlayerSettings.GetManagedStrippingLevel(android)
                  == ManagedStrippingLevel.Low,
                  "managed stripping must be Low",
                  $"{PlayerSettings.GetManagedStrippingLevel(android)} — higher "
                  + "levels strip the types JsonUtility reaches by reflection, so "
                  + "the driver profile loads empty on device and works in the editor");

            Check(EditorBuildSettings.scenes.Length > 0
                  && EditorBuildSettings.scenes[0].enabled,
                  "there must be an enabled scene at index 0",
                  $"{EditorBuildSettings.scenes.Length} scene(s) in the list — the "
                  + "headset launches whatever is at index 0");

            if (EditorBuildSettings.scenes.Length > 0)
                Check(EditorBuildSettings.scenes[0].path
                          == "Assets/_Project/Scenes/Main/Main.unity",
                      "scene 0 must be Main.unity",
                      EditorBuildSettings.scenes[0].path);

            Check(EditorUserBuildSettings.androidBuildSubtarget
                  == MobileTextureSubtarget.ASTC,
                  "texture compression must be ASTC",
                  $"{EditorUserBuildSettings.androidBuildSubtarget} — anything else "
                  + "is larger and lower quality on this hardware");

            Check(!EditorUserBuildSettings.buildAppBundle,
                  "output must be an APK, not an AAB",
                  "AAB — adb install cannot sideload an .aab");

            // Active Input Handling: 0 = old, 1 = Input System only, 2 = both.
            // There is no public PlayerSettings API for it, so it is read the
            // same way the Inspector does.
            // The one setting this cannot apply: changing it needs a full editor
            // restart, so it is tracked separately from the fixable ones.
            int inputHandler = ActiveInputHandler();
            bool inputHandlingWrong = inputHandler != 1;

            Check(!inputHandlingWrong,
                  "Active Input Handling must be Input System Package (New)",
                  inputHandler == 2
                      ? "Both — which keeps the deprecated Input Manager backend " +
                        "in the player; nothing here uses it"
                      : inputHandler == 0
                          ? "Input Manager (Old) — nothing here uses it"
                          : "could not be read");

            int fixable = problems.Count - (inputHandlingWrong ? 1 : 0);

            var report = new System.Text.StringBuilder("<b>[Verify]</b> Quest settings\n");
            foreach (string line in ok) report.AppendLine($"   ok    {line}");
            foreach (string line in problems) report.AppendLine($"   FAIL  {line}");

            if (problems.Count == 0)
            {
                report.AppendLine("\nAll settings correct. " +
                                  "Tools → Intelli-Driving → Build Quest APK.");
                Debug.Log(report.ToString());
                EditorUtility.DisplayDialog("Quest settings OK",
                    "Everything checks out. The full list is in the Console.\n\n" +
                    "Next: Tools → Intelli-Driving → Build Quest APK.", "OK");
                return;
            }

            report.AppendLine($"\n{fixable} of {problems.Count} can be applied here " +
                              "or by running Build Quest APK.");
            if (inputHandlingWrong)
                report.AppendLine("Active Input Handling is the exception: it lives " +
                                  "in Project Settings → Player → Other Settings and " +
                                  "needs an editor restart, so no build step touches it.");
            Debug.LogWarning(report.ToString());

            // Nothing this can fix — just report it.
            if (fixable == 0 || pass > 0)
            {
                EditorUtility.DisplayDialog(
                    $"{problems.Count} setting(s) to fix",
                    string.Join("\n\n", problems) +
                    (pass > 0 ? "\n\nThese did not change after being applied — " +
                                "something else is overriding them." : ""),
                    "OK");
                return;
            }

            bool apply = EditorUtility.DisplayDialog(
                $"{problems.Count} setting(s) to fix",
                string.Join("\n\n", problems) +
                $"\n\n{fixable} of these are applied automatically by a build." +
                (inputHandlingWrong
                    ? "\n\nActive Input Handling has to be changed by hand in " +
                      "Project Settings → Player → Other Settings, and needs an " +
                      "editor restart."
                    : ""),
                $"Apply the {fixable} now", "Leave them");

            if (!apply) return;

            ApplyQuestSettings();
            AssetDatabase.SaveAssets();
            Verify(pass + 1);
        }

        /// <summary>
        /// Active Input Handling, read the way the Inspector reads it. 0 = Input
        /// Manager (old), 1 = Input System Package (new), 2 = both. Returns -1 if
        /// it cannot be read, which is reported as "not 1" rather than silently
        /// passing.
        /// </summary>
        private static int ActiveInputHandler()
        {
            Object[] settings = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/ProjectSettings.asset");

            if (settings == null || settings.Length == 0) return -1;

            SerializedProperty prop = new SerializedObject(settings[0])
                .FindProperty("activeInputHandler");

            return prop?.intValue ?? -1;
        }
    }
}
#endif
