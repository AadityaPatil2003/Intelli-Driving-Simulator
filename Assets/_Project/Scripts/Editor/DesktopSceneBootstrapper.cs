#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using IDS.Core;
using IDS.XR;
using IDS.Vehicle;
using IDS.Traffic;
using IDS.Telemetry;
using IDS.AI;
using IDS.Scenarios;
using IDS.UI;
using IDS.Dev;

namespace IDS.EditorTools
{
    /// <summary>
    /// Builds a complete, playable desktop scene in one click.
    ///
    ///     Tools → Intelli-Driving → Build Playable Desktop Scene
    ///
    /// Why this exists: the integration blocker is not missing code, it is that
    /// assembling ~30 GameObjects and wiring ~60 inspector references by hand is
    /// hours of work, and every reference you miss becomes a silent null at
    /// runtime. This does the assembly deterministically, so the scene can be
    /// rebuilt from scratch whenever it breaks.
    ///
    /// The result runs end to end with NO headset:
    ///
    ///     keyboard drive → telemetry CSV → pedestrian hazard
    ///        → feature extraction → rule-based risk score → post-drive report
    ///
    /// Controls: W/A/S/D drive · right mouse look · H force hazard
    ///           · E end drive · R restart · F1 print state
    ///
    /// Saved to Assets/_Project/Scenes/Main/Main_Desktop.unity so it never
    /// overwrites an XR scene anyone else is working on.
    /// </summary>
    public static class DesktopSceneBootstrapper
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main/Main_Desktop.unity";
        private const string RoadTag = "RoadSurface";

