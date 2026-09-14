#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IDS.Editor
{
    /// <summary>
    /// One-command Quest builds, so a milestone build is reproducible rather than
    /// depending on whatever the Build Settings dialog happened to be set to.
    ///
    /// Menu: Tools → Intelli-Driving → Build Quest APK
    /// CLI:  Unity -quit -batchmode -projectPath . -buildTarget Android \
    ///         -executeMethod IDS.Editor.BuildScript.BuildQuestApk
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDir = "Builds";
        private const string ApkName = "IntelliDrivingSimulator.apk";

        [MenuItem("Tools/Intelli-Driving/Build Quest APK")]
        public static void BuildQuestApk()
        {
            Directory.CreateDirectory(OutputDir);

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] no enabled scenes in Build Settings. " +
                               "Add Main.unity at index 0.");
                return;
            }

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
                Debug.Log($"[Build] sideload with: adb install -r {path}");
            }
            else
            {
                Debug.LogError($"[Build] FAILED: {summary.result}, " +
                               $"{summary.totalErrors} errors");
            }
        }

        /// <summary>
        /// The settings that must be right or the build fails on the device in
        /// confusing ways. Enforced here rather than trusted to be set by hand.
        /// </summary>
        private static void ApplyQuestSettings()
        {
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            Debug.Log("[Build] applied Quest settings: IL2CPP, ARM64, Vulkan, ASTC");
        }

        [MenuItem("Tools/Intelli-Driving/Verify Quest Settings")]
        public static void VerifySettings()
        {
            var problems = new System.Collections.Generic.List<string>();

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                problems.Add("Target architectures should be ARM64 only");

            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length != 1 ||
                apis[0] != UnityEngine.Rendering.GraphicsDeviceType.Vulkan)
                problems.Add("Graphics API should be Vulkan only");

            if (PlayerSettings.GetScriptingBackend(
                    UnityEditor.Build.NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                problems.Add("Scripting backend should be IL2CPP");

            if (problems.Count == 0)
                Debug.Log("[Verify] Quest settings look correct.");
            else
                Debug.LogWarning("[Verify] problems:\n - " + string.Join("\n - ", problems));
        }
    }
}
#endif
