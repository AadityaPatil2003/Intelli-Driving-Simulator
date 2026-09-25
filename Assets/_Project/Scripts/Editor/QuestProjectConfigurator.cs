#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
// XRGeneralSettings and XRManagerSettings are runtime types; the per-build-target
// container and the loader metadata store are editor-only, in separate namespaces.
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

namespace IDS.EditorTools
{
    /// <summary>
    /// One-click project configuration for a Meta Quest 3 build.
    ///
    ///     Tools → Intelli-Driving → Configure Project For Quest
    ///
    /// Why this exists: the project currently cannot produce a working APK, and
    /// none of the reasons are visible in the Console. Four settings are wrong or
    /// missing, and each one fails in a way that looks like a different problem:
    ///
    ///   1. Build Settings has ZERO scenes registered  → the APK launches empty
    ///   2. No Android entry in XR Plug-in Management  → APK is a flat 2D app
    ///   3. applicationIdentifier is unset             → adb collisions on install
    ///   4. Scripting backend is unset (not IL2CPP)    → ARM64 build fails
    ///
    /// Diagnosing those four from inside a headset costs a lab session. This does
    /// them deterministically and prints what it changed.
    ///
    /// WHAT THIS CANNOT DO: the OpenXR feature checkboxes (Meta Quest feature
    /// group, passthrough, hand tracking) live in per-package assets whose type
    /// names change between package versions. This script enumerates whatever
    /// features it finds and enables the ones it recognises, then prints the full
    /// list so you can verify the rest by hand. Trust the printed list, not this
    /// comment.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public static class QuestProjectConfigurator
    {
        private const string PackageName = "com.rmit.vroom.intellidrivingsimulator";
        private const string CompanyName = "RMIT VrOoOm";
        private const string ProductName = "Intelli-Driving-Simulator";

        private const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string SimulationLoaderType = "UnityEngine.XR.Simulation.SimulationLoader";

        [MenuItem("Tools/Intelli-Driving/Configure Project For Quest", false, 1)]
        public static void ConfigureAll()
        {
            if (!EditorUtility.DisplayDialog("Configure project for Quest",
                    "This changes Player Settings, XR Plug-in Management and the " +
                    "Build Settings scene list.\n\n" +
                    "It does not touch any scene or script. Continue?",
                    "Configure", "Cancel"))
                return;

            var log = new List<string>();

            ConfigurePlayerSettings(log);
            ConfigureXRLoaders(log);
            ConfigureBuildScenes(log);
            ReportOpenXRFeatures(log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("<b>[QuestConfig] Configuration complete.</b>\n  " +
                      string.Join("\n  ", log));

            EditorUtility.DisplayDialog("Done",
                "Configuration applied. Read the Console for the full list, " +
                "including the OpenXR features you still need to verify by hand.",
                "OK");
        }

        // ------------------------------------------------------------------ //

        private static void ConfigurePlayerSettings(List<string> log)
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            log.Add($"company/product → {CompanyName} / {ProductName}");

            var android = NamedBuildTarget.Android;

            PlayerSettings.SetApplicationIdentifier(android, PackageName);
            log.Add($"package name → {PackageName}");

            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            log.Add("scripting backend → IL2CPP");

            // ARM64 only. Including ARMv7 produces an APK that installs and then
            // closes immediately on a Quest, with no useful error.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            log.Add("target architectures → ARM64 only");

            // Vulkan only. Leaving OpenGLES3 in the list means the headset may
            // pick it, and Meta passthrough does not render under it.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan });
            log.Add("graphics API → Vulkan only");

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            log.Add("minimum API level → 32");

            // 2 = "Both". The keyboard provider uses the legacy Input class and the
            // XR rig uses the new Input System, so both handlers must stay enabled.
            PlayerSettings.SetPropertyInt("activeInputHandler", 2, BuildTargetGroup.Android);
            PlayerSettings.SetPropertyInt("activeInputHandler", 2, BuildTargetGroup.Standalone);
            log.Add("active input handling → Both");

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            log.Add("texture compression → ASTC");
        }

        private static void ConfigureXRLoaders(List<string> log)
        {
            if (!EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                log.Add("!! XRGeneralSettingsPerBuildTarget not found. Open " +
                        "Project Settings → XR Plug-in Management once to create it, " +
                        "then re-run this.");
                return;
            }

            // ---- Android: OpenXR on -------------------------------------------
            var androidManager = GetOrCreateManager(perTarget, BuildTargetGroup.Android,
                                                    "Android", log);
            if (androidManager != null)
            {
                if (XRPackageMetadataStore.AssignLoader(
                        androidManager, OpenXRLoaderType, BuildTargetGroup.Android))
                    log.Add("Android XR → OpenXR loader assigned");
                else
                    log.Add("!! Android XR → could not assign OpenXR loader. Tick it " +
                            "manually: Project Settings → XR Plug-in Management → Android.");
            }

            // ---- Standalone: both loaders off ---------------------------------
            // Two loaders enabled at once is what produces
            // "ScriptableSingleton XRSimulationRuntimeSettings already exists":
            // OpenXR and XR Simulation both initialise on Play and the Simulation
            // provider registers its settings singleton a second time.
            //
            // Desktop development in this project uses the keyboard path, which
            // needs no XR runtime at all, so the correct setting is neither.
            var standaloneSettings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            var standaloneManager = standaloneSettings != null
                ? standaloneSettings.AssignedSettings : null;

            if (standaloneManager != null)
            {
                XRPackageMetadataStore.RemoveLoader(
                    standaloneManager, OpenXRLoaderType, BuildTargetGroup.Standalone);
                XRPackageMetadataStore.RemoveLoader(
                    standaloneManager, SimulationLoaderType, BuildTargetGroup.Standalone);
                log.Add("Standalone XR → both loaders removed (fixes the duplicate " +
                        "ScriptableSingleton error)");
            }
            else
            {
                log.Add("Standalone XR → no manager found, nothing to remove");
            }

            EditorUtility.SetDirty(perTarget);
        }

        private static XRManagerSettings GetOrCreateManager(
            XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group,
            string label, List<string> log)
        {
            var settings = perTarget.SettingsForBuildTarget(group);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = $"{label} Settings";
                perTarget.SetSettingsForBuildTarget(group, settings);

                string path = AssetDatabase.GetAssetPath(perTarget);
                if (!string.IsNullOrEmpty(path))
                    AssetDatabase.AddObjectToAsset(settings, path);

                log.Add($"{label} XR → settings object created");
            }

            if (settings.Manager == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = $"{label} Providers";
                settings.Manager = manager;

                string path = AssetDatabase.GetAssetPath(perTarget);
                if (!string.IsNullOrEmpty(path))
                    AssetDatabase.AddObjectToAsset(manager, path);

                log.Add($"{label} XR → provider manager created");
            }

            return settings.Manager;
        }

