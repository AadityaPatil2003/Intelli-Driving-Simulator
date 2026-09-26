#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
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
    /// Builds the full XR passthrough scene in one click.
    ///
    ///     Tools → Intelli-Driving → Build Main XR Scene
    ///
    /// This is the deliverable scene: everything the desktop bootstrapper builds,
    /// plus the XR Origin rig, passthrough, hand tracking, calibration and a
    /// world-space cabin UI, with all three hazard scenarios rather than one.
    ///
    /// Run the XR gate scene first. If passthrough does not work there, it will
    /// not work here either, and this scene has thirty components in which to
    /// lose the cause.
    ///
    /// PREREQUISITES
    ///   - XR gate scene passes on device
    ///   - Country profile assets exist (Tools → Intelli-Driving → Create Country Profiles)
    ///   - XR Interaction Toolkit Starter Assets sample imported
    ///
    /// WHAT YOU GET
    ///   XR Origin seated in the vehicle · passthrough · hand-tracked wheel with
    ///   controller and keyboard fallback · 400 m road with lane reference ·
    ///   pedestrian, cyclist and lead-brake hazards · traffic · telemetry at
    ///   20 Hz · feature extraction · rule-based risk scoring · country profile
    ///   selection · post-drive report.
    ///
    /// Saved to Assets/_Project/Scenes/Main/Main.unity. Rebuild it any time the
    /// scene file gets mangled by a merge — that is the point of this script.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public static class MainSceneBootstrapper
    {
        private const string ScenePath  = "Assets/_Project/Scenes/Main/Main.unity";
        private const string PrefabDir  = "Assets/_Project/Prefabs";
        private const string CardPath   = PrefabDir + "/CountryCard.prefab";
        private const string RoadTag    = "RoadSurface";

        // Road runs from z = 0 to z = 400. Hazards are spaced so a driver at
        // 40 km/h meets roughly one every 10 seconds, which is the pacing the
        // scenario manager's minSecondsBetweenHazards assumes.
        private const float PedestrianZ = 120f;
        private const float CyclistZ    = 200f;
        private const float LeadStartZ  = 40f;
        private const float JunctionZ   = 300f;

        [MenuItem("Tools/Intelli-Driving/Build Main XR Scene", false, 22)]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog("Build main XR scene",
                    "This creates a new scene at\n" + ScenePath +
                    "\n\nYour existing scenes are not touched.\n\n" +
                    "Run the XR gate scene first — if passthrough fails there, " +
                    "it will fail here too and be much harder to diagnose.\n\nContinue?",
                    "Build it", "Cancel"))
                return;

            EnsureTag(RoadTag);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            var arSession = BuildARSession();

            BuildRoad(out LaneReferenceSystem laneRef);
            // The CabinConfigurator wires itself to the profile events, so the
            // reference is not needed here.
            var vehicleGo = BuildVehicle(out VehicleController vehicle,
                                         out Transform driverAnchor,
                                         out _);

            Camera head = BuildXROrigin(driverAnchor, out GameObject originRoot);
            if (head == null)
            {
                EditorUtility.DisplayDialog("Failed",
                    "Could not create or find an XR Origin. Import the XR " +
                    "Interaction Toolkit Starter Assets sample and try again.", "OK");
                return;
            }

            ConfigureCameraForPassthrough(head, arSession);
            var calibration  = BuildXRServices(originRoot, head);
            BuildInput(vehicle, calibration, originRoot);

            var country = BuildCountryProfiles();

            BuildTelemetry(vehicle, laneRef, head.transform,
                           out MirrorCheckDetector mirror,
                           out GapAcceptanceDetector gaps,
                           out FeatureExtractor features,
                           out TelemetryRecorder recorder);

            BuildAI(features, country,
                    out DriverProfileManager profiles,
                    out AdaptiveScenarioSelector selector);

            var traffic  = BuildTraffic(gaps);
            var scenarios = BuildScenarios(vehicle, selector, profiles, mirror, country, traffic);

            BuildUI(vehicleGo.transform, driverAnchor, vehicle, country, profiles,
                    calibration, scenarios);

            BuildSession(recorder, scenarios);
            BuildBootstrap();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("<b>[MainScene] Built at " + ScenePath + "</b>\n" +
                      "Add it to Build Profiles at index 0, build the APK and install.\n" +
                      "Telemetry is written to " + Application.persistentDataPath + "/Sessions");

            EditorUtility.DisplayDialog("Done",
                "Main scene saved to\n" + ScenePath + "\n\n" +
                "1. File → Build Profiles → add this scene at index 0\n" +
                "2. Build and install the APK\n\n" +
                "In headset: Start → pick a country → calibrate → drive.\n" +
                "On desktop, W/A/S/D still drives it for a quick sanity check.",
                "OK");
        }

        // ================================================================== //
        // world

        private static void BuildLighting()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.shadows = LightShadows.None;   // 72 FPS budget: no shadows, ever
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.55f);

            // No skybox. In passthrough the camera clears to transparent, and a
            // skybox assigned here will paint over the real room on some paths.
            RenderSettings.skybox = null;
        }

        private static ARSession BuildARSession()
        {
            // ARInputManager was removed in AR Foundation 6 — ARSession alone is
            // correct for 6.6.x, and adding the old component is a compile error.
            return new GameObject("AR Session").AddComponent<ARSession>();
        }

        private static void BuildRoad(out LaneReferenceSystem laneRef)
        {
            var root = new GameObject("Road");

            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "RoadSurface";
            surface.tag = RoadTag;                  // VehicleController ignores this tag
            surface.transform.SetParent(root.transform);
            surface.transform.localScale = new Vector3(12f, 1f, 400f);
            surface.transform.localPosition = new Vector3(0f, -0.5f, 180f);
            Paint(surface, new Color(0.22f, 0.23f, 0.25f));

            for (int z = 0; z < 380; z += 12)
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

            // Side road at the junction, so the gap-acceptance measurement has
            // somewhere plausible to happen.
            var side = GameObject.CreatePrimitive(PrimitiveType.Cube);
            side.name = "SideRoad";
            side.tag = RoadTag;
            side.transform.SetParent(root.transform);
            side.transform.localScale = new Vector3(60f, 1f, 10f);
            side.transform.localPosition = new Vector3(0f, -0.5f, JunctionZ);
            Paint(side, new Color(0.22f, 0.23f, 0.25f));

            // Lane centreline. 20 m spacing is fine on a straight; tighten to
            // 1–2 m through any corner you add, or lane_offset reads geometry
            // error as driver error and lane_offset_rms goes noisy.
            var pathGo = new GameObject("LanePath");
            pathGo.transform.SetParent(root.transform);
            for (int z = 0; z <= 380; z += 20)
            {
                var wp = new GameObject("WP_" + z.ToString("D3"));
                wp.transform.SetParent(pathGo.transform);
                wp.transform.localPosition = new Vector3(0f, 0.05f, z);
            }
            pathGo.AddComponent<LanePath>().CollectChildren();

            laneRef = root.AddComponent<LaneReferenceSystem>();
        }

        // ================================================================== //
        // vehicle and cabin

        private static GameObject BuildVehicle(out VehicleController controller,
            out Transform driverAnchor, out CabinConfigurator cabin)
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

            var fl = MakeWheel(colliders, meshes, "WheelFL", new Vector3(-0.8f, 0f,  1.4f), out var mfl);
            var fr = MakeWheel(colliders, meshes, "WheelFR", new Vector3( 0.8f, 0f,  1.4f), out var mfr);
            var rl = MakeWheel(colliders, meshes, "WheelRL", new Vector3(-0.8f, 0f, -1.4f), out var mrl);
            var rr = MakeWheel(colliders, meshes, "WheelRR", new Vector3( 0.8f, 0f, -1.4f), out var mrr);

            controller = car.AddComponent<VehicleController>();
            new Wire(controller)
                .Ref("frontLeft", fl).Ref("frontRight", fr)
                .Ref("rearLeft", rl).Ref("rearRight", rr)
                .Ref("meshFrontLeft", mfl).Ref("meshFrontRight", mfr)
                .Ref("meshRearLeft", mrl).Ref("meshRearRight", mrr)
                .Apply();

            cabin = BuildCabin(car.transform, out driverAnchor);
            return car;
        }

        /// <summary>
        /// Cabin anchors MUST be authored left-hand-drive (driver on the left,
        /// negative X). CabinConfigurator mirrors local X when a right-hand-drive
        /// country profile is selected — author them on the right and every
        /// country comes out backwards.
        /// </summary>
        private static CabinConfigurator BuildCabin(Transform car, out Transform driverAnchor)
        {
            var root = new GameObject("Cabin");
            root.transform.SetParent(car, false);

            driverAnchor = Anchor(root.transform, "DriverAnchor",   new Vector3(-0.38f, 0.30f,  0.10f));
            var column   = Anchor(root.transform, "SteeringColumn", new Vector3(-0.38f, 0.25f,  0.62f));
            var pedals   = Anchor(root.transform, "PedalBox",       new Vector3(-0.38f, -0.15f, 0.95f));
            var stalk    = Anchor(root.transform, "IndicatorStalk", new Vector3(-0.22f, 0.25f,  0.55f));
            var mirrorL  = Anchor(root.transform, "MirrorLeft",     new Vector3(-0.95f, 0.35f,  0.85f));
            var mirrorR  = Anchor(root.transform, "MirrorRight",    new Vector3( 0.95f, 0.35f,  0.85f));

            BuildWheelRing(column);

            var highlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            highlight.name = "PriorityMirrorHighlight";
            highlight.transform.SetParent(root.transform, false);
            highlight.transform.localPosition = mirrorL.localPosition;
            highlight.transform.localScale = new Vector3(0.22f, 0.12f, 0.02f);
            Object.DestroyImmediate(highlight.GetComponent<Collider>());
            Paint(highlight, new Color(0.2f, 0.85f, 0.55f));
            highlight.SetActive(false);

            var cabin = root.AddComponent<CabinConfigurator>();
            new Wire(cabin)
                .Ref("driverAnchor", driverAnchor)
                .Ref("steeringColumnAnchor", column)
                .Ref("pedalBoxAnchor", pedals)
                .Ref("indicatorStalkAnchor", stalk)
                .Ref("leftMirror", mirrorL)
                .Ref("rightMirror", mirrorR)
                .Ref("priorityMirrorHighlight", highlight)
                .Apply();

            return cabin;
        }

        /// <summary>
        /// A ring, built from segments — NOT a scaled cylinder. A Unity cylinder
        /// is solid, so a "wheel" made from one is a disc that blocks the entire
        /// windscreen at arm's length. Radius 0.17 m matches
        /// CalibrationManager.assumedWheelRadius, so what the driver sees is
        /// where the calibration expects their hands to be.
        /// </summary>
        private static void BuildWheelRing(Transform column)
        {
            const float radius = 0.17f;
            const int segments = 20;

            var ring = new GameObject("WheelRing");
            ring.transform.SetParent(column, false);
            ring.transform.localRotation = Quaternion.Euler(75f, 0f, 0f);

            var colour = new Color(0.14f, 0.15f, 0.17f);
            float segLength = 2f * Mathf.PI * radius / segments * 1.25f;

            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = "Seg_" + i.ToString("D2");
                seg.transform.SetParent(ring.transform, false);
                seg.transform.localPosition =
                    new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                seg.transform.localRotation =
                    Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                seg.transform.localScale = new Vector3(0.028f, 0.028f, segLength);
                Object.DestroyImmediate(seg.GetComponent<Collider>());
                Paint(seg, colour);
            }

            // Two spokes, so the wheel reads as rotating rather than as a
            // featureless hoop.
            foreach (float spoke in new[] { 0f, 180f })
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Spoke_" + spoke;
                bar.transform.SetParent(ring.transform, false);
                bar.transform.localRotation = Quaternion.Euler(0f, spoke, 0f);
                bar.transform.localScale = new Vector3(radius * 1.9f, 0.018f, 0.03f);
                Object.DestroyImmediate(bar.GetComponent<Collider>());
                Paint(bar, colour);
            }
        }

        private static Transform Anchor(Transform parent, string name, Vector3 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            return go.transform;
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

            var pivot = new GameObject(name + "_Mesh");
            pivot.transform.SetParent(meshes.transform);
            pivot.transform.localPosition = pos;

            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "Tyre";
            cyl.transform.SetParent(pivot.transform);
            cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            cyl.transform.localScale = new Vector3(0.68f, 0.11f, 0.68f);
            Object.DestroyImmediate(cyl.GetComponent<Collider>());
            Paint(cyl, new Color(0.13f, 0.13f, 0.14f));

            mesh = pivot.transform;
            return wc;
        }

        // ================================================================== //
        // XR rig

        /// <summary>
        /// The rig is parented to the driver anchor so the user rides in the car.
        /// An unparented XR Origin leaves the driver stationary while the vehicle
        /// drives away, which looks like broken tracking but is not.
        /// </summary>
        private static Camera BuildXROrigin(Transform driverAnchor, out GameObject originRoot)
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
                    originRoot.transform.SetParent(driverAnchor, false);
                    originRoot.transform.localPosition = Vector3.zero;

                    SeatTheRig(originRoot);

                    var cam = originRoot.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        Debug.Log($"[MainScene] using Starter Assets rig: {prefabPath}");
                        return cam;
                    }
                }
            }

            Debug.LogWarning("[MainScene] XR Origin prefab not found — constructing a " +
                             "rig manually. Importing the XR Interaction Toolkit " +
                             "Starter Assets sample is the more reliable path.");
            return ConstructXROrigin(driverAnchor, out originRoot);
        }

        /// <summary>
        /// The Starter Assets rig ships in Floor tracking mode with a camera
        /// offset of about 1.1 m, which is right for someone standing on their
        /// own floor and wrong for someone sitting in a car seat: it puts the
        /// view a metre above the roof, looking down at the front wheel.
        ///
        /// Device mode with a zero offset pins the eyes to the seat anchor and
        /// makes head movement relative to it, which is what a seated experience
        /// needs. Without this the scene looks like the car is missing — you are
        /// simply floating above it.
        /// </summary>
        private static void SeatTheRig(GameObject originRoot)
        {
            var origin = originRoot.GetComponent<XROrigin>();
            if (origin == null) return;

            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;

            if (origin.CameraFloorOffsetObject != null)
                origin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;

            EditorUtility.SetDirty(origin);
        }

        private static Camera ConstructXROrigin(Transform driverAnchor, out GameObject originRoot)
        {
            originRoot = new GameObject("XR Origin (XR Rig)");
            originRoot.transform.SetParent(driverAnchor, false);
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

            // Device mode, not Floor. The rig is parented inside the cabin, so
            // the seat position is authored by the cabin anchor rather than
            // derived from the player's real floor.
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

            return cam;
        }

        /// <summary>
        /// Solid colour, alpha ZERO. An opaque clear colour paints over the
        /// passthrough feed and produces the "app runs but everything is black"
        /// symptom. So does HDR — check the URP asset if this scene comes up dark.
        /// </summary>
        private static void ConfigureCameraForPassthrough(Camera camera, ARSession session)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);

            if (camera.GetComponent<AudioListener>() == null)
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
        }

        /// <summary>
        /// HandTrackingProvider must live on the XR Origin transform — it uses
        /// its own transform to convert XR-Origin space to world space, so on any
        /// other object the hands land in the wrong place.
        /// </summary>
        private static CalibrationManager BuildXRServices(GameObject originRoot, Camera head)
        {
            var hands = originRoot.AddComponent<HandTrackingProvider>();

            var calibration = originRoot.AddComponent<CalibrationManager>();
            new Wire(calibration)
                .Ref("xrOrigin", originRoot.transform)
                .Ref("headTransform", head.transform)
                .Ref("handProviderRef", hands)
                .Apply();

            var comfort = originRoot.AddComponent<ComfortSettings>();
            new Wire(comfort)
                .Bool("vignetteEnabled", false)   // no vignette object authored yet
                .Bool("referenceCageEnabled", false)
                .Apply();

            return calibration;
        }

        // ================================================================== //
        // input

        private static void BuildInput(VehicleController vehicle,
            CalibrationManager calibration, GameObject originRoot)
        {
            var go = new GameObject("IDS_Input");

            var hands = originRoot.GetComponent<HandTrackingProvider>();

            var wheel = go.AddComponent<WheelInputProvider>();
            new Wire(wheel)
                .Ref("calibration", calibration)
                .Ref("handProviderRef", hands)
                .Apply();

            var controller = go.AddComponent<ControllerInputProvider>();
            var keyboard   = go.AddComponent<KeyboardInputProvider>();

            // Order is cosmetic — VehicleInputAdapter sorts by Priority, so the
            // hand-tracked wheel (100) wins, then controller (50), then keyboard (1).
            var adapter = go.AddComponent<VehicleInputAdapter>();
            new Wire(adapter)
                .List("providerRefs", new List<Object> { wheel, controller, keyboard })
                .Apply();

            new Wire(vehicle).Ref("inputAdapter", adapter).Apply();
        }

        // ================================================================== //
        // data and analysis

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
                Debug.LogWarning("[MainScene] No CountryProfile assets found. Run " +
                                 "Tools → Intelli-Driving → Create Country Profiles, " +
                                 "then re-run this bootstrapper. The scene still plays, " +
                                 "but country selection will be empty.");
            }

            var w = new Wire(mgr).List("availableProfiles", found);
            if (australia != null)   w.Ref("defaultProfile", australia);
            else if (found.Count > 0) w.Ref("defaultProfile", found[0]);
            w.Apply();

            return mgr;
        }

        private static void BuildTelemetry(VehicleController vehicle,
            LaneReferenceSystem laneRef, Transform head,
            out MirrorCheckDetector mirror, out GapAcceptanceDetector gaps,
            out FeatureExtractor features, out TelemetryRecorder recorder)
        {
            var go = new GameObject("IDS_Telemetry");

            mirror = go.AddComponent<MirrorCheckDetector>();
            new Wire(mirror)
                .Ref("vehicleRef", vehicle)
                .Ref("headTransform", head)
                .Apply();

            recorder = go.AddComponent<TelemetryRecorder>();
            new Wire(recorder)
                .Ref("vehicleRef", vehicle)
                .Ref("laneRef", laneRef)
                .Ref("headTransform", head)
                .Ref("mirrorDetector", mirror)
                .Int("sampleRateHz", 20)
                .Apply();

            // The gap detector needs its own trigger volume at the junction, and
            // it must not sit on IDS_Telemetry or the trigger would follow the
            // wrong transform.
            var gapGo = new GameObject("GapZone");
            gapGo.transform.position = new Vector3(0f, 1f, JunctionZ);

            var box = gapGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(14f, 4f, 40f);

            gaps = gapGo.AddComponent<GapAcceptanceDetector>();
            new Wire(gaps)
                .Ref("conflictPoint", gapGo.transform)
                .Ref("vehicleRef", vehicle)
                .Apply();

            features = go.AddComponent<FeatureExtractor>();
            new Wire(features)
                .Ref("recorder", recorder)
                .Ref("mirrorDetector", mirror)
                .Ref("gapDetector", gaps)
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

        // ================================================================== //
        // traffic and scenarios

        private static NPCVehicleController BuildTraffic(GapAcceptanceDetector gaps)
        {
            var root = new GameObject("IDS_Traffic");

            // Lead vehicle path, straight down the lane ahead of the driver.
            var pathGo = new GameObject("LeadPath");
            pathGo.transform.SetParent(root.transform);
            for (int z = (int)LeadStartZ; z <= 380; z += 40)
            {
                var wp = new GameObject("LWP_" + z.ToString("D3"));
                wp.transform.SetParent(pathGo.transform);
                wp.transform.position = new Vector3(0f, 0.4f, z);
            }
            var path = pathGo.AddComponent<TrafficPath>();
            new Wire(path).Bool("loop", false).Float("defaultSpeedKph", 40f).Apply();

            var lead = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lead.name = "LeadVehicle";
            lead.transform.SetParent(root.transform);
            lead.transform.position = new Vector3(0f, 0.6f, LeadStartZ);
            lead.transform.localScale = new Vector3(1.8f, 1.2f, 4.2f);
            Paint(lead, new Color(0.25f, 0.4f, 0.7f));

            var leadCtrl = lead.AddComponent<NPCVehicleController>();
            new Wire(leadCtrl)
                .Ref("path", path)
                .Float("targetSpeedKph", 40f)
                .Apply();

            // Cross traffic at the junction, for gap acceptance.
            var cross = new List<Object>();
            for (int i = 0; i < 3; i++)
            {
                var cp = new GameObject("CrossPath_" + i);
                cp.transform.SetParent(root.transform);
                for (int x = -40; x <= 40; x += 20)
                {
                    var wp = new GameObject("CWP_" + (x + 40).ToString("D3"));
                    wp.transform.SetParent(cp.transform);
                    wp.transform.position = new Vector3(x, 0.4f, JunctionZ + 3f);
                }
                var cpath = cp.AddComponent<TrafficPath>();
                new Wire(cpath).Bool("loop", true).Float("defaultSpeedKph", 30f).Apply();

                var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
                car.name = "CrossVehicle_" + i;
                car.transform.SetParent(root.transform);
                car.transform.position = new Vector3(-40f + i * 18f, 0.6f, JunctionZ + 3f);
                car.transform.localScale = new Vector3(4.2f, 1.2f, 1.8f);
                Paint(car, new Color(0.6f, 0.6f, 0.62f));

                var ctrl = car.AddComponent<NPCVehicleController>();
                new Wire(ctrl).Ref("path", cpath).Float("targetSpeedKph", 30f).Apply();
                cross.Add(ctrl);
            }

            new Wire(gaps).List("crossTraffic", cross).Apply();
            return leadCtrl;
        }

        private static ScenarioManager BuildScenarios(VehicleController vehicle,
            AdaptiveScenarioSelector selector, DriverProfileManager profiles,
            MirrorCheckDetector mirror, CountryProfileManager country,
            NPCVehicleController lead)
        {
            var root = new GameObject("IDS_Scenarios");
            var manager = root.AddComponent<ScenarioManager>();

            var pedestrian = BuildPedestrianScenario(root.transform, vehicle);
            var cyclist    = BuildCyclistScenario(root.transform, vehicle, mirror, country);
            var leadBrake  = BuildLeadBrakeScenario(root.transform, vehicle, lead);

            new Wire(manager)
                .List("scenarios", new List<Object> { pedestrian, cyclist, leadBrake })
                .Ref("selector", selector)
                .Ref("profileManager", profiles)
                .Int("hazardsPerSession", 2)
                .Float("minSecondsBetweenHazards", 20f)
                .Apply();

            return manager;
        }

        private static PedestrianScenario BuildPedestrianScenario(
            Transform parent, VehicleController vehicle)
        {
            var go = new GameObject("PedestrianScenario");
            go.transform.SetParent(parent);

            var start = Anchor(go.transform, "Ped_Start", new Vector3( 6.5f, 1f, PedestrianZ));
            var end   = Anchor(go.transform, "Ped_End",   new Vector3(-4.0f, 1f, PedestrianZ));

            var ped = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ped.name = "Pedestrian";
            ped.transform.SetParent(go.transform);
            ped.transform.position = start.position;
            ped.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            Paint(ped, new Color(0.95f, 0.75f, 0.2f));

            var follower = ped.AddComponent<NPCPathFollower>();
            new Wire(follower)
                .Ref("startPoint", start)
                .Ref("endPoint", end)
                .Float("speedMetresPerSec", 1.4f)
                .Apply();

            var scenario = go.AddComponent<PedestrianScenario>();
            new Wire(scenario)
                .Str("scenarioId", "pedestrian_step_out")
                .Ref("pedestrian", follower)
                .Ref("stepOutPoint", ped.transform)
                .Ref("vehicleRef", vehicle)
                .Apply();

            return scenario;
        }

        private static CyclistScenario BuildCyclistScenario(Transform parent,
            VehicleController vehicle, MirrorCheckDetector mirror, CountryProfileManager country)
        {
            var go = new GameObject("CyclistScenario");
            go.transform.SetParent(parent);

            var start = Anchor(go.transform, "Cyc_Start", new Vector3(-4.6f, 1f, CyclistZ - 40f));
            var end   = Anchor(go.transform, "Cyc_End",   new Vector3(-4.6f, 1f, CyclistZ + 60f));
            var zone  = Anchor(go.transform, "ManoeuvreZone", new Vector3(0f, 1f, CyclistZ));

            var cyc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cyc.name = "Cyclist";
            cyc.transform.SetParent(go.transform);
            cyc.transform.position = start.position;
            cyc.transform.localScale = new Vector3(0.45f, 0.85f, 0.45f);
            Paint(cyc, new Color(0.35f, 0.85f, 0.45f));

            var follower = cyc.AddComponent<NPCPathFollower>();
            new Wire(follower)
                .Ref("startPoint", start)
                .Ref("endPoint", end)
                .Float("speedMetresPerSec", 5.5f)
                .Apply();

            // scenarioId MUST be set here even though CyclistScenario.Start
            // overwrites it. ScenarioManager arms the session from SessionManager
            // .Start, which can run before the scenario's own Start — and at that
            // moment the id is still the serialized default "unnamed_scenario",
            // so the planned hazard is reported as "not in the scene" and skipped.
            var scenario = go.AddComponent<CyclistScenario>();
            new Wire(scenario)
                .Str("scenarioId", "cyclist_blind_spot")
                .Ref("cyclist", follower)
                .Ref("manoeuvreZone", zone)
                .Ref("mirrorDetector", mirror)
                .Ref("countryProfiles", country)
                .Ref("vehicleRef", vehicle)
                .Apply();

            return scenario;
        }

        private static LeadBrakeScenario BuildLeadBrakeScenario(Transform parent,
            VehicleController vehicle, NPCVehicleController lead)
        {
            var go = new GameObject("LeadBrakeScenario");
            go.transform.SetParent(parent);

            // Same reason as CyclistScenario: the id has to be correct before
            // any Start() runs, or ScenarioManager cannot find this scenario.
            var scenario = go.AddComponent<LeadBrakeScenario>();
            new Wire(scenario)
                .Str("scenarioId", "lead_vehicle_brake")
                .Ref("leadVehicle", lead)
                .Ref("vehicleRef", vehicle)
                .Float("leadCruiseSpeedKph", 40f)
                .Float("followDistanceMetres", 18f)
                .Apply();

            return scenario;
        }

        // ================================================================== //
        // UI

        /// <summary>
        /// World space, not screen overlay. A screen-space canvas in XR renders
        /// at the near plane across both eyes and is unreadable. The panels sit
        /// on the dashboard so they move with the car but are not head-locked —
        /// head-locked UI in a driving task is a motion-sickness generator.
        /// </summary>
        private static void BuildUI(Transform car, Transform driverAnchor,
            VehicleController vehicle, CountryProfileManager country,
            DriverProfileManager profiles, CalibrationManager calibration,
            ScenarioManager scenarios)
        {
            var canvasGo = new GameObject("IDS_UI",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Parented to the driver anchor, NOT to the car. CabinConfigurator
            // mirrors the anchor's local X for right-hand-drive countries at
            // runtime, so a canvas positioned from the anchor at build time ends
            // up on the wrong side of the cabin the moment Australia is selected.
            // Parenting to the anchor means it moves with the seat.
            canvasGo.transform.SetParent(driverAnchor, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0.02f, 0.85f);
            canvasGo.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1400f, 900f);
            rt.localScale = Vector3.one * 0.00055f;

            // A bare EventSystem with no module. The XR Interaction Toolkit adds
            // XRUIInputModule itself when an interactor needs it, and only one UI
            // module is allowed per EventSystem — adding InputSystemUIInputModule
            // here gets it disabled at runtime with a warning.
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(
                    FindObjectsInactive.Include) == null)
            {
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            }

            var hud      = BuildHUD(canvasGo.transform, vehicle, country);
            var alerts   = BuildAlerts(canvasGo.transform, vehicle, country);
            var report   = BuildReport(canvasGo.transform, profiles, scenarios);
            var welcome  = BuildWelcomePanel(canvasGo.transform);
            var ready    = BuildSimplePanel(canvasGo.transform, "ReadyPanel",
                              "READY", "Grip the wheel and press Begin Drive.");
            var analysing = BuildSimplePanel(canvasGo.transform, "AnalysingPanel",
                              "ANALYSING", "Scoring your drive…");

            BuildCountryPanel(canvasGo.transform, out GameObject countryPanel,
                              out Transform cardContainer);
            BuildCalibrationPanel(canvasGo.transform, out GameObject calibrationPanel,
                              out TMP_Text calInstructions, out TMP_Text calStatus);

            var cardPrefab = EnsureCountryCardPrefab();

            var ui = canvasGo.AddComponent<UIManager>();
            new Wire(ui)
                .Ref("welcomePanel", welcome)
                .Ref("countryPanel", countryPanel)
                .Ref("calibrationPanel", calibrationPanel)
                .Ref("readyPanel", ready)
                .Ref("analysingPanel", analysing)
                .Ref("reportPanel", report)
                .Ref("hud", hud)
                .Ref("countryCardContainer", cardContainer)
                .Ref("countryCardPrefab", cardPrefab)
                .Ref("calibrationInstructions", calInstructions)
                .Ref("calibrationStatus", calStatus)
                .Ref("countryProfiles", country)
                .Ref("calibration", calibration)
                .Apply();

            canvasGo.AddComponent<LiveAlertTriggers>();
            new Wire(canvasGo.GetComponent<LiveAlertTriggers>())
                .Ref("vehicleRef", vehicle)
                .Ref("countryProfiles", country)
                .Apply();

            // UIManager drives panel visibility from SessionPhase; start hidden
            // so nothing flashes on the first frame.
            countryPanel.SetActive(false);
            calibrationPanel.SetActive(false);
            ready.SetActive(false);
            analysing.SetActive(false);
        }

        private static DrivingHUD BuildHUD(Transform parent,
            VehicleController vehicle, CountryProfileManager country)
        {
            var hudRoot = Panel(parent, "HUD",
                new Vector2(0f, 0f), new Vector2(0.3f, 0.24f), new Color(0f, 0f, 0f, 0.55f));

            var speed = Label(hudRoot.transform, "Speed", "0", 88,
                new Vector2(0.05f, 0.35f), new Vector2(0.62f, 0.95f), TextAlignmentOptions.Right);
            var unit = Label(hudRoot.transform, "Unit", "km/h", 26,
                new Vector2(0.64f, 0.38f), new Vector2(0.96f, 0.62f), TextAlignmentOptions.Left);
            var mode = Label(hudRoot.transform, "Mode", "TRAINING DRIVE", 22,
                new Vector2(0.05f, 0.16f), new Vector2(0.96f, 0.34f), TextAlignmentOptions.Left);
            var priority = Label(hudRoot.transform, "PriorityMirror", "", 20,
                new Vector2(0.05f, 0.02f), new Vector2(0.96f, 0.16f), TextAlignmentOptions.Left);

            var hud = parent.gameObject.AddComponent<DrivingHUD>();
            new Wire(hud)
                .Ref("hudRoot", hudRoot)
                .Ref("speedText", speed).Ref("speedUnitText", unit)
                .Ref("trainingModeText", mode).Ref("priorityMirrorText", priority)
                .Ref("vehicleRef", vehicle).Ref("countryProfiles", country)
                .Apply();

            return hud;
        }

        private static AlertManager BuildAlerts(Transform parent,
            VehicleController vehicle, CountryProfileManager country)
        {
            var alertRoot = Panel(parent, "Alert",
                new Vector2(0.28f, 0.80f), new Vector2(0.72f, 0.94f), new Color(0f, 0f, 0f, 0.7f));
            var alertText = Label(alertRoot.transform, "AlertText", "", 46,
                Vector2.zero, Vector2.one, TextAlignmentOptions.Center);

            var alerts = parent.gameObject.AddComponent<AlertManager>();
            new Wire(alerts).Ref("alertRoot", alertRoot).Ref("alertText", alertText).Apply();

            alertRoot.SetActive(false);
            return alerts;
        }

        private static GameObject BuildWelcomePanel(Transform parent)
        {
            var root = Panel(parent, "WelcomePanel",
                new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.85f),
                new Color(0.06f, 0.07f, 0.09f, 0.95f));

            Label(root.transform, "Title", "INTELLI-DRIVING SIMULATOR", 44,
                new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.94f), TextAlignmentOptions.Center);
            Label(root.transform, "Body",
                "A short training drive that adapts to the road rules you are " +
                "used to. You will pick the country you learned to drive in, " +
                "calibrate the wheel, then drive for about 90 seconds.", 24,
                new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.74f), TextAlignmentOptions.Top);

            MakeButton(root.transform, "StartButton", "START",
                new Vector2(0.35f, 0.10f), new Vector2(0.65f, 0.26f));

            return root;
        }

        private static void BuildCountryPanel(Transform parent,
            out GameObject root, out Transform container)
        {
            root = Panel(parent, "CountryPanel",
                new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.9f),
                new Color(0.06f, 0.07f, 0.09f, 0.95f));

            Label(root.transform, "Title", "WHERE DID YOU LEARN TO DRIVE?", 36,
                new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f), TextAlignmentOptions.Center);

            var holder = new GameObject("CardContainer", typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            holder.transform.SetParent(root.transform, false);

            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.04f, 0.06f);
            hrt.anchorMax = new Vector2(0.96f, 0.84f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;

            var layout = holder.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            container = holder.transform;
        }

        private static void BuildCalibrationPanel(Transform parent,
            out GameObject root, out TMP_Text instructions, out TMP_Text status)
        {
            root = Panel(parent, "CalibrationPanel",
                new Vector2(0.18f, 0.2f), new Vector2(0.82f, 0.85f),
                new Color(0.06f, 0.07f, 0.09f, 0.95f));

            Label(root.transform, "Title", "CALIBRATE THE WHEEL", 36,
                new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.95f), TextAlignmentOptions.Center);

            instructions = Label(root.transform, "Instructions",
                "Put both hands where the steering wheel would be, as if you " +
                "were holding it at ten and two. Hold still, then press Calibrate.",
                24, new Vector2(0.08f, 0.45f), new Vector2(0.92f, 0.80f),
                TextAlignmentOptions.Top);

            status = Label(root.transform, "Status", "Waiting for both hands…", 22,
                new Vector2(0.08f, 0.30f), new Vector2(0.92f, 0.43f),
                TextAlignmentOptions.Center);

            MakeButton(root.transform, "CalibrateButton", "CALIBRATE",
                new Vector2(0.14f, 0.08f), new Vector2(0.47f, 0.24f));
            MakeButton(root.transform, "ConfirmButton", "LOOKS RIGHT",
                new Vector2(0.53f, 0.08f), new Vector2(0.86f, 0.24f));
        }

        private static GameObject BuildSimplePanel(Transform parent,
            string name, string title, string body)
        {
            var root = Panel(parent, name,
                new Vector2(0.25f, 0.3f), new Vector2(0.75f, 0.75f),
                new Color(0.06f, 0.07f, 0.09f, 0.95f));

            Label(root.transform, "Title", title, 38,
                new Vector2(0.06f, 0.6f), new Vector2(0.94f, 0.85f), TextAlignmentOptions.Center);
            Label(root.transform, "Body", body, 24,
                new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.58f), TextAlignmentOptions.Top);

            if (name == "ReadyPanel")
                MakeButton(root.transform, "BeginDriveButton", "BEGIN DRIVE",
                    new Vector2(0.3f, 0.08f), new Vector2(0.7f, 0.28f));

            return root;
        }

        private static PostDriveReportPanel BuildReport(Transform parent,
            DriverProfileManager profiles, ScenarioManager scenarios)
        {
            var root = Panel(parent, "ReportPanel",
                new Vector2(0.14f, 0.08f), new Vector2(0.86f, 0.92f),
                new Color(0.06f, 0.07f, 0.09f, 0.96f));

            Label(root.transform, "Title", "SESSION COMPLETE", 34,
                new Vector2(0.06f, 0.91f), new Vector2(0.94f, 0.98f), TextAlignmentOptions.Left);

            var risk = Label(root.transform, "Risk", "-- / 100", 62,
                new Vector2(0.06f, 0.77f), new Vector2(0.6f, 0.91f), TextAlignmentOptions.Left);
            var band = Label(root.transform, "Band", "", 28,
                new Vector2(0.6f, 0.79f), new Vector2(0.94f, 0.89f), TextAlignmentOptions.Right);

            var riskBar = Bar(root.transform, "RiskBar",
                new Vector2(0.06f, 0.73f), new Vector2(0.94f, 0.76f));

            var lane   = SkillRow(root.transform, "Lane",     "Lane keeping",        0.655f, out Image laneBar);
            var steer  = SkillRow(root.transform, "Steering", "Steering smoothness", 0.585f, out Image steerBar);
            var mirror = SkillRow(root.transform, "Mirror",   "Mirror awareness",    0.515f, out Image mirrorBar);
            var gap    = SkillRow(root.transform, "Gap",      "Gap selection",       0.445f, out Image gapBar);
            var hazard = SkillRow(root.transform, "Hazard",   "Hazard response",     0.375f, out Image hazardBar);

            var weakest = Label(root.transform, "Weakest", "", 28,
                new Vector2(0.06f, 0.29f), new Vector2(0.94f, 0.36f), TextAlignmentOptions.Left);
            var recommend = Label(root.transform, "Recommendation", "", 22,
                new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.29f), TextAlignmentOptions.Left);
            var trend = Label(root.transform, "Trend", "", 20,
                new Vector2(0.06f, 0.17f), new Vector2(0.94f, 0.22f), TextAlignmentOptions.Left);
            var summary = Label(root.transform, "ScenarioSummary", "", 20,
                new Vector2(0.06f, 0.09f), new Vector2(0.94f, 0.17f), TextAlignmentOptions.TopLeft);

            MakeButton(root.transform, "DriveAgainButton", "DRIVE AGAIN",
                new Vector2(0.1f, 0.015f), new Vector2(0.45f, 0.08f));
            MakeButton(root.transform, "DoneButton", "DONE",
                new Vector2(0.55f, 0.015f), new Vector2(0.9f, 0.08f));

            var report = parent.gameObject.AddComponent<PostDriveReportPanel>();
            new Wire(report)
                .Ref("panelRoot", root)
                .Ref("compositeRiskText", risk).Ref("riskBandText", band)
                .Ref("compositeRiskBar", riskBar)
                .Ref("laneKeepingBar", laneBar).Ref("steeringBar", steerBar)
                .Ref("mirrorBar", mirrorBar).Ref("gapBar", gapBar).Ref("hazardBar", hazardBar)
                .Ref("laneKeepingValue", lane).Ref("steeringValue", steer)
                .Ref("mirrorValue", mirror).Ref("gapValue", gap).Ref("hazardValue", hazard)
                .Ref("weakestSkillText", weakest).Ref("recommendationText", recommend)
                .Ref("trendText", trend).Ref("scenarioSummaryText", summary)
                .Ref("profileManager", profiles)
                .Ref("scenarioManager", scenarios)
                .Apply();

            root.SetActive(false);
            return report;
        }

        private static TMP_Text SkillRow(Transform parent, string name, string caption,
            float yMin, out Image bar)
        {
            var label = Label(parent, name, caption + ": --", 22,
                new Vector2(0.06f, yMin + 0.028f), new Vector2(0.94f, yMin + 0.065f),
                TextAlignmentOptions.Left);
            bar = Bar(parent, name + "Bar",
                new Vector2(0.06f, yMin), new Vector2(0.94f, yMin + 0.022f));
            return label;
        }

        /// <summary>
        /// PostDriveReportPanel animates these with Image.fillAmount, which does
        /// nothing unless the Image type is Filled. That is the whole reason this
        /// helper exists rather than reusing Panel().
        /// </summary>
        private static Image Bar(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var track = Panel(parent, name + "_Track", min, max, new Color(1f, 1f, 1f, 0.12f));

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track.transform, false);

            var rt = fillGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = fillGo.GetComponent<Image>();
            img.color = new Color(0.3f, 0.8f, 0.5f);
            img.raycastTarget = false;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 0f;

            return img;
        }

        /// <summary>
        /// The button's onClick target is left empty on purpose. UIManager's
        /// handlers are public methods on a component that does not exist until
        /// this scene is built, and persistent listeners cannot be wired to a
        /// scene object from a static editor method without UnityEventTools.
        /// Hook them in the Inspector once: Start → OnStartPressed, Calibrate →
        /// OnCalibratePressed, Looks Right → OnCalibrationConfirmPressed,
        /// Begin Drive → OnBeginDrivePressed, Done → OnExitPressed.
        /// </summary>
        private static Button MakeButton(Transform parent, string name, string text,
            Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.15f, 0.45f, 0.42f, 0.95f);

            Label(go.transform, "Text", text, 26,
                Vector2.zero, Vector2.one, TextAlignmentOptions.Center);

            return go.GetComponent<Button>();
        }

        /// <summary>
        /// UIManager instantiates this once per country profile. No such prefab
        /// exists in the repo, so it is generated here and saved as an asset —
        /// generating it every build would orphan a new copy each time.
        /// </summary>
        private static GameObject EnsureCountryCardPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath);
            if (existing != null) return existing;

            Directory.CreateDirectory(PrefabDir);

            var card = new GameObject("CountryCard",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));

            var img = card.GetComponent<Image>();
            img.color = new Color(0.1f, 0.12f, 0.15f, 0.95f);

            var le = card.GetComponent<LayoutElement>();
            le.minWidth = 260f;
            le.minHeight = 320f;

            var name    = Label(card.transform, "Name", "Country", 30,
                new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.96f), TextAlignmentOptions.Center);
            var traffic = Label(card.transform, "TrafficSide", "", 20,
                new Vector2(0.06f, 0.72f), new Vector2(0.94f, 0.82f), TextAlignmentOptions.Center);
            var cabinSide = Label(card.transform, "CabinSide", "", 20,
                new Vector2(0.06f, 0.63f), new Vector2(0.94f, 0.72f), TextAlignmentOptions.Center);
            var diff    = Label(card.transform, "Differences", "", 18,
                new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.62f), TextAlignmentOptions.Top);

            var button = MakeButton(card.transform, "SelectButton", "SELECT",
                new Vector2(0.12f, 0.05f), new Vector2(0.88f, 0.2f));

            var view = card.AddComponent<CountryCardView>();
            new Wire(view)
                .Ref("nameText", name)
                .Ref("trafficSideText", traffic)
                .Ref("cabinSideText", cabinSide)
                .Ref("differencesText", diff)
                .Ref("selectButton", button)
                .Apply();

            var prefab = PrefabUtility.SaveAsPrefabAsset(card, CardPath);
            Object.DestroyImmediate(card);

            Debug.Log("[MainScene] created country card prefab at " + CardPath);
            return prefab;
        }

        // ================================================================== //
        // session

        private static void BuildSession(TelemetryRecorder recorder, ScenarioManager scenarios)
        {
            var go = new GameObject("IDS_Session");
            var session = go.AddComponent<SessionManager>();
            go.AddComponent<DevHotkeys>();

            new Wire(session)
                .Ref("telemetryRecorderRef", recorder)
                .Ref("scenarioManagerRef", scenarios)
                .Float("driveDurationSeconds", 90f)
                // ON by default so a freshly built scene does something the
                // moment you press Play: it skips welcome/country/calibration
                // and goes straight to driving, which is the only way to get
                // telemetry without clicking world-space buttons with a mouse.
                // UNTICK IT on IDS_Session before the headset demo, or testers
                // never see the country selection.
                .Bool("fastStartForTesting", true)
                .Apply();
        }

        private static void BuildBootstrap()
        {
            var go = new GameObject("IDS_XRBootstrap");
            new Wire(go.AddComponent<XRBootstrap>())
                .Bool("initialiseOnStart", true)
                .Int("targetFrameRate", 72)
                .Apply();
        }

        // ================================================================== //
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

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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
            Debug.Log("[MainScene] created tag '" + tag + "'");
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
                    Debug.LogError($"[MainScene] {_owner} has no serialized field " +
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