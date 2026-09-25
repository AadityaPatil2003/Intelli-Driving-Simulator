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

        // XRSettingsKey is deprecated in XR Plug-in Management
        // 4.7, but the underlying EditorBuildSettings config key is unchanged.
        private const string XRSettingsKey = "com.unity.xr.management.loader_settings";

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

            ConfigureActiveInputHandling(log);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            log.Add("texture compression → ASTC");
        }

        /// <summary>
        /// Active Input Handling is a single PROJECT-WIDE setting, not a
        /// per-platform one. Calling PlayerSettings.SetPropertyInt with a
        /// BuildTargetGroup produces "Unknown Property: 'Android::activeInputHandler'"
        /// because no per-target variant exists. It has to be written through the
        /// serialized ProjectSettings object.
        ///
        /// It must be "Both": KeyboardInputProvider uses the legacy Input class,
        /// while the XR rig's TrackedPoseDriver uses the new Input System.
        /// </summary>
        private static void ConfigureActiveInputHandling(List<string> log)
        {
            const int both = 2;   // 0 = Input Manager, 1 = Input System, 2 = Both

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                log.Add("!! could not open ProjectSettings.asset — set Active Input " +
                        "Handling to 'Both' by hand in Player settings.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");

            if (prop == null)
            {
                log.Add("!! activeInputHandler not found — set Active Input Handling " +
                        "to 'Both' by hand in Player settings.");
                return;
            }

            if (prop.intValue == both)
            {
                log.Add("active input handling → already Both");
                return;
            }

            int before = prop.intValue;
            prop.intValue = both;
            so.ApplyModifiedProperties();

            log.Add($"active input handling → Both (was {before}) — " +
                    "RESTART UNITY for this to take effect");
        }

        private static void ConfigureXRLoaders(List<string> log)
        {
            if (!EditorBuildSettings.TryGetConfigObject(
                    XRSettingsKey,
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
                ? standaloneSettings.Manager : null;

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
        /// Reports which OpenXR features are enabled for Android, and turns on the
        /// ones this project needs where it can.
        ///
        /// Uses reflection because the OpenXR feature types are per-package and
        /// change names between package versions. Two things this has to be careful
        /// about, both of which broke earlier versions of this script:
        ///
        ///   - OpenXRSettings declares BOTH GetFeatures() and GetFeatures&lt;T&gt;(),
        ///     so Type.GetMethod("GetFeatures", Type[]) throws
        ///     AmbiguousMatchException. The method has to be picked explicitly.
        ///   - AppDomain.GetAssemblies() can return unloaded assemblies in Unity;
        ///     Type.GetType with an assembly-qualified name avoids the scan.
        ///
        /// This is informational only, so the whole thing is wrapped: a failure
        /// here must never abort the configuration that ran before it.
        /// </summary>
        private static void ReportOpenXRFeatures(List<string> log)
        {
            try
            {
                ReportOpenXRFeaturesUnsafe(log);
            }
            catch (System.Exception e)
            {
                log.Add($"!! could not read the OpenXR feature list ({e.GetType().Name}). " +
                        "Everything above still applied. Verify features by hand under " +
                        "XR Plug-in Management → OpenXR → Android.");
            }
        }

        private static void ReportOpenXRFeaturesUnsafe(List<string> log)
        {
            var settingsType =
                System.Type.GetType("UnityEngine.XR.OpenXR.OpenXRSettings, Unity.XR.OpenXR")
                ?? System.Type.GetType("UnityEngine.XR.OpenXR.OpenXRSettings");

            if (settingsType == null)
            {
                log.Add("!! OpenXR types not found — is the OpenXR Plugin installed?");
                return;
            }

            var getSettings = settingsType.GetMethod("GetSettingsForBuildTargetGroup",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (getSettings == null)
            {
                log.Add("!! OpenXR settings accessor not found — verify features by hand.");
                return;
            }

            object androidSettings = getSettings.Invoke(null, new object[] { BuildTargetGroup.Android });

            if (androidSettings == null)
            {
                log.Add("!! no OpenXR settings for Android. Tick OpenXR under " +
                        "XR Plug-in Management → Android, then re-run.");
                return;
            }

            // Pick the non-generic, parameterless GetFeatures() explicitly.
            var getFeatures = settingsType
                .GetMethods(System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetFeatures"
                                     && !m.IsGenericMethod
                                     && m.GetParameters().Length == 0);

            if (getFeatures == null)
            {
                log.Add("!! OpenXR feature list not readable — verify features by hand.");
                return;
            }

            var features = getFeatures.Invoke(androidSettings, null) as System.Array;

            if (features == null || features.Length == 0)
            {
                log.Add("!! no OpenXR features found for Android.");
                return;
            }

            string[] wanted = { "passthrough", "hand tracking subsystem", "meta quest support" };
            var turnedOn = new List<string>();
            var listing = new List<string>();

            foreach (object feature in features)
            {
                if (feature == null) continue;

                var type = feature.GetType();
                var enabledProp = type.GetProperty("enabled");
                string name = (feature as Object)?.name ?? type.Name;

                bool isEnabled = enabledProp != null &&
                                 enabledProp.CanRead &&
                                 (bool)enabledProp.GetValue(feature);

                listing.Add($"{(isEnabled ? "[x]" : "[ ]")} {name}");

                if (isEnabled || enabledProp == null || !enabledProp.CanWrite) continue;

                string lower = name.ToLowerInvariant();
                if (!wanted.Any(w => lower.Contains(w))) continue;

                enabledProp.SetValue(feature, true);
                if (feature is Object unityObject) EditorUtility.SetDirty(unityObject);
                turnedOn.Add(name);
            }

            if (turnedOn.Count > 0)
                log.Add("OpenXR features enabled → " + string.Join(", ", turnedOn));

            log.Add("OpenXR Android features (VERIFY THESE):\n    " +
                    string.Join("\n    ", listing));
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

            if (EditorBuildSettings.TryGetConfigObject(XRSettingsKey,
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