        private static void ConfigureBuildScenes(List<string> log)
        {
            // Preferred build order: the XR gate scene is index 0 until passthrough
            // is verified, because that is the scene we are actually testing today.
            string[] preferred =
            {
                "Assets/_Project/Scenes/Main/Main.unity",
                "Assets/_Project/Scenes/Main/XR_Test.unity",
                "Assets/_Project/Scenes/Main/Main_Desktop.unity",
            };

            var found = new List<EditorBuildSettingsScene>();

            foreach (string path in preferred)
                if (File.Exists(path))
                    found.Add(new EditorBuildSettingsScene(path, true));

            // Anything else under _Project/Scenes, disabled, so it is available
            // without affecting the build index of the scenes above.
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (found.Any(s => s.path == path)) continue;
                found.Add(new EditorBuildSettingsScene(path, false));
            }

            if (found.Count == 0)
            {
                log.Add("!! no scenes found under Assets/_Project/Scenes — build list " +
                        "left empty. Generate a scene first, then re-run this.");
                return;
            }

            EditorBuildSettings.scenes = found.ToArray();
            log.Add($"build scenes → {found.Count} registered, index 0 = " +
                    Path.GetFileName(found[0].path));
        }

        /// <summary>
        /// OpenXR features are per-package assets whose concrete types vary by
        /// package version, so rather than guessing type names this enumerates
        /// what is actually installed, enables anything whose name clearly matches
        /// what this project needs, and prints the rest for manual verification.
        /// </summary>
        private static void ReportOpenXRFeatures(List<string> log)
        {
            var settingsType = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
                .FirstOrDefault(t => t.FullName == "UnityEngine.XR.OpenXR.OpenXRSettings");

            if (settingsType == null)
            {
                log.Add("!! OpenXR package types not found — is the OpenXR Plugin installed?");
                return;
            }

            var getSettings = settingsType.GetMethod("GetSettingsForBuildTargetGroup",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (getSettings == null)
            {
                log.Add("!! OpenXR settings accessor not found — verify features by hand.");
                return;
            }

            object androidSettings;
            try
            {
                androidSettings = getSettings.Invoke(null, new object[] { BuildTargetGroup.Android });
            }
            catch (System.Exception e)
            {
                log.Add($"!! could not read OpenXR Android settings: {e.Message}");
                return;
            }

            if (androidSettings == null)
            {
                log.Add("!! no OpenXR settings for Android yet. Tick OpenXR under " +
                        "Project Settings → XR Plug-in Management → Android, then re-run.");
                return;
            }

            var featuresProp = settingsType.GetMethod("GetFeatures", new System.Type[0]);
            if (featuresProp == null)
            {
                log.Add("!! OpenXR feature list not readable — verify by hand.");
                return;
            }

            var features = featuresProp.Invoke(androidSettings, null) as System.Array;
            if (features == null || features.Length == 0)
            {
                log.Add("!! no OpenXR features found for Android.");
                return;
            }

            string[] wanted = { "passthrough", "hand tracking", "meta quest", "oculus touch" };
            var enabled = new List<string>();
            var available = new List<string>();

            foreach (object feature in features)
            {
                if (feature == null) continue;

                var type = feature.GetType();
                var nameField = type.GetProperty("name") ?? type.GetProperty("nameUi");
                string name = nameField?.GetValue(feature) as string ?? type.Name;

                var enabledProp = type.GetProperty("enabled");
                bool isEnabled = enabledProp != null && (bool)enabledProp.GetValue(feature);

                available.Add($"{(isEnabled ? "[x]" : "[ ]")} {name}");

                if (enabledProp == null || !enabledProp.CanWrite) continue;

                string lower = name.ToLowerInvariant();
                if (wanted.Any(w => lower.Contains(w)) && !isEnabled)
                {
                    enabledProp.SetValue(feature, true);
                    EditorUtility.SetDirty(feature as Object);
                    enabled.Add(name);
                }
            }

            if (enabled.Count > 0)
                log.Add("OpenXR features enabled → " + string.Join(", ", enabled));

            log.Add("OpenXR Android features present (VERIFY THESE BY HAND):\n    " +
                    string.Join("\n    ", available));
        }

        // ------------------------------------------------------------------ //

        [MenuItem("Tools/Intelli-Driving/Verify Quest Configuration", false, 2)]
        public static void Verify()
        {
            var problems = new List<string>();
            var android = NamedBuildTarget.Android;

            if (EditorBuildSettings.scenes.Length == 0)
                problems.Add("Build Settings has no scenes — the APK will launch empty.");
            else if (!EditorBuildSettings.scenes[0].enabled)
                problems.Add("Build Settings scene at index 0 is disabled.");

            if (PlayerSettings.GetScriptingBackend(android) != ScriptingImplementation.IL2CPP)
                problems.Add("Scripting backend is not IL2CPP.");

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                problems.Add("Target architectures should be ARM64 only.");

            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length != 1 || apis[0] != GraphicsDeviceType.Vulkan)
                problems.Add("Graphics API should be Vulkan only.");

            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel32)
                problems.Add("Minimum API level should be 32 or higher.");

            string id = PlayerSettings.GetApplicationIdentifier(android);
            if (string.IsNullOrEmpty(id) || id.Contains("DefaultCompany"))
                problems.Add($"Package name is still '{id}'.");

            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget) && perTarget != null)
            {
                var androidSettings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
                var loaders = androidSettings?.Manager?.activeLoaders;

                if (loaders == null || loaders.Count == 0)
                    problems.Add("No XR loader is enabled for Android — the APK will " +
                                 "run as a flat 2D app with no passthrough.");

                var standalone = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
                if (standalone?.Manager?.activeLoaders != null &&
                    standalone.Manager.activeLoaders.Count > 1)
                    problems.Add("More than one XR loader is enabled for Standalone — " +
                                 "this causes the duplicate ScriptableSingleton error.");
            }

            if (problems.Count == 0)
                Debug.Log("<b>[QuestConfig] Verification passed.</b> " +
                          "Project is configured for a Quest build.");
            else
                Debug.LogWarning("<b>[QuestConfig] Problems found:</b>\n  - " +
                                 string.Join("\n  - ", problems));
        }
    }
}
#endif