        [MenuItem("Tools/Intelli-Driving/Build Playable Desktop Scene", false, 20)]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog("Build desktop scene",
                    "This creates a new scene at\n" + ScenePath +
                    "\n\nYour existing scenes are not touched. Continue?",
                    "Build it", "Cancel"))
                return;

            EnsureTag(RoadTag);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildRoad(out LaneReferenceSystem laneRef);
            var vehicleGo = BuildVehicle(out VehicleController vehicle);
            var camera = BuildCamera(vehicleGo.transform);
            BuildInput(vehicle);
            var country = BuildCountryProfiles();
            BuildTelemetry(vehicle, laneRef, camera.transform,
                           out MirrorCheckDetector mirror,
                           out FeatureExtractor features,
                           out TelemetryRecorder recorder);
            BuildAI(features, country, out DriverProfileManager profiles,
                    out AdaptiveScenarioSelector selector);
            var scenarios = BuildScenarios(vehicle, selector, profiles);
            BuildUI(vehicle, country, profiles);
            BuildSession(recorder, scenarios);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("<b>[Bootstrapper] Desktop scene built.</b>\n" +
                      "Press Play. W/A/S/D to drive. The pedestrian hazard fires " +
                      "around 120 m ahead, or press H to force it. Press E to end " +
                      "the drive and see the report. Telemetry is written to:\n" +
                      Application.persistentDataPath + "/Sessions");

            EditorUtility.DisplayDialog("Done",
                "Scene saved to\n" + ScenePath +
                "\n\nPress Play.\n\nW/A/S/D drive · H force hazard · E end drive",
                "OK");
        }

        // ---------------------------------------------------------------- //

        private static void BuildLighting()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;   // frame budget: shadows off from day one
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.52f);
        }

        private static void BuildRoad(out LaneReferenceSystem laneRef)
        {
            var root = new GameObject("Road");

            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "RoadSurface";
            surface.tag = RoadTag;                       // VehicleController ignores this tag
            surface.transform.SetParent(root.transform);
            surface.transform.localScale = new Vector3(12f, 1f, 400f);
            surface.transform.localPosition = new Vector3(0f, -0.5f, 180f);
            Paint(surface, new Color(0.22f, 0.23f, 0.25f));

            for (int z = 0; z < 360; z += 12)
            {
                var dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dash.name = "Dash_" + z;
                dash.transform.SetParent(root.transform);
                dash.transform.localScale = new Vector3(0.18f, 0.02f, 4f);
                dash.transform.localPosition = new Vector3(0f, 0.01f, z + 2f);
                Object.DestroyImmediate(dash.GetComponent<Collider>());
                Paint(dash, new Color(0.9f, 0.9f, 0.88f));
            }

            foreach (float x in new[] { -6f, 6f })
            {
                var kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                kerb.name = x < 0 ? "Kerb_Left" : "Kerb_Right";
                kerb.transform.SetParent(root.transform);
                kerb.transform.localScale = new Vector3(0.4f, 0.5f, 400f);
                kerb.transform.localPosition = new Vector3(x, 0.15f, 180f);
                Paint(kerb, new Color(0.55f, 0.55f, 0.58f));
            }

            // Lane centreline. 20 m spacing is fine on a straight; tighten to
            // 1-2 m through any corner you add, or lane_offset goes noisy on bends
            // and Anushka's lane_offset_rms reads geometry error as driver error.
            var pathGo = new GameObject("LanePath");
            pathGo.transform.SetParent(root.transform);
            for (int z = 0; z <= 360; z += 20)
            {
                var wp = new GameObject("WP_" + z.ToString("D3"));
                wp.transform.SetParent(pathGo.transform);
                wp.transform.localPosition = new Vector3(0f, 0.05f, z);
            }
            pathGo.AddComponent<LanePath>().CollectChildren();

            laneRef = root.AddComponent<LaneReferenceSystem>();
        }

        private static GameObject BuildVehicle(out VehicleController controller)
        {
            var car = new GameObject("Vehicle");
            car.transform.position = new Vector3(0f, 0.75f, 0f);

            var rb = car.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(car.transform);
            body.transform.localScale = new Vector3(1.8f, 0.7f, 4.2f);
            body.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            Paint(body, new Color(0.78f, 0.35f, 0.18f));

            var colliders = new GameObject("Colliders");
            colliders.transform.SetParent(car.transform);
            colliders.transform.localPosition = Vector3.zero;

            var meshes = new GameObject("WheelMeshes");
            meshes.transform.SetParent(car.transform);
            meshes.transform.localPosition = Vector3.zero;

            var fl = MakeWheel(colliders, meshes, "WheelFL", new Vector3(-0.8f, 0f, 1.4f), out var mfl);
            var fr = MakeWheel(colliders, meshes, "WheelFR", new Vector3(0.8f, 0f, 1.4f), out var mfr);
            var rl = MakeWheel(colliders, meshes, "WheelRL", new Vector3(-0.8f, 0f, -1.4f), out var mrl);
            var rr = MakeWheel(colliders, meshes, "WheelRR", new Vector3(0.8f, 0f, -1.4f), out var mrr);

            controller = car.AddComponent<VehicleController>();
            new Wire(controller)
                .Ref("frontLeft", fl).Ref("frontRight", fr)
                .Ref("rearLeft", rl).Ref("rearRight", rr)
                .Ref("meshFrontLeft", mfl).Ref("meshFrontRight", mfr)
                .Ref("meshRearLeft", mrl).Ref("meshRearRight", mrr)
                .Apply();

            return car;
        }

        private static WheelCollider MakeWheel(GameObject colliders, GameObject meshes,
            string name, Vector3 pos, out Transform mesh)
        {
            var wcGo = new GameObject(name);
            wcGo.transform.SetParent(colliders.transform);
            wcGo.transform.localPosition = pos;

            var wc = wcGo.AddComponent<WheelCollider>();
            wc.radius = 0.34f;
            wc.suspensionDistance = 0.22f;
            wc.wheelDampingRate = 0.25f;
            wc.forceAppPointDistance = 0.1f;

            var spring = wc.suspensionSpring;
            spring.spring = 38000f;
            spring.damper = 4800f;
            spring.targetPosition = 0.45f;
            wc.suspensionSpring = spring;

            var fwd = wc.forwardFriction;
            fwd.stiffness = 1.6f;
            wc.forwardFriction = fwd;

            var side = wc.sidewaysFriction;
            side.stiffness = 1.9f;   // too low here is why prototype cars understeer into walls
            wc.sidewaysFriction = side;

            // Mesh pivot is an empty; the cylinder is a child rotated 90 on Z, so
            // GetWorldPose can drive the pivot without fighting the primitive's
            // default Y axis.
            var pivot = new GameObject(name + "_Mesh");
            pivot.transform.SetParent(meshes.transform);
            pivot.transform.localPosition = pos;

            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "Tyre";
            cyl.transform.SetParent(pivot.transform);
            cyl.transform.localPosition = Vector3.zero;
            cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            cyl.transform.localScale = new Vector3(0.68f, 0.11f, 0.68f);
            Object.DestroyImmediate(cyl.GetComponent<Collider>());
            Paint(cyl, new Color(0.13f, 0.13f, 0.14f));

            mesh = pivot.transform;
            return wc;
        }

        private static Camera BuildCamera(Transform target)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.05f;
            go.AddComponent<AudioListener>();

            var chase = go.AddComponent<DesktopChaseCamera>();
            new Wire(chase).Ref("target", target).Apply();

            return cam;
        }

        private static void BuildInput(VehicleController vehicle)
        {
            var go = new GameObject("IDS_Input");

            var keyboard = go.AddComponent<KeyboardInputProvider>();
            var adapter = go.AddComponent<VehicleInputAdapter>();

            new Wire(adapter).List("providerRefs", new List<Object> { keyboard }).Apply();
            new Wire(vehicle).Ref("inputAdapter", adapter).Apply();
        }

        private static CountryProfileManager BuildCountryProfiles()
        {
            var go = new GameObject("IDS_CountryProfiles");
            var mgr = go.AddComponent<CountryProfileManager>();
            go.AddComponent<CountryProfileHolder>();

            var found = new List<Object>();
            CountryProfile australia = null;

            foreach (string guid in AssetDatabase.FindAssets("t:CountryProfile"))
            {
                var p = AssetDatabase.LoadAssetAtPath<CountryProfile>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (p == null) continue;
                found.Add(p);
                if (p.profileId == "australia") australia = p;
            }

            if (found.Count == 0)
            {
                Debug.LogWarning("[Bootstrapper] No CountryProfile assets found. Run " +
                                 "Tools → Intelli-Driving → Create Country Profiles, " +
                                 "then re-run this bootstrapper. The scene still plays, " +
                                 "but scoring falls back to default constants.");
            }

            var w = new Wire(mgr).List("availableProfiles", found);
            if (australia != null) w.Ref("defaultProfile", australia);
            else if (found.Count > 0) w.Ref("defaultProfile", found[0]);
            w.Apply();

            return mgr;
        }

        private static void BuildTelemetry(VehicleController vehicle,
            LaneReferenceSystem laneRef, Transform head,
            out MirrorCheckDetector mirror, out FeatureExtractor features,
            out TelemetryRecorder recorder)
        {
            var go = new GameObject("IDS_Telemetry");

            mirror = go.AddComponent<MirrorCheckDetector>();
            new Wire(mirror).Ref("vehicleRef", vehicle).Ref("headTransform", head).Apply();

            recorder = go.AddComponent<TelemetryRecorder>();
            new Wire(recorder)
                .Ref("vehicleRef", vehicle)
                .Ref("laneRef", laneRef)
                .Ref("headTransform", head)
                .Ref("mirrorDetector", mirror)
                .Apply();

            features = go.AddComponent<FeatureExtractor>();
            new Wire(features)
                .Ref("recorder", recorder)
                .Ref("mirrorDetector", mirror)
                .Apply();
        }

        private static void BuildAI(FeatureExtractor features, CountryProfileManager country,
            out DriverProfileManager profiles, out AdaptiveScenarioSelector selector)
        {
            var go = new GameObject("IDS_AI");

            var rule = go.AddComponent<RuleBasedRiskScorer>();
            new Wire(rule).Ref("profileManager", country).Apply();

            selector = go.AddComponent<AdaptiveScenarioSelector>();

            profiles = go.AddComponent<DriverProfileManager>();
            new Wire(profiles)
                .Ref("countryProfiles", country)
                .Ref("selector", selector)
                .Apply();

            var scoring = go.AddComponent<RiskScoringSystem>();
            new Wire(scoring)
                .Ref("ruleScorer", rule)
                .Ref("featureExtractor", features)
                .Ref("profileManager", profiles)
                .Ref("countryProfiles", country)
                .Bool("preferClassifier", false)   // rule scorer is the demo path
                .Apply();
        }

        private static ScenarioManager BuildScenarios(VehicleController vehicle,
            AdaptiveScenarioSelector selector, DriverProfileManager profiles)
        {
            var root = new GameObject("IDS_Scenarios");
            var manager = root.AddComponent<ScenarioManager>();

            var scenarioGo = new GameObject("PedestrianScenario");
            scenarioGo.transform.SetParent(root.transform);

            var start = new GameObject("Ped_Start");
            start.transform.SetParent(scenarioGo.transform);
            start.transform.position = new Vector3(6.5f, 1f, 120f);

            var end = new GameObject("Ped_End");
            end.transform.SetParent(scenarioGo.transform);
            end.transform.position = new Vector3(-4f, 1f, 120f);

            var ped = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ped.name = "Pedestrian";
            ped.transform.SetParent(scenarioGo.transform);
            ped.transform.position = start.transform.position;
            ped.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            Paint(ped, new Color(0.95f, 0.75f, 0.2f));

            var follower = ped.AddComponent<NPCPathFollower>();
            new Wire(follower)
                .Ref("startPoint", start.transform)
                .Ref("endPoint", end.transform)
                .Apply();

            var scenario = scenarioGo.AddComponent<PedestrianScenario>();
            new Wire(scenario)
                .Str("scenarioId", "pedestrian_step_out")
                .Ref("pedestrian", follower)
                .Ref("stepOutPoint", ped.transform)
                .Ref("vehicleRef", vehicle)
                .Apply();

            new Wire(manager)
                .List("scenarios", new List<Object> { scenario })
                .Ref("selector", selector)
                .Ref("profileManager", profiles)
                .Int("hazardsPerSession", 1)
                .Float("minSecondsBetweenHazards", 0f)
                .Apply();

            return manager;
        }

        private static void BuildUI(VehicleController vehicle,
            CountryProfileManager country, DriverProfileManager profiles)
        {
            var canvasGo = new GameObject("IDS_UI",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Exclude) == null)
            {
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }

            // ---- driving HUD ------------------------------------------------
            var hudRoot = Panel(canvasGo.transform, "HUD",
                new Vector2(0f, 0f), new Vector2(0.28f, 0.22f), new Color(0f, 0f, 0f, 0.55f));

            var speed = Label(hudRoot.transform, "Speed", "0", 88,
                new Vector2(0.05f, 0.35f), new Vector2(0.62f, 0.95f), TextAlignmentOptions.Right);
            var unit = Label(hudRoot.transform, "Unit", "km/h", 26,
                new Vector2(0.64f, 0.38f), new Vector2(0.96f, 0.62f), TextAlignmentOptions.Left);
            var mode = Label(hudRoot.transform, "Mode", "TRAINING DRIVE", 22,
                new Vector2(0.05f, 0.16f), new Vector2(0.96f, 0.34f), TextAlignmentOptions.Left);
            var priorityMirror = Label(hudRoot.transform, "PriorityMirror", "", 20,
                new Vector2(0.05f, 0.02f), new Vector2(0.96f, 0.16f), TextAlignmentOptions.Left);

            var hud = canvasGo.AddComponent<DrivingHUD>();
            new Wire(hud)
                .Ref("hudRoot", hudRoot)
                .Ref("speedText", speed).Ref("speedUnitText", unit)
                .Ref("trainingModeText", mode).Ref("priorityMirrorText", priorityMirror)
                .Ref("vehicleRef", vehicle).Ref("countryProfiles", country)
                .Apply();

            // ---- alerts ------------------------------------------------------
            var alertRoot = Panel(canvasGo.transform, "Alert",
                new Vector2(0.28f, 0.78f), new Vector2(0.72f, 0.92f), new Color(0f, 0f, 0f, 0.7f));
            var alertText = Label(alertRoot.transform, "AlertText", "", 46,
                new Vector2(0f, 0f), new Vector2(1f, 1f), TextAlignmentOptions.Center);

            var alerts = canvasGo.AddComponent<AlertManager>();
            new Wire(alerts).Ref("alertRoot", alertRoot).Ref("alertText", alertText).Apply();

            var triggers = canvasGo.AddComponent<LiveAlertTriggers>();
            new Wire(triggers).Ref("vehicleRef", vehicle).Ref("countryProfiles", country).Apply();

            // ---- post-drive report -------------------------------------------
            var reportRoot = Panel(canvasGo.transform, "Report",
                new Vector2(0.22f, 0.1f), new Vector2(0.78f, 0.9f),
                new Color(0.06f, 0.07f, 0.09f, 0.96f));

            Label(reportRoot.transform, "Title", "SESSION COMPLETE", 34,
                new Vector2(0.06f, 0.9f), new Vector2(0.94f, 0.98f), TextAlignmentOptions.Left);

            var risk = Label(reportRoot.transform, "Risk", "-- / 100", 62,
                new Vector2(0.06f, 0.76f), new Vector2(0.6f, 0.9f), TextAlignmentOptions.Left);
            var band = Label(reportRoot.transform, "Band", "", 28,
                new Vector2(0.6f, 0.78f), new Vector2(0.94f, 0.88f), TextAlignmentOptions.Right);

            var lane = Label(reportRoot.transform, "Lane", "Lane keeping: --", 22,
                new Vector2(0.06f, 0.68f), new Vector2(0.94f, 0.75f), TextAlignmentOptions.Left);
            var steer = Label(reportRoot.transform, "Steering", "Steering smoothness: --", 22,
                new Vector2(0.06f, 0.61f), new Vector2(0.94f, 0.68f), TextAlignmentOptions.Left);
            var mir = Label(reportRoot.transform, "Mirror", "Mirror awareness: --", 22,
                new Vector2(0.06f, 0.54f), new Vector2(0.94f, 0.61f), TextAlignmentOptions.Left);
            var gap = Label(reportRoot.transform, "Gap", "Gap selection: --", 22,
                new Vector2(0.06f, 0.47f), new Vector2(0.94f, 0.54f), TextAlignmentOptions.Left);
            var haz = Label(reportRoot.transform, "Hazard", "Hazard response: --", 22,
                new Vector2(0.06f, 0.40f), new Vector2(0.94f, 0.47f), TextAlignmentOptions.Left);

            var weakest = Label(reportRoot.transform, "Weakest", "", 28,
                new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.38f), TextAlignmentOptions.Left);
            var recommend = Label(reportRoot.transform, "Recommendation", "", 22,
                new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.30f), TextAlignmentOptions.Left);
            var trend = Label(reportRoot.transform, "Trend", "", 20,
                new Vector2(0.06f, 0.16f), new Vector2(0.94f, 0.22f), TextAlignmentOptions.Left);
            var summary = Label(reportRoot.transform, "ScenarioSummary", "", 20,
                new Vector2(0.06f, 0.03f), new Vector2(0.94f, 0.16f), TextAlignmentOptions.TopLeft);

            var report = canvasGo.AddComponent<PostDriveReportPanel>();
            new Wire(report)
                .Ref("panelRoot", reportRoot)
                .Ref("compositeRiskText", risk).Ref("riskBandText", band)
                .Ref("laneKeepingValue", lane).Ref("steeringValue", steer)
                .Ref("mirrorValue", mir).Ref("gapValue", gap).Ref("hazardValue", haz)
                .Ref("weakestSkillText", weakest).Ref("recommendationText", recommend)
                .Ref("trendText", trend).Ref("scenarioSummaryText", summary)
                .Ref("profileManager", profiles)
                .Apply();

            reportRoot.SetActive(false);
        }

        private static void BuildSession(TelemetryRecorder recorder, ScenarioManager scenarios)
        {
            var go = new GameObject("IDS_Session");
            var session = go.AddComponent<SessionManager>();
            go.AddComponent<DevHotkeys>();

            new Wire(session)
                .Ref("telemetryRecorderRef", recorder)
                .Ref("scenarioManagerRef", scenarios)
                .Float("driveDurationSeconds", 90f)
                .Bool("fastStartForTesting", true)   // skip welcome/country/calibration
                .Apply();
        }

        // ---------------------------------------------------------------- //
        // helpers

        private static GameObject Panel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = colour;
            img.raycastTarget = false;
            return go;
        }

        private static TMP_Text Label(Transform parent, string name, string text,
            float size, Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Paint(GameObject go, Color colour)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return;

            renderer.sharedMaterial = new Material(shader) { color = colour };
        }

        private static void EnsureTag(string tag)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var so = new SerializedObject(assets[0]);
            var tags = so.FindProperty("tags");
            if (tags == null) return;

            for (int i = 0; i < tags.arraySize; i++)
                if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            so.ApplyModifiedProperties();
            Debug.Log("[Bootstrapper] created tag '" + tag + "'");
        }

        /// <summary>
        /// Sets [SerializeField] private fields — the only way to wire these
        /// components from an editor script without making every field public.
        /// A missing field name is reported loudly rather than skipped silently,
        /// because a silent miss becomes a null at runtime and costs an hour.
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
                    Debug.LogError($"[Bootstrapper] {_owner} has no serialized field " +
                                   $"'{field}'. The script changed — update the " +
                                   "bootstrapper rather than wiring it by hand.");
                return p;
            }

            public Wire Ref(string field, Object value)
            {
                var p = Find(field);
                if (p != null) p.objectReferenceValue = value;
                return this;
            }

            public Wire List(string field, List<Object> values)
            {
                var p = Find(field);
                if (p == null) return this;
                p.ClearArray();
                p.arraySize = values.Count;
                for (int i = 0; i < values.Count; i++)
                    p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                return this;
            }

            public Wire Bool(string field, bool value)
            {
                var p = Find(field);
                if (p != null) p.boolValue = value;
                return this;
            }

            public Wire Int(string field, int value)
            {
                var p = Find(field);
                if (p != null) p.intValue = value;
                return this;
            }

            public Wire Float(string field, float value)
            {
                var p = Find(field);
                if (p != null) p.floatValue = value;
                return this;
            }

            public Wire Str(string field, string value)
            {
                var p = Find(field);
                if (p != null) p.stringValue = value;
                return this;
            }

            public void Apply() => _so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
