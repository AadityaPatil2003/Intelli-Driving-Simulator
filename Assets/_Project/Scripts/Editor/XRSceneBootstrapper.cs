#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using IDS.XR;
using IDS.Dev;

namespace IDS.EditorTools
{
    /// <summary>
    /// Generates the XR passthrough gate scene.
    ///
    ///     Tools → Intelli-Driving → Build XR Gate Scene
    ///
    /// This is deliberately the smallest scene that can answer the question the
    /// project has not yet answered: does passthrough work on the device?
    ///
    /// It contains an XR Origin, an AR Session, the passthrough controller, a
    /// fixed reference cube and an in-headset diagnostic panel. Nothing else.
    /// Debugging passthrough inside a thirty-component scene means every failure
    /// has thirty possible causes; here it has four, and the panel tells you
    /// which one.
    ///
    /// ACCEPTANCE (this is the A3 gate from the project documentation):
    ///   - the real room is visible through the headset
    ///   - head movement tracks correctly
    ///   - the cube stays fixed in space as you move around it
    ///
    /// Nothing else proceeds to the device until this passes.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public static class XRSceneBootstrapper
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main/XR_Test.unity";

        [MenuItem("Tools/Intelli-Driving/Build XR Gate Scene", false, 21)]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog("Build XR gate scene",
                    "Creates a minimal passthrough test scene at\n" + ScenePath +
                    "\n\nYour other scenes are not touched. Continue?",
                    "Build it", "Cancel"))
                return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            var arSession = BuildARSession();
            Camera camera = BuildXROrigin(out GameObject originRoot);

            if (camera == null)
            {
                EditorUtility.DisplayDialog("Failed",
                    "Could not create or find an XR Origin. Import the XR " +
                    "Interaction Toolkit Starter Assets sample and try again.", "OK");
                return;
            }

            var passthrough = ConfigureCameraForPassthrough(camera, arSession);
            BuildReferenceCube();
            BuildDiagnosticPanel(camera.transform, passthrough);
            BuildBootstrap();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("<b>[XRGate] Scene built at " + ScenePath + "</b>\n" +
                      "Next: Tools → Intelli-Driving → Configure Project For Quest, " +
                      "then build and install the APK.\n" +
                      "Acceptance: real room visible, head tracking correct, " +
                      "cube stays fixed in space.");

            EditorUtility.DisplayDialog("Done",
                "XR gate scene saved.\n\n" +
                "1. Run 'Configure Project For Quest'\n" +
                "2. Build the APK\n" +
                "3. adb install -r\n\n" +
                "The diagnostic panel floats in front of you and reports what is " +
                "actually running.", "OK");
        }

        // ------------------------------------------------------------------ //

        private static void BuildLighting()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.55f);
        }

        private static ARSession BuildARSession()
        {
            // ARInputManager is deliberately absent: it was removed in AR
            // Foundation 6, and adding it is a compile error rather than a
            // warning. ARSession alone is correct for 6.6.x.
            var go = new GameObject("AR Session");
            return go.AddComponent<ARSession>();
        }

        /// <summary>
        /// Prefers the XR Origin prefab from the XR Interaction Toolkit Starter
        /// Assets sample, because that rig is tested and carries the correct
        /// TrackedPoseDriver bindings. Falls back to constructing one only if the
        /// sample has not been imported.
        /// </summary>
        private static Camera BuildXROrigin(out GameObject originRoot)
        {
            originRoot = null;

            string prefabPath = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p =>
                    p.Contains("Starter Assets") &&
                    Path.GetFileNameWithoutExtension(p).StartsWith("XR Origin"));

            if (!string.IsNullOrEmpty(prefabPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    originRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    originRoot.transform.position = Vector3.zero;

                    var cam = originRoot.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        Debug.Log($"[XRGate] using Starter Assets rig: {prefabPath}");
                        return cam;
                    }
                }
            }

            Debug.LogWarning("[XRGate] XR Origin prefab not found — constructing a " +
                             "rig manually. Importing the XR Interaction Toolkit " +
                             "Starter Assets sample is the more reliable path.");
            return ConstructXROrigin(out originRoot);
        }

        private static Camera ConstructXROrigin(out GameObject originRoot)
        {
            originRoot = new GameObject("XR Origin (XR Rig)");
            var origin = originRoot.AddComponent<XROrigin>();

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originRoot.transform, false);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(offset.transform, false);

            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
            camGo.AddComponent<AudioListener>();

            var driver = camGo.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            driver.positionInput = new UnityEngine.InputSystem.InputActionProperty(
                new UnityEngine.InputSystem.InputAction(
                    "Position", UnityEngine.InputSystem.InputActionType.Value,
                    "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new UnityEngine.InputSystem.InputActionProperty(
                new UnityEngine.InputSystem.InputAction(
                    "Rotation", UnityEngine.InputSystem.InputActionType.Value,
                    "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));

            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            return cam;
        }

        /// <summary>
        /// The camera settings that decide whether passthrough is visible at all.
        /// Solid black with alpha ZERO. Opaque black is the single most common
        /// cause of "the app runs but the screen is black".
        /// </summary>
        private static PassthroughController ConfigureCameraForPassthrough(
            Camera camera, ARSession session)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);

            if (camera.GetComponent<AudioListener>() == null &&
                Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
                camera.gameObject.AddComponent<AudioListener>();

            var manager = camera.GetComponent<ARCameraManager>()
                          ?? camera.gameObject.AddComponent<ARCameraManager>();

            if (camera.GetComponent<ARCameraBackground>() == null)
                camera.gameObject.AddComponent<ARCameraBackground>();

            var controller = camera.GetComponent<PassthroughController>()
                             ?? camera.gameObject.AddComponent<PassthroughController>();

            new Wire(controller)
                .Ref("cameraManager", manager)
                .Ref("arSession", session)
                .Bool("enablePassthroughOnStart", true)
                .Apply();

            return controller;
        }

        /// <summary>
        /// A cube at a fixed world position. If it drifts as you walk around it,
        /// tracking is wrong; if it stays put, the rig is correct. This is the
        /// actual acceptance test, and it needs no instrumentation to read.
        /// </summary>
        private static void BuildReferenceCube()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Reference Cube";
            cube.transform.position = new Vector3(0f, 1.0f, 1.2f);
            cube.transform.localScale = Vector3.one * 0.25f;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
                cube.GetComponent<Renderer>().sharedMaterial =
                    new Material(shader) { color = new Color(0.94f, 0.64f, 0.01f) };

            // A second cube on the floor gives a depth cue: if the two appear at
            // the same distance, stereo rendering is not working.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor Marker";
            floor.transform.position = new Vector3(0.6f, 0.02f, 1.6f);
            floor.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);
            if (shader != null)
                floor.GetComponent<Renderer>().sharedMaterial =
                    new Material(shader) { color = new Color(0.3f, 0.72f, 0.51f) };
        }

        private static void BuildDiagnosticPanel(Transform head, PassthroughController passthrough)
        {
            var canvasGo = new GameObject("Diagnostic Panel",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(600f, 460f);
            rt.localScale = Vector3.one * 0.0012f;

            // Parented to the head so it is always readable — this is a debug
            // panel, not production UI. The driving HUD is deliberately NOT
            // head-locked; see DrivingHUD.
            canvasGo.transform.SetParent(head, false);
            canvasGo.transform.localPosition = new Vector3(0f, -0.05f, 0.8f);

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.82f);

            var textGo = new GameObject("Output", typeof(RectTransform));
            textGo.transform.SetParent(canvasGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(24f, 24f);
            textRt.offsetMax = new Vector2(-24f, -24f);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "starting…";
            tmp.fontSize = 22f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.richText = true;

            var hud = canvasGo.AddComponent<XRDiagnosticHUD>();
            new Wire(hud)
                .Ref("output", tmp)
                .Ref("passthroughControllerRef", passthrough)
                .Apply();
        }

        private static void BuildBootstrap()
        {
            var go = new GameObject("IDS_XRBootstrap");
            var bootstrap = go.AddComponent<XRBootstrap>();

            new Wire(bootstrap)
                .Bool("initialiseOnStart", true)
                .Int("targetFrameRate", 72)
                .Apply();
        }

        // ------------------------------------------------------------------ //

        /// <summary>
        /// Sets [SerializeField] private fields from an editor script, reporting
        /// any field name that no longer exists rather than skipping it silently.
        /// </summary>
        private class Wire
        {
            private readonly SerializedObject _so;
            private readonly string _owner;

            public Wire(Object target)
            {
                _so = new SerializedObject(target);
                _owner = target.GetType().Name;
            }

            private SerializedProperty Find(string field)
            {
                var p = _so.FindProperty(field);
                if (p == null)
                    Debug.LogError($"[XRGate] {_owner} has no serialized field " +
                                   $"'{field}'. The script changed — update this " +
                                   "generator rather than wiring it by hand.");
                return p;
            }

            public Wire Ref(string f, Object v)
            { var p = Find(f); if (p != null) p.objectReferenceValue = v; return this; }

            public Wire Bool(string f, bool v)
            { var p = Find(f); if (p != null) p.boolValue = v; return this; }

            public Wire Int(string f, int v)
            { var p = Find(f); if (p != null) p.intValue = v; return this; }

            public void Apply() => _so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
