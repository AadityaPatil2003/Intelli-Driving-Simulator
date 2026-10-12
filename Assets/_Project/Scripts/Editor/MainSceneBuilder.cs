#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TMPro;
using IDS.AI;
using IDS.Core;
using IDS.Scenarios;
using IDS.Telemetry;
using IDS.Traffic;
using IDS.UI;
using IDS.Vehicle;
using IDS.XR;

namespace IDS.EditorTools
{
    /// <summary>
    /// Builds the whole scene, wired, in one pass.
    ///
    ///     Tools → Intelli-Driving → Build Main Scene
    ///
    /// WHY ONE BUILDER AND NOT SEVERAL STEPS. The previous version of this
    /// project had a scene bootstrapper, a separate button wirer, a separate
    /// "upgrade" pass and a build-list setter, each of which had to be run in the
    /// right order against the right open scene. One of them was run against the
    /// wrong scene and its fixes went nowhere, silently — the log said it had
    /// succeeded. Every step now happens here, in order, against a scene this
    /// method created itself, and it finishes by validating its own output.
    ///
    /// Running it replaces Assets/_Project/Scenes/Main/Main.unity and sets it as
    /// the only scene in the build list.
    /// </summary>
    public static class MainSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main/Main.unity";
        private const string ProfileFolder = "Assets/_Project/Data/CountryProfiles";

        // ---- road and world dimensions, all metres ---------------------------

        private const float RoadLength = 1400f;
        private const float RoadWidth = 10f;          // two 3.5 m lanes plus margin
        private const float LaneOffset = 1.75f;       // lane centre from road centre
        private const float RoadStartZ = -30f;
        private const float DriveEndZ = RoadLength - 90f;

        // ---- cabin dimensions, local to the vehicle root ---------------------

        private const float SeatX = -0.38f;           // LEFT-hand-drive baseline
        private const float SeatY = 0.78f;
        private const float SeatZ = -0.15f;

        private const float WheelRadius = 0.17f;

        private static readonly StringBuilder Report = new();

        // =====================================================================

        [MenuItem("Tools/Intelli-Driving/Build Main Scene", false, 10)]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog("Build Main Scene",
                    "This replaces:\n\n" + ScenePath +
                    "\n\nand sets it as the only scene in the build list.\n\n" +
                    "Any hand edits to that scene will be lost.", "Build", "Cancel"))
                return;

            Report.Clear();
            Line("Intelli-Driving-Simulator — scene build");
            Line($"Unity {Application.unityVersion}");
            Line("");

            SceneBuildUtil.EnsureFolder("Assets/_Project/Scenes/Main");
            SceneBuildUtil.EnsureFolder(SceneBuildUtil.MaterialFolder);

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // LOAD THE PROFILES AFTER THE NEW SCENE, NOT BEFORE. Opening a new
            // scene unloads assets that nothing in the open scene references yet,
            // and the three CountryProfile ScriptableObjects are exactly that at
            // this point. Loading them first left three destroyed references:
            // still non-null as far as C# is concerned, but fake-null to Unity,
            // so they serialised into the scene as fileID: 0. The build reported
            // "3 country profile(s) assigned" and the headset showed a country
            // screen with no countries on it.
            //
            // Nothing between here and the wiring may open or close a scene.
            List<CountryProfile> profiles = LoadProfiles();

            // ---- build, in dependency order ----------------------------------

            var services = BuildServices(profiles);
            BuildLighting();

            var env = BuildEnvironment();
            var car = BuildVehicle(env);
            var rig = BuildXRRig(car);
            var traffic = BuildTraffic(env);
            var ui = BuildUI(car, rig);
            var hazards = BuildScenarios(env, traffic);

            WireEverything(services, env, car, rig, traffic, ui, hazards, profiles);

            // ---- persist ------------------------------------------------------

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            Line(saved ? $"Scene saved → {ScenePath}" : "SCENE SAVE FAILED");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SetBuildList();

            // ---- validate its own output --------------------------------------

            int problems = Validate(services, env, car, rig, ui, hazards, traffic);

            Debug.Log("<b>[SceneBuilder]</b>\n" + Report);

            EditorUtility.DisplayDialog(
                problems == 0 ? "Scene built" : $"Scene built with {problems} problem(s)",
                problems == 0
                    ? "Main.unity is built, wired and validated.\n\n" +
                      "Next: File → Save Project, then\n" +
                      "Tools → Intelli-Driving → Build Quest APK"
                    : $"The scene was built, but validation found {problems} " +
                      "problem(s).\n\nThe full report is in the Console — read it " +
                      "before building an APK.",
                "OK");
        }

        private static void Line(string s) => Report.AppendLine(s);

        // =====================================================================
        //  services
        // =====================================================================

        private class Services
        {
            public GameObject Root;
            public SessionManager Session;
            public CountryProfileManager Countries;
            public DriverProfileManager Drivers;
            public AdaptiveScenarioSelector Selector;
            public RuleBasedRiskScorer RuleScorer;
            public ClassifierRiskScorer Classifier;
            public RiskScoringSystem Scoring;
            public CountryProfileHolder ProfileHolder;
            public TelemetryRecorder Recorder;
            public FeatureExtractor Features;
            public MirrorCheckDetector Mirrors;
        }

        private static Services BuildServices(List<CountryProfile> profiles)
        {
            var s = new Services { Root = SceneBuildUtil.Go("_Services") };
            Transform t = s.Root.transform;

            s.Root.AddComponent<XRBootstrap>();

            s.Session = s.Root.AddComponent<SessionManager>();
            s.Countries = s.Root.AddComponent<CountryProfileManager>();
            s.Selector = s.Root.AddComponent<AdaptiveScenarioSelector>();
            s.Drivers = s.Root.AddComponent<DriverProfileManager>();
            s.RuleScorer = s.Root.AddComponent<RuleBasedRiskScorer>();
            s.Classifier = s.Root.AddComponent<ClassifierRiskScorer>();
            s.Scoring = s.Root.AddComponent<RiskScoringSystem>();
            s.ProfileHolder = s.Root.AddComponent<CountryProfileHolder>();

            GameObject telemetry = SceneBuildUtil.Go("Telemetry", t);
            s.Recorder = telemetry.AddComponent<TelemetryRecorder>();
            s.Features = telemetry.AddComponent<FeatureExtractor>();
            s.Mirrors = telemetry.AddComponent<MirrorCheckDetector>();

            GameObject dev = SceneBuildUtil.Go("Dev", t);
            dev.AddComponent<IDS.Dev.DevHotkeys>();

            // Controller equivalents of the dev hotkeys, so a participant in the
            // headset can end a drive, dismiss a report and start a new session
            // without a keyboard and without pointing at anything.
            SceneBuildUtil.Go("Shortcuts", t).AddComponent<VRSessionShortcuts>();

            // The coach. On by default: this is a training tool, so every hazard
            // briefs its procedure, cues the step and gives a verdict. Turn
            // coachingEnabled off on this component for a measured assessment or
            // a user-testing baseline, where prompting would contaminate the
            // habits being measured.
            SceneBuildUtil.Go("Coach", t).AddComponent<IDS.UI.DriveCoach>();

            // Records peak input and the scenario queue for the report screen.
            SceneBuildUtil.Go("Diagnostics", t)
                          .AddComponent<IDS.Telemetry.SessionDiagnostics>();

            bool listOk = SceneBuildUtil.Wire(s.Countries, "availableProfiles", profiles);

            CountryProfile fallback =
                profiles.FirstOrDefault(p => p.profileId == "australia")
                ?? profiles.FirstOrDefault();

            bool defaultOk = SceneBuildUtil.Wire(s.Countries, "defaultProfile", fallback);

            // Report what actually landed, not what was attempted. The scene
            // previously came out with three empty profile slots while the build
            // log said "3 country profile(s) assigned", and the only symptom was
            // an empty country-selection screen on the headset.
            Line(listOk && defaultOk
                 ? $"Services: {profiles.Count} country profile(s) assigned and "
                   + $"verified, default = {(fallback != null ? fallback.displayName : "none")}"
                 : "PROBLEM: country profiles did NOT serialise into the scene. "
                   + "The country-selection screen will be empty.");

            return s;
        }

        private static List<CountryProfile> LoadProfiles()
        {
            var profiles = new List<CountryProfile>();

            foreach (string guid in AssetDatabase.FindAssets("t:CountryProfile",
                                                             new[] { ProfileFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var p = AssetDatabase.LoadAssetAtPath<CountryProfile>(path);
                if (p != null) profiles.Add(p);
            }

            // Australia, India, USA — a stable order so the cards do not shuffle
            // between builds.
            var order = new List<string> { "australia", "india", "usa" };
            profiles = profiles
                .OrderBy(p => order.IndexOf(p.profileId) < 0 ? 99 : order.IndexOf(p.profileId))
                .ToList();

            if (profiles.Count == 0)
                Line("PROBLEM: no CountryProfile assets found in " + ProfileFolder +
                     ". Run Tools → Intelli-Driving → Create Country Profiles first.");
            else
                Line("Profiles found: " + string.Join(", ", profiles.Select(p => p.profileId)));

            return profiles;
        }

        // =====================================================================
        //  lighting
        // =====================================================================

        private static void BuildLighting()
        {
            GameObject root = SceneBuildUtil.Go("Lighting");

            GameObject sunGo = SceneBuildUtil.Go("Sun", root.transform);
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.90f);
            sun.intensity = 1.15f;

            // Baked lighting means a bake step, a lightmap and a thing to go
            // stale; one realtime directional light is both cheaper to maintain
            // and adequate for simple geometry.
            //
            // HARD shadows, matching the URP asset: Soft Shadows are switched off
            // in the Android quality level, so asking for soft here would silently
            // get hard ones anyway. Saying hard means the scene and the pipeline
            // agree about what is being rendered.
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.6f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.62f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.22f, 0.20f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.76f);
            RenderSettings.fogStartDistance = 160f;
            RenderSettings.fogEndDistance = 420f;

            Line("Lighting: one realtime directional light with hard shadows, " +
                 "trilight ambient, linear fog from 160 m — which is what hides " +
                 "the far end of the road and the 20 m shadow cut-off, instead of " +
                 "both ending in a visible line across the ground");
        }

        // =====================================================================
        //  environment
        // =====================================================================

        private class Env
        {
            public GameObject Root;
            public Transform EgoLaneRoot;
            public Transform OncomingLaneRoot;
            public LanePath EgoLane;
            public LaneReferenceSystem LaneReference;
            public Transform VehicleStart;
            public Transform ConflictPoint;
            public Transform JunctionApproach;
            public GapAcceptanceDetector GapDetector;
            public TrafficPath CrossPath;
            public TrafficPath LeadPath;
            public List<Transform> CrossWaypoints;
            public List<Transform> LeadWaypoints;
            public Transform CyclistPathRoot;
            public Transform PedestrianStart;
            public Transform PedestrianEnd;
            public Transform ManoeuvreZone;
            public Transform[] Signs;
            public EnvironmentConfigurator Configurator;
        }

        private static Env BuildEnvironment()
        {
            var env = new Env { Root = SceneBuildUtil.Go("Environment") };
            Transform t = env.Root.transform;

            Material grass = SceneBuildUtil.Mat("Grass", new Color(0.26f, 0.34f, 0.20f), 0.05f);
            Material asphalt = SceneBuildUtil.Mat("Asphalt", new Color(0.17f, 0.17f, 0.18f), 0.25f);
            Material paint = SceneBuildUtil.Mat("LinePaint", new Color(0.92f, 0.92f, 0.88f), 0.1f);
            Material kerb = SceneBuildUtil.Mat("Kerb", new Color(0.62f, 0.60f, 0.57f), 0.1f);
            Material post = SceneBuildUtil.Mat("VergePost", new Color(0.85f, 0.85f, 0.82f), 0.1f);
            Material building = SceneBuildUtil.Mat("Building", new Color(0.45f, 0.44f, 0.46f), 0.15f);
            Material signFace = SceneBuildUtil.Mat("SignFace", new Color(0.92f, 0.92f, 0.90f), 0.2f);

            float midZ = RoadStartZ + RoadLength * 0.5f;

            // Ground. A cube rather than a plane: a plane is single-sided and
            // disappears if the driver ever gets under it, which is exactly when
            // you most need to see where the ground is.
            SceneBuildUtil.Box("Ground", t, new Vector3(0f, -0.5f, midZ),
                               new Vector3(600f, 1f, RoadLength + 400f), grass, collider: true)
                          .tag = "RoadSurface";

            GameObject road = SceneBuildUtil.Box("Road", t, new Vector3(0f, 0.01f, midZ),
                                                 new Vector3(RoadWidth, 0.04f, RoadLength),
                                                 asphalt, collider: true);
            road.tag = "RoadSurface";

            // Centre line: one long quad with a tiled dash texture, instead of
            // two hundred separate dash objects.
            Material dashes = SceneBuildUtil.DashedLineMat("LaneDashes", RoadLength / 8f);
            SceneBuildUtil.Box("CentreLine", t, new Vector3(0f, 0.035f, midZ),
                               new Vector3(0.14f, 0.01f, RoadLength), dashes);

            SceneBuildUtil.Box("EdgeLineLeft", t,
                               new Vector3(-RoadWidth * 0.5f + 0.35f, 0.035f, midZ),
                               new Vector3(0.12f, 0.01f, RoadLength), paint);
            SceneBuildUtil.Box("EdgeLineRight", t,
                               new Vector3(RoadWidth * 0.5f - 0.35f, 0.035f, midZ),
                               new Vector3(0.12f, 0.01f, RoadLength), paint);

            SceneBuildUtil.Box("KerbLeft", t,
                               new Vector3(-RoadWidth * 0.5f - 0.2f, 0.07f, midZ),
                               new Vector3(0.4f, 0.14f, RoadLength), kerb, collider: true);
            SceneBuildUtil.Box("KerbRight", t,
                               new Vector3(RoadWidth * 0.5f + 0.2f, 0.07f, midZ),
                               new Vector3(0.4f, 0.14f, RoadLength), kerb, collider: true);

            // Verge posts every 40 m. Their only job is optical flow: without
            // near-field objects passing the driver, speed is very hard to judge
            // and a long straight road feels static.
            GameObject posts = SceneBuildUtil.Go("VergePosts", t);
            int postCount = 0;
            for (float z = RoadStartZ + 20f; z < RoadStartZ + RoadLength - 20f; z += 40f)
            {
                SceneBuildUtil.Box($"Post_L_{postCount}", posts.transform,
                                   new Vector3(-RoadWidth * 0.5f - 1.2f, 0.55f, z),
                                   new Vector3(0.1f, 1.1f, 0.1f), post);
                SceneBuildUtil.Box($"Post_R_{postCount}", posts.transform,
                                   new Vector3(RoadWidth * 0.5f + 1.2f, 0.55f, z),
                                   new Vector3(0.1f, 1.1f, 0.1f), post);
                postCount++;
            }

            // Buildings for depth and for a sense of an urban district.
            GameObject blocks = SceneBuildUtil.Go("Buildings", t);
            int blockIndex = 0;
            for (float z = RoadStartZ + 60f; z < RoadStartZ + RoadLength - 60f; z += 85f)
            {
                float h = 6f + (blockIndex % 4) * 3.5f;
                float w = 14f + (blockIndex % 3) * 6f;

                SceneBuildUtil.Box($"Block_L_{blockIndex}", blocks.transform,
                                   new Vector3(-RoadWidth * 0.5f - 14f, h * 0.5f, z),
                                   new Vector3(w, h, 24f), building, collider: true);
                SceneBuildUtil.Box($"Block_R_{blockIndex}", blocks.transform,
                                   new Vector3(RoadWidth * 0.5f + 14f, h * 0.5f, z + 40f),
                                   new Vector3(w, h, 24f), building, collider: true);
                blockIndex++;
            }

            // ---- lanes --------------------------------------------------------

            GameObject laneRootGo = SceneBuildUtil.Go("Lanes", t);
            env.LaneReference = laneRootGo.AddComponent<LaneReferenceSystem>();

            env.EgoLaneRoot = SceneBuildUtil.Go("EgoLane", laneRootGo.transform,
                                                new Vector3(-LaneOffset, 0f, 0f)).transform;
            GameObject egoPathGo = SceneBuildUtil.Go("Path", env.EgoLaneRoot);
            env.EgoLane = egoPathGo.AddComponent<LanePath>();

            var egoWaypoints = new List<Transform>();
            for (float z = RoadStartZ; z <= RoadStartZ + RoadLength; z += 50f)
                egoWaypoints.Add(SceneBuildUtil.Go($"wp_{(int)z}", egoPathGo.transform,
                                                   new Vector3(0f, 0.05f, z)).transform);

            SceneBuildUtil.Wire(env.EgoLane, "waypoints", egoWaypoints);
            SceneBuildUtil.Wire(env.EgoLane, "laneHalfWidth", 1.75f);
            SceneBuildUtil.Wire(env.LaneReference, "lanePaths",
                                new List<LanePath> { env.EgoLane });

            env.OncomingLaneRoot = SceneBuildUtil.Go("OncomingLane", laneRootGo.transform,
                                                     new Vector3(LaneOffset, 0f, 0f)).transform;

            // ---- start point --------------------------------------------------

            env.VehicleStart = SceneBuildUtil.Go("VehicleStart", t,
                                                 new Vector3(-LaneOffset, 0.5f, RoadStartZ + 25f))
                                             .transform;

            // ---- intersection and the gap measurement -------------------------

            GameObject junction = SceneBuildUtil.Go("Intersection", t,
                                                    new Vector3(0f, 0f, 520f));

            SceneBuildUtil.Box("CrossRoad", junction.transform, new Vector3(0f, 0.012f, 0f),
                               new Vector3(120f, 0.04f, RoadWidth), asphalt, collider: true)
                          .tag = "RoadSurface";

            env.ConflictPoint = SceneBuildUtil.Go("ConflictPoint", junction.transform,
                                                  new Vector3(-LaneOffset, 0.2f, 0f)).transform;

            GameObject gapTrigger = SceneBuildUtil.Go("GapApproach", junction.transform,
                                                      new Vector3(-LaneOffset, 1f, -16f));
            env.JunctionApproach = gapTrigger.transform;

            var gapBox = gapTrigger.AddComponent<BoxCollider>();
            gapBox.isTrigger = true;
            gapBox.size = new Vector3(3.4f, 2.4f, 34f);
            env.GapDetector = gapTrigger.AddComponent<GapAcceptanceDetector>();

            // ---- make the junction legible ------------------------------------
            // It was an unmarked crossroads with live cross traffic: no sign, no
            // give-way line, no warning. The driver's first indication was a car
            // arriving from one side. Signing it is not decoration — the gap the
            // driver accepts is one of the five scored skills, and a gap accepted
            // at an intersection you did not know was there measures surprise.

            GameObject briefing = SceneBuildUtil.Go("JunctionBriefing", junction.transform,
                                                    new Vector3(-LaneOffset, 1f, -70f));
            var briefBox = briefing.AddComponent<BoxCollider>();
            briefBox.isTrigger = true;
            briefBox.size = new Vector3(3.4f, 2.4f, 10f);
            briefing.AddComponent<IDS.UI.JunctionCoach>();

            // Give-way line: a row of white blocks across the ego lane, which is
            // what a driver actually looks for when deciding where to stop.
            for (int i = 0; i < 7; i++)
            {
                SceneBuildUtil.Box($"GiveWayBlock_{i}", junction.transform,
                                   new Vector3(-LaneOffset - 1.5f + i * 0.5f, 0.016f, -6.4f),
                                   new Vector3(0.30f, 0.02f, 0.60f), paint);
            }

            GameObject giveWay = SceneBuildUtil.Go("GiveWaySign", junction.transform,
                                                   new Vector3(-6.2f, 0f, -14f));
            SceneBuildUtil.Box("Pole", giveWay.transform, new Vector3(0f, 1.1f, 0f),
                               new Vector3(0.08f, 2.2f, 0.08f), post);
            GameObject gwFace = SceneBuildUtil.Box("Face", giveWay.transform,
                                                   new Vector3(0f, 2.3f, 0f),
                                                   new Vector3(0.80f, 0.80f, 0.04f),
                                                   SceneBuildUtil.Mat("SignRed",
                                                       new Color(0.78f, 0.12f, 0.14f), 0.2f));
            gwFace.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            SceneBuildUtil.Box("Inner", giveWay.transform, new Vector3(0f, 2.3f, -0.03f),
                               new Vector3(0.52f, 0.52f, 0.04f), signFace)
                          .transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            SceneBuildUtil.WorldText("Label", giveWay.transform,
                                     new Vector3(0f, 2.28f, -0.06f), "GIVE\nWAY",
                                     1.7f, new Color(0.08f, 0.08f, 0.09f),
                                     new Vector2(0.9f, 0.9f));

            GameObject crossPathGo = SceneBuildUtil.Go("CrossTrafficPath", junction.transform);
            env.CrossPath = crossPathGo.AddComponent<TrafficPath>();
            var crossWaypoints = new List<Transform>
            {
                SceneBuildUtil.Go("cw_0", crossPathGo.transform, new Vector3(-58f, 0.4f, 1.8f)).transform,
                SceneBuildUtil.Go("cw_1", crossPathGo.transform, new Vector3(0f, 0.4f, 1.8f)).transform,
                SceneBuildUtil.Go("cw_2", crossPathGo.transform, new Vector3(58f, 0.4f, 1.8f)).transform
            };
            env.CrossWaypoints = crossWaypoints;
            SceneBuildUtil.Wire(env.CrossPath, "waypoints", crossWaypoints);
            SceneBuildUtil.Wire(env.CrossPath, "loop", true);
            SceneBuildUtil.Wire(env.CrossPath, "defaultSpeedKph", 38f);

            // ---- lead vehicle route -------------------------------------------
            //
            // A child of the EGO LANE root, with waypoints at local x = 0. The
            // lead vehicle drives in the driver's own lane, so when the lane moves
            // to the other side of the road for a right-hand-traffic profile, the
            // lead car moves with it for free. Parented to the environment root
            // instead, it would stay in the oncoming lane and the following-
            // distance hazard would never trigger.

            GameObject leadPathGo = SceneBuildUtil.Go("LeadVehiclePath", env.EgoLaneRoot);
            env.LeadPath = leadPathGo.AddComponent<TrafficPath>();
            var leadWaypoints = new List<Transform>();
            for (float z = 150f; z <= RoadStartZ + RoadLength; z += 80f)
                leadWaypoints.Add(SceneBuildUtil.Go($"lw_{(int)z}", leadPathGo.transform,
                                                    new Vector3(0f, 0.4f, z)).transform);
            env.LeadWaypoints = leadWaypoints;
            SceneBuildUtil.Wire(env.LeadPath, "waypoints", leadWaypoints);
            SceneBuildUtil.Wire(env.LeadPath, "loop", false);
            SceneBuildUtil.Wire(env.LeadPath, "defaultSpeedKph", 40f);

            // ---- hazard placement anchors -------------------------------------

            env.CyclistPathRoot = SceneBuildUtil.Go("CyclistPath", t,
                                                    new Vector3(-3.1f, 0f, 0f)).transform;

            env.PedestrianStart = SceneBuildUtil.Go("PedestrianStart", t,
                                                    new Vector3(-4.6f, 0.9f, 300f)).transform;
            // x +4.6, not +1.2. The road is 10 m wide (x -5 to +5), so an end
            // point at +1.2 had the pedestrian stop just past the centre line and
            // stand there. A hazard that clears your lane halfway through is not
            // the hazard the scoring assumes.
            env.PedestrianEnd = SceneBuildUtil.Go("PedestrianEnd", t,
                                                  new Vector3(4.6f, 0.9f, 300f)).transform;

            env.ManoeuvreZone = SceneBuildUtil.Go("CyclistManoeuvreZone", t,
                                                  new Vector3(-LaneOffset, 0.5f, 760f)).transform;

            // ---- signs ---------------------------------------------------------

            GameObject signRoot = SceneBuildUtil.Go("Signs", t);
            var signs = new List<Transform>();

            Material signRed = SceneBuildUtil.Mat("SignRed", new Color(0.78f, 0.12f, 0.14f), 0.2f);
            Material signWarn = SceneBuildUtil.Mat("SignWarn", new Color(0.95f, 0.76f, 0.09f), 0.2f);
            Material signInk = SceneBuildUtil.Mat("SignInk", new Color(0.08f, 0.08f, 0.09f), 0.2f);
            Material zebra = SceneBuildUtil.Mat("Zebra", new Color(0.93f, 0.93f, 0.91f), 0.15f);

            // Speed-limit signs. The number comes from the country profile at
            // runtime, because the scorer marks speed compliance against that
            // same number — a painted-on "50" would contradict the scoring the
            // moment the driver picks a country with a different limit.
            float[] limitZ = { 180f, 430f, 940f, 1180f };
            for (int i = 0; i < limitZ.Length; i++)
            {
                GameObject sign = SceneBuildUtil.Go($"SpeedLimit_{i}", signRoot.transform,
                                                    new Vector3(-6.2f, 0f, limitZ[i]));
                SceneBuildUtil.Box("Pole", sign.transform, new Vector3(0f, 1.1f, 0f),
                                   new Vector3(0.08f, 2.2f, 0.08f), post);
                SceneBuildUtil.Box("Ring", sign.transform, new Vector3(0f, 2.3f, 0f),
                                   new Vector3(0.80f, 0.80f, 0.04f), signRed);
                SceneBuildUtil.Box("Face", sign.transform, new Vector3(0f, 2.3f, -0.03f),
                                   new Vector3(0.62f, 0.62f, 0.04f), signFace);

                // Faces back down the road, toward oncoming traffic.
                // NO 180 deg flip. TextMeshPro is readable from its LOCAL -Z side:
                // Unity's default camera sits at negative z looking along +Z and
                // reads text at the origin correctly, which means the reader is on
                // the text's -Z side. The driver approaches from low z, so the
                // driver is already on the sign's -Z side and identity rotation is
                // what they can read. Rotating 180 deg turned the number into its
                // own mirror image.
                TextMeshPro limit = SceneBuildUtil.WorldText(
                    "Limit", sign.transform, new Vector3(0f, 2.3f, -0.06f),
                    "50", 5.2f, new Color(0.08f, 0.08f, 0.09f), new Vector2(0.6f, 0.6f));

                sign.AddComponent<SpeedLimitSign>().Configure(limit);
                signs.Add(sign.transform);
            }

            // Pedestrian crossing: a warning sign on each approach plus the
            // painted bars, so the hazard is something the driver could have
            // anticipated rather than an ambush. A reaction time measured against
            // an unsignposted pedestrian is measuring surprise, not driving.
            float crossingZ = env.PedestrianStart != null
                ? env.PedestrianStart.position.z : 690f;

            foreach (float offset in new[] { -55f, -28f })
            {
                GameObject warn = SceneBuildUtil.Go(
                    $"PedCrossingWarn_{Mathf.Abs(offset):F0}", signRoot.transform,
                    new Vector3(-6.2f, 0f, crossingZ + offset));

                SceneBuildUtil.Box("Pole", warn.transform, new Vector3(0f, 1.1f, 0f),
                                   new Vector3(0.08f, 2.2f, 0.08f), post);

                GameObject face = SceneBuildUtil.Box("Face", warn.transform,
                                                     new Vector3(0f, 2.3f, 0f),
                                                     new Vector3(0.62f, 0.62f, 0.04f),
                                                     signWarn);
                face.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

                SceneBuildUtil.Box("Figure", warn.transform, new Vector3(0f, 2.3f, -0.04f),
                                   new Vector3(0.10f, 0.34f, 0.03f), signInk);

                // A yellow diamond with a black bar on it means nothing to
                // somebody who has not been told what it means, and this is a
                // training tool for drivers meeting an unfamiliar road system.
                // Label it.
                SceneBuildUtil.WorldText("Label", warn.transform,
                                         new Vector3(0f, 1.62f, -0.06f), "CROSSING",
                                         1.5f, new Color(0.10f, 0.10f, 0.11f),
                                         new Vector2(1.4f, 0.4f));

                signs.Add(warn.transform);
            }

            // Zebra bars across both lanes at the crossing.
            GameObject crossing = SceneBuildUtil.Go("PedestrianCrossing", t,
                                                    new Vector3(0f, 0f, crossingZ));
            for (int i = -4; i <= 4; i++)
            {
                SceneBuildUtil.Box($"Bar_{i + 4}", crossing.transform,
                                   new Vector3(i * 1.05f, 0.012f, 0f),
                                   new Vector3(0.52f, 0.02f, 3.0f), zebra);
            }

            env.Signs = signs.ToArray();

            env.Configurator = env.Root.AddComponent<EnvironmentConfigurator>();

            Line($"Environment: {RoadLength:F0} m road, {postCount * 2} verge posts, " +
                 $"{blockIndex * 2} blocks, intersection at z=520, " +
                 $"{signs.Count} signs (speed limits driven by the country "
                 + "profile, pedestrian crossing signposted and painted), "
                 + "lane half-width 1.75 m");

            return env;
        }

        // =====================================================================
        //  vehicle and cabin
        // =====================================================================

        private class Car
        {
            public GameObject Root;
            public VehicleController Controller;
            public Rigidbody Body;
            public CabinConfigurator Cabin;
            public Transform CabinRoot;
            public Transform SeatAnchor;
            public Transform SteeringColumn;
            public Transform Wheel;
            public Transform Dashboard;
            public Transform MirrorLeft, MirrorRight, MirrorCentre;
            public GameObject PriorityHighlight;
            public RoadEndGuard EndGuard;
        }

        private static Car BuildVehicle(Env env)
        {
            var car = new Car();

            Material bodyMat = SceneBuildUtil.Mat("CarBody", new Color(0.16f, 0.30f, 0.52f), 0.55f, 0.35f);
            Material interior = SceneBuildUtil.Mat("CarInterior", new Color(0.13f, 0.13f, 0.14f), 0.15f);
            Material dash = SceneBuildUtil.Mat("Dashboard", new Color(0.10f, 0.10f, 0.11f), 0.1f);
            Material rim = SceneBuildUtil.Mat("WheelRim", new Color(0.09f, 0.09f, 0.10f), 0.3f);
            Material tyre = SceneBuildUtil.Mat("Tyre", new Color(0.07f, 0.07f, 0.07f), 0.05f);
            Material glass = SceneBuildUtil.Mat("MirrorGlass", Color.white, 0.9f, 0.1f);
            Material highlight = SceneBuildUtil.Mat("PriorityHighlight",
                                                    new Color(0.2f, 1f, 0.6f), 0.4f, 0f, emissive: true);

            car.Root = SceneBuildUtil.Go("Vehicle");
            car.Root.transform.position = env.VehicleStart.position;
            Transform t = car.Root.transform;

            car.Body = car.Root.AddComponent<Rigidbody>();
            car.Body.mass = 1200f;
            car.Body.linearDamping = 0.04f;
            car.Body.angularDamping = 2.2f;
            car.Body.interpolation = RigidbodyInterpolation.Interpolate;
            car.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // ---- chassis collider ---------------------------------------------
            // One box on the root rather than colliders on the visual parts: the
            // wheel colliders handle the ground, and a single convex body makes
            // collision reporting predictable.
            var chassis = car.Root.AddComponent<BoxCollider>();
            chassis.center = new Vector3(0f, 0.25f, 0f);
            chassis.size = new Vector3(1.78f, 0.95f, 4.3f);

            car.Controller = car.Root.AddComponent<VehicleController>();
            car.EndGuard = car.Root.AddComponent<RoadEndGuard>();

            // ---- body shell ---------------------------------------------------

            GameObject shell = SceneBuildUtil.Go("Body", t);
            SceneBuildUtil.Box("LowerBody", shell.transform, new Vector3(0f, 0.08f, 0f),
                               new Vector3(1.78f, 0.62f, 4.3f), bodyMat);
            SceneBuildUtil.Box("Bonnet", shell.transform, new Vector3(0f, 0.40f, 1.45f),
                               new Vector3(1.70f, 0.18f, 1.4f), bodyMat);
            SceneBuildUtil.Box("Boot", shell.transform, new Vector3(0f, 0.40f, -1.70f),
                               new Vector3(1.70f, 0.20f, 0.9f), bodyMat);
            SceneBuildUtil.Box("Roof", shell.transform, new Vector3(0f, 1.32f, -0.15f),
                               new Vector3(1.62f, 0.06f, 2.0f), bodyMat);

            // A-pillars and B-pillars. These matter more than they look: a rigid
            // near-field frame around the driver's view is the strongest comfort
            // mitigation available once passthrough is gone.
            SceneBuildUtil.Box("PillarAL", shell.transform, new Vector3(-0.78f, 0.95f, 0.80f),
                               new Vector3(0.09f, 0.80f, 0.12f), bodyMat);
            SceneBuildUtil.Box("PillarAR", shell.transform, new Vector3(0.78f, 0.95f, 0.80f),
                               new Vector3(0.09f, 0.80f, 0.12f), bodyMat);
            SceneBuildUtil.Box("PillarBL", shell.transform, new Vector3(-0.80f, 0.95f, -1.05f),
                               new Vector3(0.09f, 0.80f, 0.14f), bodyMat);
            SceneBuildUtil.Box("PillarBR", shell.transform, new Vector3(0.80f, 0.95f, -1.05f),
                               new Vector3(0.09f, 0.80f, 0.14f), bodyMat);
            // Doors stop at a window sill of y 0.62, not at 0.86. At 0.86 the
            // door skin was a solid wall right across the driver's line of sight
            // to the door mirrors: the sightline from the eye to the left mirror
            // crosses the door plane at y 0.71, which was inside the old panel.
            // The mirrors were being rendered correctly the whole time and
            // simply could not be seen from the seat.
            SceneBuildUtil.Box("DoorL", shell.transform, new Vector3(-0.86f, 0.48f, -0.15f),
                               new Vector3(0.07f, 0.28f, 2.0f), bodyMat);
            SceneBuildUtil.Box("DoorR", shell.transform, new Vector3(0.86f, 0.48f, -0.15f),
                               new Vector3(0.07f, 0.28f, 2.0f), bodyMat);

            // No windscreen pane. A transparent pane would sort against the
            // world-space UI canvas that sits just beyond it, and the menus
            // flickering behind glass is worse than an open front.
            SceneBuildUtil.Box("WindscreenFrameTop", shell.transform,
                               new Vector3(0f, 1.30f, 0.84f),
                               new Vector3(1.64f, 0.08f, 0.12f), bodyMat);

            // ---- wheels ---------------------------------------------------------

            GameObject colliders = SceneBuildUtil.Go("WheelColliders", t);
            GameObject meshes = SceneBuildUtil.Go("WheelMeshes", t);

            WheelCollider fl = MakeWheel("WheelFL", colliders.transform, new Vector3(-0.78f, -0.10f, 1.35f));
            WheelCollider fr = MakeWheel("WheelFR", colliders.transform, new Vector3(0.78f, -0.10f, 1.35f));
            WheelCollider rl = MakeWheel("WheelRL", colliders.transform, new Vector3(-0.78f, -0.10f, -1.35f));
            WheelCollider rr = MakeWheel("WheelRR", colliders.transform, new Vector3(0.78f, -0.10f, -1.35f));

            Transform mfl = MakeWheelMesh("MeshFL", meshes.transform, tyre, rim);
            Transform mfr = MakeWheelMesh("MeshFR", meshes.transform, tyre, rim);
            Transform mrl = MakeWheelMesh("MeshRL", meshes.transform, tyre, rim);
            Transform mrr = MakeWheelMesh("MeshRR", meshes.transform, tyre, rim);

            car.Controller.Configure(fl, fr, rl, rr, mfl, mfr, mrl, mrr);

            // ---- cabin -----------------------------------------------------------

            GameObject cabinGo = SceneBuildUtil.Go("Cabin", t);
            car.CabinRoot = cabinGo.transform;
            car.Cabin = cabinGo.AddComponent<CabinConfigurator>();

            SceneBuildUtil.Box("Floor", car.CabinRoot, new Vector3(0f, 0.26f, -0.1f),
                               new Vector3(1.60f, 0.05f, 2.2f), interior);
            // Firewall, NOT a bulkhead. This was originally built 0.85 m tall and
            // centred at SeatY, which put a solid wall across the whole forward
            // view 1.1 m from the driver's eyes — the single thing that made the
            // cabin unusable. It must stay entirely below the dashboard top
            // (SeatY - 0.22 + 0.15 = 0.71 in cabin space); the span below is
            // 0.26 → 0.58, so it is occluded by the dash from the seat and never
            // crosses the horizon. ForwardViewIsClear() in Validate() enforces this.
            SceneBuildUtil.Box("Firewall", car.CabinRoot, new Vector3(0f, 0.42f, 0.95f),
                               new Vector3(1.60f, 0.32f, 0.08f), interior);
            SceneBuildUtil.Box("RearSeatBack", car.CabinRoot, new Vector3(0f, 0.72f, -1.25f),
                               new Vector3(1.55f, 0.75f, 0.14f), interior);

            // Seat. Authored on the LEFT, mirrored to the right for Australia and
            // India by CabinConfigurator.
            GameObject seat = SceneBuildUtil.Go("SeatAnchor", car.CabinRoot,
                                                new Vector3(SeatX, SeatY, SeatZ));
            car.SeatAnchor = seat.transform;

            SceneBuildUtil.Box("SeatBase", car.SeatAnchor, new Vector3(0f, -0.44f, 0.04f),
                               new Vector3(0.52f, 0.12f, 0.52f), interior);
            SceneBuildUtil.Box("SeatBack", car.SeatAnchor, new Vector3(0f, -0.14f, -0.26f),
                               new Vector3(0.52f, 0.62f, 0.10f), interior);

            // Steering column. Its own tilt is what VRSteeringWheel measures hand
            // angles against, so the wheel must be a child of this and this must
            // not rotate with the steering.
            GameObject column = SceneBuildUtil.Go("SteeringColumn", car.CabinRoot,
                                                  new Vector3(SeatX, SeatY - 0.30f, SeatZ + 0.36f));
            column.transform.localRotation =
                Quaternion.LookRotation(new Vector3(0f, 0.342f, -0.940f), Vector3.up);
            car.SteeringColumn = column.transform;

            car.Wheel = BuildSteeringWheel(column.transform, rim).transform;

            GameObject pedals = SceneBuildUtil.Go("PedalBox", car.CabinRoot,
                                                  new Vector3(SeatX, SeatY - 0.62f, SeatZ + 0.62f));
            SceneBuildUtil.Box("Accelerator", pedals.transform, new Vector3(0.07f, 0f, 0f),
                               new Vector3(0.08f, 0.02f, 0.16f), interior);
            SceneBuildUtil.Box("Brake", pedals.transform, new Vector3(-0.07f, 0.01f, 0f),
                               new Vector3(0.10f, 0.02f, 0.16f), interior);

            GameObject stalk = SceneBuildUtil.Go("IndicatorStalk", car.CabinRoot,
                                                 new Vector3(SeatX - 0.22f, SeatY - 0.28f, SeatZ + 0.30f));
            SceneBuildUtil.Box("Stalk", stalk.transform, Vector3.zero,
                               new Vector3(0.14f, 0.02f, 0.02f), interior);

            // The Dashboard object is the HUD ANCHOR and mirrors with the seat,
            // so the instrument readout stays in front of whoever is driving.
            GameObject dashboard = SceneBuildUtil.Go("Dashboard", car.CabinRoot,
                                                     new Vector3(SeatX, SeatY - 0.22f, SeatZ + 0.70f));
            car.Dashboard = dashboard.transform;

            // The fascia itself is a separate child of the CABIN, centred on the
            // car's centreline and NOT mirrored. As a child of the seat-relative
            // anchor it was a 1.50 m slab centred on the driver, so it stuck out
            // to x -1.13 — past the door skin and across the line of sight to the
            // left door mirror. A dashboard spans the cabin; it does not follow
            // the driver across it.
            SceneBuildUtil.Box("DashFascia", car.CabinRoot,
                               new Vector3(0f, SeatY - 0.22f, SeatZ + 0.70f),
                               new Vector3(1.55f, 0.30f, 0.22f), dash);

            // ---- mirrors ---------------------------------------------------------
            // Not mirrored by drive side: both door mirrors exist on a real car
            // whichever side the driver sits.

            // Each mirror head is 0.26 wide, so placing the door mirrors at
            // x ±0.99 puts their inboard edge at ±0.86, which is inside the door
            // skin's outer face at ±0.895 — the head meets the bodywork and needs
            // no separate mounting arm. An arm was worse than nothing here: the
            // head sweeps a 0.14 m radius when it aims at the driver, which is
            // further than the 0.125 m gap to the door, so any arm long enough to
            // be seen also crossed the glass.
            //
            // 0.70 m puts the head's centre just below the 0.78 m eye line and
            // ~39° off the forward axis, which is where a door mirror belongs.
            car.MirrorLeft = BuildMirror("MirrorLeft", car.CabinRoot,
                                         new Vector3(-0.99f, 0.72f, 0.60f),
                                         MirrorView.Facing.RearLeft, car.SeatAnchor,
                                         interior, glass);
            car.MirrorRight = BuildMirror("MirrorRight", car.CabinRoot,
                                          new Vector3(0.99f, 0.72f, 0.60f),
                                          MirrorView.Facing.RearRight, car.SeatAnchor,
                                          interior, glass);
            // Centre mirror: forward to z 0.74, under the windscreen header and
            // touching the roof's underside at 1.29, so it reads as mounted. The
            // extra 0.14 m of reach drops the glance angle from 29° to 25°.
            car.MirrorCentre = BuildMirror("MirrorCentre", car.CabinRoot,
                                           new Vector3(0f, 1.19f, 0.74f),
                                           MirrorView.Facing.RearCentre, car.SeatAnchor,
                                           interior, glass);

            // A FRAME, not a plate. At 0.24 x 0.17 it was within a millimetre of
            // the 0.23 x 0.16 glass and sat at the same depth, so on the priority
            // mirror — the left one under the Australian profile — it covered the
            // reflection completely and the mirror showed a flat coloured panel.
            // Bigger than the housing (0.26 x 0.19) and set behind the glass, it
            // reads as a glowing border around the mirror instead.
            car.PriorityHighlight = SceneBuildUtil.Box("PriorityHighlight", car.CabinRoot,
                                                       Vector3.zero,
                                                       new Vector3(0.31f, 0.24f, 0.008f),
                                                       highlight);
            car.PriorityHighlight.SetActive(false);

            car.Cabin.Configure(car.SeatAnchor, car.SteeringColumn, pedals.transform,
                                stalk.transform, car.Dashboard,
                                car.MirrorLeft, car.MirrorRight, car.MirrorCentre,
                                car.PriorityHighlight);

            Line("Vehicle: 4.3 m chassis, 4 wheel colliders, cabin with floor, " +
                 "pillars, doors, dashboard, pedal box, indicator stalk and three " +
                 $"working mirrors; seat authored at x={SeatX:F2} (left-hand drive " +
                 "baseline), eye height " +
                 $"{(env.VehicleStart.position.y + SeatY):F2} m above the road");

            return car;
        }

        private static WheelCollider MakeWheel(string name, Transform parent, Vector3 position)
        {
            GameObject go = SceneBuildUtil.Go(name, parent, position);
            var w = go.AddComponent<WheelCollider>();

            w.radius = 0.34f;
            w.suspensionDistance = 0.20f;
            w.wheelDampingRate = 0.3f;
            w.forceAppPointDistance = 0.1f;

            JointSpring spring = w.suspensionSpring;
            spring.spring = 38000f;
            spring.damper = 4800f;
            spring.targetPosition = 0.5f;
            w.suspensionSpring = spring;

            WheelFrictionCurve forward = w.forwardFriction;
            forward.extremumSlip = 0.4f;
            forward.extremumValue = 1f;
            forward.asymptoteSlip = 0.8f;
            forward.asymptoteValue = 0.6f;
            forward.stiffness = 1.6f;
            w.forwardFriction = forward;

            WheelFrictionCurve sideways = w.sidewaysFriction;
            sideways.extremumSlip = 0.25f;
            sideways.extremumValue = 1f;
            sideways.asymptoteSlip = 0.55f;
            sideways.asymptoteValue = 0.75f;
            // Higher than forward stiffness: a car that slides sideways under
            // gentle steering is unpleasant in VR and makes lane_offset measure
            // the tyre model rather than the driver.
            sideways.stiffness = 2.1f;
            w.sidewaysFriction = sideways;

            return w;
        }

        private static Transform MakeWheelMesh(string name, Transform parent,
                                               Material tyre, Material rim)
        {
            GameObject go = SceneBuildUtil.Go(name, parent);

            // A Unity cylinder is 2 units tall and 1 wide, with its axis along Y.
            // Rotated 90° about Z it becomes a wheel on the X axis.
            GameObject outer = SceneBuildUtil.Cyl("Tyre", go.transform, Vector3.zero,
                                                  new Vector3(0.68f, 0.11f, 0.68f), tyre);
            outer.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            GameObject hub = SceneBuildUtil.Cyl("Hub", go.transform, Vector3.zero,
                                                new Vector3(0.40f, 0.12f, 0.40f), rim);
            hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            return go.transform;
        }

        /// <summary>
        /// A steering wheel built as a ring of segments plus spokes.
        ///
        /// A Unity cylinder is SOLID. An earlier build used one as the wheel and
        /// the driver spent the session looking through a 34 cm porthole, which is
        /// what "the wheel fills my whole view" meant. A ring has to be built from
        /// segments.
        /// </summary>
        private static GameObject BuildSteeringWheel(Transform column, Material rimMat)
        {
            Material grip = SceneBuildUtil.Mat("WheelGrip", new Color(0.11f, 0.11f, 0.12f), 0.35f);
            Material spokeMat = SceneBuildUtil.Mat("WheelSpoke", new Color(0.18f, 0.18f, 0.20f), 0.45f);

            GameObject wheel = SceneBuildUtil.Go("Wheel", column);

            const int segments = 24;
            float segmentLength = 2f * Mathf.PI * WheelRadius / segments * 1.08f;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * 360f / segments;
                float rad = angle * Mathf.Deg2Rad;

                var pos = new Vector3(Mathf.Sin(rad) * WheelRadius,
                                      Mathf.Cos(rad) * WheelRadius, 0f);

                GameObject seg = SceneBuildUtil.Box($"Rim_{i}", wheel.transform, pos,
                                                    new Vector3(0.028f, segmentLength, 0.028f),
                                                    grip);
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }

            // Three spokes, like a real wheel, so the driver can tell at a glance
            // how far the wheel is turned.
            for (int i = 0; i < 3; i++)
            {
                float angle = 90f + i * 120f;
                float rad = angle * Mathf.Deg2Rad;

                var pos = new Vector3(Mathf.Sin(rad) * WheelRadius * 0.5f,
                                      Mathf.Cos(rad) * WheelRadius * 0.5f, 0f);

                GameObject spoke = SceneBuildUtil.Box($"Spoke_{i}", wheel.transform, pos,
                                                      new Vector3(0.022f, WheelRadius, 0.016f),
                                                      spokeMat);
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }

            SceneBuildUtil.Cyl("Hub", wheel.transform, Vector3.zero,
                               new Vector3(0.075f, 0.012f, 0.075f), rimMat)
                          .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            return wheel;
        }

        private static Transform BuildMirror(string name, Transform cabin, Vector3 position,
                                             MirrorView.Facing facing, Transform seat,
                                             Material housing, Material glassMat)
        {
            GameObject go = SceneBuildUtil.Go(name, cabin, position);

            // The HEAD is the part that aims. Housing and glass are both children
            // of it, so they rotate together and stay coplanar. Rotating the glass
            // on its own — which is what this did originally — buries half of it
            // in the housing and pokes the other half out the front.
            //
            // Inside the head, +Z points at the driver: the glass sits at the
            // origin and the housing goes BEHIND it at local -Z.
            GameObject head = SceneBuildUtil.Go("Head", go.transform);

            SceneBuildUtil.Box("Housing", head.transform, new Vector3(0f, 0f, -0.025f),
                               new Vector3(0.26f, 0.19f, 0.05f), housing);

            // A box, not a quad: a quad is single-sided and which side is visible
            // depends on its rotation, so a mirror built from one is invisible
            // half the time.
            GameObject glass = SceneBuildUtil.Box("Glass", head.transform, Vector3.zero,
                                                  new Vector3(0.23f, 0.16f, 0.012f), glassMat);

            // Angle the head toward the driver's eyes so the reflection is
            // actually visible from the seat.
            Vector3 toSeat = seat.position - head.transform.position;
            if (toSeat.sqrMagnitude > 0.0001f)
                head.transform.rotation = Quaternion.LookRotation(toSeat, Vector3.up);

            GameObject camGo = SceneBuildUtil.Go("MirrorCamera", go.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;

            // Rear-facing, splayed outward for the door mirrors.
            Vector3 look = facing switch
            {
                MirrorView.Facing.RearLeft => new Vector3(-0.38f, -0.04f, -0.92f),
                MirrorView.Facing.RearRight => new Vector3(0.38f, -0.04f, -0.92f),
                _ => new Vector3(0f, -0.03f, -1f)
            };
            camGo.transform.localRotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            var view = camGo.AddComponent<MirrorView>();
            // The camera is a child of the mirror root, NOT of the head, so it
            // keeps looking rearward whatever angle the head is aimed at.
            view.Configure(facing, glass.GetComponent<Renderer>(), head.transform);

            return go.transform;
        }

        // =====================================================================
        //  XR rig
        // =====================================================================

        private class Rig
        {
            public GameObject Root;
            public XROrigin Origin;
            public Transform CameraOffset;
            public Camera Head;
            public VRSeatRecenter Recenter;
            public VRUIPointer Pointer;
            public VRSteeringWheel Wheel;
            public VehicleInputAdapter Input;
            public XRControllerAnchor LeftAnchor, RightAnchor;
            public LineRenderer LeftLine, RightLine;
            public GameObject ReticleRoot;
            public Image ReticleFill;
            public ComfortSettings Comfort;
        }

        private static Rig BuildXRRig(Car car)
        {
            var rig = new Rig();

            Material controllerMat = SceneBuildUtil.Mat("ControllerBody",
                                                        new Color(0.14f, 0.14f, 0.16f), 0.4f);

            // Parented to the seat anchor. This is what makes the driver move with
            // the car — and it is why VRSeatRecenter can do all its maths in
            // seat-local space, where the car's own motion cancels out exactly.
            rig.Root = SceneBuildUtil.Go("XR Origin", car.SeatAnchor);
            Transform t = rig.Root.transform;

            GameObject offset = SceneBuildUtil.Go("Camera Offset", t);
            rig.CameraOffset = offset.transform;

            GameObject headGo = SceneBuildUtil.Go("Main Camera", offset.transform);
            headGo.tag = "MainCamera";

            rig.Head = headGo.AddComponent<Camera>();
            rig.Head.nearClipPlane = 0.04f;    // the wheel is ~0.35 m from the eyes
            rig.Head.farClipPlane = 600f;
            rig.Head.clearFlags = CameraClearFlags.SolidColor;
            rig.Head.backgroundColor = new Color(0.58f, 0.68f, 0.82f);
            rig.Head.allowHDR = false;         // HDR costs bandwidth and buys nothing here
            rig.Head.allowMSAA = true;

            var camData = headGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderType = CameraRenderType.Base;
            camData.renderPostProcessing = false;
            camData.renderShadows = true;
            camData.allowXRRendering = true;

            headGo.AddComponent<AudioListener>();

            // The head pose driver. See XRHeadAnchor for why this is not
            // TrackedPoseDriver: that component reads its pose through serialized
            // InputActionProperty fields that a script-built scene cannot verify,
            // and does nothing at all — silently — when they are wrong.
            //
            // XROrigin.Awake logs one warning about the missing TrackedPoseDriver.
            // It is expected and harmless: the camera IS driven, by this
            // component, which logs the pose it is applying.
            rig.Head.gameObject.AddComponent<XRHeadAnchor>();

            // One authoritative XR Origin, in Device tracking mode: a seated
            // driver wants head pose relative to where tracking started, not
            // height above the real floor. Field names and the enum order below
            // were read from the installed com.unity.xr.core-utils source, not
            // assumed: m_RequestedTrackingOriginMode is
            // { NotSpecified, Device, Floor, Unbounded }, so Device is 1.
            rig.Origin = rig.Root.AddComponent<XROrigin>();
            SceneBuildUtil.Wire(rig.Origin, "m_Camera", rig.Head);
            SceneBuildUtil.Wire(rig.Origin, "m_CameraFloorOffsetObject", offset);
            SceneBuildUtil.Wire(rig.Origin, "m_OriginBaseGameObject", rig.Root);
            SceneBuildUtil.Wire(rig.Origin, "m_RequestedTrackingOriginMode", 1);
            SceneBuildUtil.Wire(rig.Origin, "m_CameraYOffset", 0f);

            // ---- controllers ---------------------------------------------------

            rig.LeftAnchor = BuildController("LeftController", offset.transform, Hand.Left,
                                             controllerMat, out rig.LeftLine);
            rig.RightAnchor = BuildController("RightController", offset.transform, Hand.Right,
                                              controllerMat, out rig.RightLine);

            // ---- gaze reticle ---------------------------------------------------

            // Non-interactive: the reticle must never be something a ray can land
            // on, or it blocks the very ray whose progress it is drawing.
            Canvas reticleCanvas = SceneBuildUtil.WorldCanvas(
                "GazeReticle", offset.transform, Vector3.zero, Quaternion.identity,
                new Vector2(64f, 64f), 0.0009f, rig.Head, interactive: false);

            GameObject reticleCanvasGo = reticleCanvas.gameObject;
            rig.ReticleRoot = reticleCanvasGo;

            GameObject fillGo = SceneBuildUtil.UiGo("DwellFill", reticleCanvasGo.transform);
            SceneBuildUtil.Stretch((RectTransform)fillGo.transform);

            rig.ReticleFill = fillGo.AddComponent<Image>();
            rig.ReticleFill.sprite = SceneBuildUtil.KnobSprite();   // radial fill needs a sprite
            rig.ReticleFill.color = new Color(0.45f, 1f, 0.80f, 0.85f);
            rig.ReticleFill.type = Image.Type.Filled;
            rig.ReticleFill.fillMethod = Image.FillMethod.Radial360;
            rig.ReticleFill.fillAmount = 0f;
            rig.ReticleFill.raycastTarget = false;

            reticleCanvasGo.SetActive(false);

            // ---- rig components --------------------------------------------------

            rig.Recenter = rig.Root.AddComponent<VRSeatRecenter>();
            rig.Recenter.Configure(t, rig.Head, car.SeatAnchor);

            rig.Pointer = rig.Root.AddComponent<VRUIPointer>();

            GameObject inputGo = SceneBuildUtil.Go("Input", t);
            rig.Input = inputGo.AddComponent<VehicleInputAdapter>();
            rig.Wheel = inputGo.AddComponent<VRSteeringWheel>();
            KeyboardInputProvider keyboard = inputGo.AddComponent<KeyboardInputProvider>();

            rig.Wheel.Configure(car.Wheel, rig.LeftAnchor.transform, rig.RightAnchor.transform);

            // Through Wire, not SetProviders: a list mutated directly in edit mode
            // is not necessarily serialised, and an input adapter that finds no
            // providers at runtime is a car that does not move.
            SceneBuildUtil.Wire(rig.Input, "providerRefs",
                                new List<MonoBehaviour> { rig.Wheel, keyboard });

            // ---- comfort ----------------------------------------------------------

            rig.Comfort = rig.Root.AddComponent<ComfortSettings>();
            GameObject vignette = BuildVignette(rig.Head);
            rig.Comfort.Configure(vignette, null);

            Line("XR rig: one XR Origin in Device tracking mode, parented to the " +
                 "seat anchor; no locomotion components; camera near plane 0.04 m; " +
                 "two controller anchors with laser lines; gaze reticle for a " +
                 "controller-free fallback");

            return rig;
        }

        private static XRControllerAnchor BuildController(string name, Transform parent,
                                                          Hand hand, Material material,
                                                          out LineRenderer line)
        {
            GameObject go = SceneBuildUtil.Go(name, parent);
            var anchor = go.AddComponent<XRControllerAnchor>();

            GameObject visual = SceneBuildUtil.Go("Visual", go.transform);
            SceneBuildUtil.Box("Grip", visual.transform, new Vector3(0f, -0.03f, -0.04f),
                               new Vector3(0.035f, 0.035f, 0.12f), material);
            SceneBuildUtil.Cyl("Ring", visual.transform, new Vector3(0f, 0.015f, 0.02f),
                               new Vector3(0.07f, 0.008f, 0.07f), material);

            GameObject lineGo = SceneBuildUtil.Go("Pointer", visual.transform);
            line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.004f;
            line.numCapVertices = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = SceneBuildUtil.Mat("PointerLine",
                                                     new Color(0.45f, 0.70f, 1f), 0f, 0f,
                                                     emissive: true);

            anchor.Configure(hand, visual);

            return anchor;
        }

        /// <summary>
        /// A head-locked vignette: a ring of dark quads that closes in slightly
        /// during turns, where peripheral optical flow is strongest.
        /// </summary>
        private static GameObject BuildVignette(Camera head)
        {
            GameObject go = SceneBuildUtil.Go("TurnVignette", head.transform,
                                              new Vector3(0f, 0f, 0.25f));

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            go.layer = LayerMask.NameToLayer("UI");

            // No GraphicRaycaster, on purpose: this canvas is 25 cm from the eyes
            // and would otherwise intercept every ray before it reached anything.
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(900f, 700f);
            rect.localScale = Vector3.one * 0.0007f;

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var colour = new Color(0f, 0f, 0f, 0.55f);
            MakeVignetteBar("Left", go.transform, true, colour);
            MakeVignetteBar("Right", go.transform, false, colour);

            go.SetActive(false);
            return go;
        }

        private static void MakeVignetteBar(string name, Transform parent, bool leftEdge,
                                            Color colour)
        {
            GameObject go = SceneBuildUtil.UiGo(name, parent);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(leftEdge ? 0f : 1f, 0f);
            rect.anchorMax = new Vector2(leftEdge ? 0f : 1f, 1f);
            rect.pivot = new Vector2(leftEdge ? 0f : 1f, 0.5f);
            rect.sizeDelta = new Vector2(110f, 0f);
            rect.anchoredPosition = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
        }

        // =====================================================================
        //  traffic
        // =====================================================================

        private class Traffic
        {
            public NPCVehicleController Lead;
            public NPCVehicleController Cross;
            public NPCPathFollower Pedestrian;
            public NPCPathFollower Cyclist;
        }

        private static Traffic BuildTraffic(Env env)
        {
            var traffic = new Traffic();
            GameObject root = SceneBuildUtil.Go("Traffic");

            Material npcMat = SceneBuildUtil.Mat("NPCCar", new Color(0.58f, 0.18f, 0.18f), 0.5f, 0.3f);
            Material crossMat = SceneBuildUtil.Mat("CrossCar", new Color(0.78f, 0.70f, 0.22f), 0.5f, 0.3f);
            Material person = SceneBuildUtil.Mat("Pedestrian", new Color(0.86f, 0.55f, 0.25f), 0.1f);
            Material bike = SceneBuildUtil.Mat("Cyclist", new Color(0.25f, 0.70f, 0.85f), 0.2f);

            traffic.Lead = BuildNpcCar("LeadVehicle", root.transform, npcMat,
                                       env.LeadPath, env.LeadWaypoints, 40f);
            traffic.Cross = BuildNpcCar("CrossVehicle", root.transform, crossMat,
                                        env.CrossPath, env.CrossWaypoints, 38f);

            // ---- pedestrian -----------------------------------------------------

            GameObject ped = SceneBuildUtil.Go("Pedestrian", root.transform);
            SceneBuildUtil.Box("Torso", ped.transform, new Vector3(0f, 0.25f, 0f),
                               new Vector3(0.38f, 0.62f, 0.24f), person);
            SceneBuildUtil.Box("Head", ped.transform, new Vector3(0f, 0.68f, 0f),
                               new Vector3(0.21f, 0.23f, 0.21f), person);
            SceneBuildUtil.Box("LegL", ped.transform, new Vector3(-0.11f, -0.35f, 0f),
                               new Vector3(0.13f, 0.58f, 0.15f), person);
            SceneBuildUtil.Box("LegR", ped.transform, new Vector3(0.11f, -0.35f, 0f),
                               new Vector3(0.13f, 0.58f, 0.15f), person);

            var pedCollider = ped.AddComponent<CapsuleCollider>();
            pedCollider.height = 1.75f;
            pedCollider.radius = 0.25f;
            pedCollider.center = new Vector3(0f, 0.05f, 0f);

            ped.transform.position = env.PedestrianStart.position;
            traffic.Pedestrian = ped.AddComponent<NPCPathFollower>();
            SceneBuildUtil.Wire(traffic.Pedestrian, "startPoint", env.PedestrianStart);
            SceneBuildUtil.Wire(traffic.Pedestrian, "endPoint", env.PedestrianEnd);
            SceneBuildUtil.Wire(traffic.Pedestrian, "speedMetresPerSec", 1.8f);

            // ---- cyclist --------------------------------------------------------

            // A longer run than the old 700 -> 900, so the cyclist is still riding
            // by the time the driver has overtaken and the scenario has resolved,
            // rather than reaching the end of its path and freezing in the lane.
            GameObject cyclistStart = SceneBuildUtil.Go("CyclistStart", env.CyclistPathRoot,
                                                        new Vector3(0f, 0.9f, 640f));
            GameObject cyclistEnd = SceneBuildUtil.Go("CyclistEnd", env.CyclistPathRoot,
                                                      new Vector3(0f, 0.9f, 1150f));

            GameObject cyc = SceneBuildUtil.Go("Cyclist", root.transform);
            SceneBuildUtil.Box("Rider", cyc.transform, new Vector3(0f, 0.30f, 0f),
                               new Vector3(0.34f, 0.60f, 0.26f), bike);
            SceneBuildUtil.Box("Head", cyc.transform, new Vector3(0f, 0.72f, 0f),
                               new Vector3(0.20f, 0.22f, 0.22f), bike);
            SceneBuildUtil.Cyl("WheelFront", cyc.transform, new Vector3(0f, -0.35f, 0.52f),
                               new Vector3(0.66f, 0.03f, 0.66f), SceneBuildUtil.Mat(
                                   "BikeWheel", new Color(0.1f, 0.1f, 0.1f), 0.2f))
                          .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            SceneBuildUtil.Cyl("WheelRear", cyc.transform, new Vector3(0f, -0.35f, -0.42f),
                               new Vector3(0.66f, 0.03f, 0.66f), SceneBuildUtil.Mat(
                                   "BikeWheel", new Color(0.1f, 0.1f, 0.1f), 0.2f))
                          .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var cycCollider = cyc.AddComponent<CapsuleCollider>();
            cycCollider.height = 1.8f;
            cycCollider.radius = 0.3f;

            cyc.transform.position = cyclistStart.transform.position;
            traffic.Cyclist = cyc.AddComponent<NPCPathFollower>();
            SceneBuildUtil.Wire(traffic.Cyclist, "startPoint", cyclistStart.transform);
            SceneBuildUtil.Wire(traffic.Cyclist, "endPoint", cyclistEnd.transform);
            SceneBuildUtil.Wire(traffic.Cyclist, "speedMetresPerSec", 4.0f);

            SceneBuildUtil.Wire(env.GapDetector, "crossTraffic",
                                new List<NPCVehicleController> { traffic.Cross });
            SceneBuildUtil.Wire(env.GapDetector, "conflictPoint", env.ConflictPoint);

            Line("Traffic: lead vehicle, cross-traffic vehicle at the intersection, " +
                 "one pedestrian, one cyclist — all kinematic, all collidable");

            return traffic;
        }

        /// <summary>
        /// An NPC car, placed on its own route rather than at the scene origin.
        ///
        /// The waypoints are passed in rather than read back through
        /// TrafficPath.GetWaypoint, because that reads a serialized list this
        /// builder has only just written — edit-time serialization round-trips
        /// are exactly the sort of thing that works today and silently returns an
        /// empty list after a Unity upgrade.
        /// </summary>
        private static NPCVehicleController BuildNpcCar(string name, Transform parent,
                                                        Material material, TrafficPath path,
                                                        List<Transform> waypoints,
                                                        float cruiseKph)
        {
            GameObject go = SceneBuildUtil.Go(name, parent);

            if (waypoints != null && waypoints.Count > 0)
            {
                go.transform.position = waypoints[0].position;

                if (waypoints.Count > 1)
                {
                    Vector3 forward = waypoints[1].position - waypoints[0].position;
                    forward.y = 0f;
                    if (forward.sqrMagnitude > 0.001f)
                        go.transform.rotation =
                            Quaternion.LookRotation(forward.normalized, Vector3.up);
                }
            }

            SceneBuildUtil.Box("Body", go.transform, new Vector3(0f, 0.35f, 0f),
                               new Vector3(1.74f, 0.70f, 4.2f), material);
            SceneBuildUtil.Box("Roof", go.transform, new Vector3(0f, 0.86f, -0.25f),
                               new Vector3(1.60f, 0.45f, 2.1f), material);

            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(1.78f, 1.3f, 4.3f);

            var controller = go.AddComponent<NPCVehicleController>();
            controller.Configure(path, cruiseKph);

            // Obstacle layers: everything except the UI layer. A ray that hits a
            // menu panel is not traffic.
            SceneBuildUtil.Wire(controller, "obstacleLayers",
                                ~(1 << LayerMask.NameToLayer("UI")));

            return controller;
        }

        // =====================================================================
        //  scenarios
        // =====================================================================

        private class Hazards
        {
            public ScenarioManager Manager;
            public PedestrianScenario Pedestrian;
            public CyclistScenario Cyclist;
            public LeadBrakeScenario LeadBrake;
        }

        private static Hazards BuildScenarios(Env env, Traffic traffic)
        {
            var h = new Hazards();

            GameObject root = SceneBuildUtil.Go("Scenarios");
            h.Manager = root.AddComponent<ScenarioManager>();

            GameObject pedGo = SceneBuildUtil.Go("PedestrianStepOut", root.transform);
            h.Pedestrian = pedGo.AddComponent<PedestrianScenario>();
            h.Pedestrian.Configure(traffic.Pedestrian, env.PedestrianStart);

            GameObject cycGo = SceneBuildUtil.Go("CyclistBlindSpot", root.transform);
            h.Cyclist = cycGo.AddComponent<CyclistScenario>();
            h.Cyclist.Configure(traffic.Cyclist, env.ManoeuvreZone);

            GameObject leadGo = SceneBuildUtil.Go("LeadVehicleBrake", root.transform);
            h.LeadBrake = leadGo.AddComponent<LeadBrakeScenario>();
            h.LeadBrake.Configure(traffic.Lead);

            // Two hazards per three-minute drive. Three is a shooting gallery: by
            // the third the driver is braced and braced reactions are not the
            // reflex this project measures.
            h.Manager.Configure(
                new HazardScenarioBase[] { h.Pedestrian, h.Cyclist, h.LeadBrake }, 2);

            Line("Scenarios: pedestrian step-out, cyclist blind spot, lead-vehicle " +
                 "brake; 2 of 3 per session, chosen adaptively, 20 s minimum apart");

            return h;
        }

        // =====================================================================
        //  UI
        // =====================================================================

        private class Ui
        {
            public GameObject Root;
            public UIManager Manager;
            public PostDriveReportPanel Report;
            public DrivingHUD Hud;
            public AlertManager Alerts;
            public LiveAlertTriggers AlertTriggers;
            public GameObject CountryCardPrefab;
            public Transform CardContainer;
            public MenuStaging Staging;
        }

        private static readonly Color PanelBg = new Color(0.07f, 0.09f, 0.13f, 0.93f);
        private static readonly Color Accent = new Color(0.18f, 0.52f, 0.86f, 1f);
        private static readonly Color AccentWarm = new Color(0.82f, 0.45f, 0.18f, 1f);
        private static readonly Color Ink = new Color(0.94f, 0.96f, 0.99f, 1f);
        private static readonly Color InkDim = new Color(0.70f, 0.75f, 0.82f, 1f);

        private static Ui BuildUI(Car car, Rig rig)
        {
            var ui = new Ui { Root = SceneBuildUtil.Go("UI") };
            Transform t = ui.Root.transform;

            // EventSystem with NO input module, on purpose. VRUIPointer invokes
            // button handlers itself and raycasts through EventSystem.RaycastAll,
            // which needs no module. The Input System's UI module needs a
            // configured action asset to do anything, and the legacy module logs
            // an error in a project with Active Input Handling set to Both.
            GameObject eventSystem = SceneBuildUtil.Go("EventSystem", t);
            eventSystem.AddComponent<EventSystem>();

            // These four components live on the always-active UI root, each with a
            // reference to the panel it drives. A component inside a deactivated
            // hierarchy never receives OnEnable, so one that starts hidden and
            // holds its own event subscription never learns the session changed.
            ui.Manager = ui.Root.AddComponent<UIManager>();
            ui.Report = ui.Root.AddComponent<PostDriveReportPanel>();
            ui.Hud = ui.Root.AddComponent<DrivingHUD>();
            ui.Alerts = ui.Root.AddComponent<AlertManager>();
            ui.AlertTriggers = ui.Root.AddComponent<LiveAlertTriggers>();

            // ---- menu canvas, ahead of the driver -------------------------------
            //
            // Parented to the SEAT ANCHOR, not the cabin. On the cabin it would
            // sit on the car's centreline, which is 38 cm to the driver's side —
            // and the wrong side depending on the country. On the seat it is
            // always straight ahead and it mirrors with the drive side for free.
            //
            // 1.22 m from the eyes, 1.30 × 0.90 m: about 56° wide, which fills
            // the comfortable central field without the driver having to turn
            // their head to read a corner of it.

            // Authored at the HALL placement — 2.6 m ahead, car not drawn.
            // MenuStaging moves and rescales it to the CABIN placement for the
            // two phases where the driver is meant to be sitting in the car.
            //
            // The old placement (0, 0.04, 1.22) at scale 0.00105 spanned -18.6°
            // to +21.9° from the driver's eyes, and the dashboard cuts off
            // everything below -4.94°, so a third of the canvas — every button on
            // every panel — was behind the dash. It was still clickable, because
            // the cabin meshes have no colliders and VRUIPointer intersects the
            // canvas plane analytically, which is why this presented as "the
            // country screen has no countries" rather than as a dead menu.
            Canvas menu = SceneBuildUtil.WorldCanvas(
                "MenuCanvas", car.SeatAnchor,
                new Vector3(0f, 0.10f, 2.60f), Quaternion.identity,
                new Vector2(1240f, 860f), 0.00215f, rig.Head);

            GameObject welcome = BuildWelcomePanel(menu.transform, ui.Manager);
            GameObject country = BuildCountryPanel(menu.transform, ui, out ui.CardContainer);
            GameObject recenter = BuildRecenterPanel(menu.transform, ui.Manager, out TMP_Text recenterInstr, out TMP_Text recenterStatus);
            GameObject ready = BuildReadyPanel(menu.transform, ui.Manager, out TMP_Text readyInstr);
            GameObject analysing = BuildAnalysingPanel(menu.transform);
            GameObject report = BuildReportPanel(menu.transform, ui);
            GameObject complete = BuildCompletePanel(menu.transform, ui.Manager,
                                                     out TMP_Text completeRec,
                                                     out TMP_Text completeCount);

            ui.CountryCardPrefab = BuildCountryCardPrefab(ui.Root.transform);

            ui.Manager.Configure(welcome, country, recenter, ready, analysing, report,
                                 complete, ui.CardContainer, ui.CountryCardPrefab);
            ui.Manager.ConfigureText(recenterInstr, recenterStatus, readyInstr,
                                     completeRec, completeCount);

            // ---- driving HUD on the dashboard ------------------------------------

            BuildHud(car, rig, ui);

            // ---- alert banner -----------------------------------------------------

            BuildAlertBanner(car, rig, ui);

            // ---- menu staging -----------------------------------------------------
            // Hides the car during the pure-menu phases and places the canvas
            // where it can be read in each case. Excludes the XR rig: the head
            // camera, the controller anchors and the reticle all live under it,
            // and hiding those would take the pointer with them.

            ui.Staging = ui.Root.AddComponent<MenuStaging>();
            ui.Staging.Configure(car.Root.transform, menu.transform,
                                 new[] { rig.Root.transform });

            // ---- mirror culling ---------------------------------------------------
            // Done here rather than in BuildVehicle because the XR rig has to
            // exist first: the rig lives under the seat anchor, and putting the
            // controller models on the cabin layer would hide the driver's own
            // hands from the mirrors for no reason.

            int cabinLayer = SceneBuildUtil.EnsureLayer(SceneBuildUtil.CabinLayer);
            int moved = SceneBuildUtil.SetLayerRecursive(car.Root.transform, cabinLayer,
                                                         rig.Root.transform);

            Line(cabinLayer >= 0
                 ? $"Mirrors: {moved} car mesh(es) on layer "
                   + $"'{SceneBuildUtil.CabinLayer}' (index {cabinLayer}), culled from "
                   + "all three mirror cameras so they show the road, not the cabin"
                 : "PROBLEM: no free user layer — the mirrors will show the inside "
                   + "of the car instead of the road behind it.");

            foreach (GameObject p in new[] { welcome, country, recenter, ready,
                                             analysing, report, complete })
                p.SetActive(p == welcome);

            Line("UI: one world-space menu canvas on the cabin with 7 panels, " +
                 "a dashboard HUD, an alert banner, and an EventSystem with no " +
                 "input module (VRUIPointer drives the buttons directly)");

            return ui;
        }

        private static GameObject BuildWelcomePanel(Transform canvas, UIManager m)
        {
            GameObject p = SceneBuildUtil.Panel("WelcomePanel", canvas, PanelBg);

            SceneBuildUtil.Text("Title", p.transform, "INTELLI-DRIVING SIMULATOR",
                                62f, Ink, TextAlignmentOptions.Center,
                                new Vector2(0f, 250f), new Vector2(1140f, 90f));

            SceneBuildUtil.Text("Subtitle", p.transform,
                                "Driver adaptation training for a new road system",
                                34f, InkDim, TextAlignmentOptions.Center,
                                new Vector2(0f, 170f), new Vector2(1060f, 60f));

            SceneBuildUtil.Text("Blurb", p.transform,
                                "This is not a driving test. It looks for habits that were " +
                                "correct where you learned to drive and do not transfer here " +
                                "— which way you look first, where you sit in the lane, which " +
                                "mirror you trust.\n\nEach drive takes about three minutes.",
                                28f, InkDim, TextAlignmentOptions.Center,
                                new Vector2(0f, 30f), new Vector2(940f, 200f));

            SceneBuildUtil.Btn("StartButton", p.transform, "START", new Vector2(0f, -170f),
                               new Vector2(380f, 100f), m.OnStartPressed, Accent, 40f);

            SceneBuildUtil.Btn("ExitButton", p.transform, "Exit", new Vector2(0f, -290f),
                               new Vector2(240f, 70f), m.OnExitPressed,
                               new Color(0.25f, 0.28f, 0.34f), 28f);

            return p;
        }

        private static GameObject BuildCountryPanel(Transform canvas, Ui ui,
                                                    out Transform cardContainer)
        {
            GameObject p = SceneBuildUtil.Panel("CountryPanel", canvas, PanelBg);

            SceneBuildUtil.Text("Title", p.transform, "WHERE DID YOU LEARN TO DRIVE?",
                                46f, Ink, TextAlignmentOptions.Center,
                                new Vector2(0f, 330f), new Vector2(1140f, 70f));

            SceneBuildUtil.Text("Hint", p.transform,
                                "Pick the road system you are used to. The cabin, the lane " +
                                "and the scoring baseline all change with it.",
                                26f, InkDim, TextAlignmentOptions.Center,
                                new Vector2(0f, 258f), new Vector2(1020f, 60f));

            GameObject row = SceneBuildUtil.Go("CardRow", p.transform);
            var rowRect = row.AddComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0f, -40f);
            rowRect.sizeDelta = new Vector2(1140f, 520f);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            cardContainer = row.transform;
            return p;
        }

        /// <summary>
        /// The country card, built once and kept inactive under the UI root.
        /// UIManager instantiates one per profile. Kept as a scene object rather
        /// than a prefab asset so the whole UI lives in one file and cannot get
        /// out of step with a stale prefab.
        /// </summary>
        private static GameObject BuildCountryCardPrefab(Transform parent)
        {
            GameObject card = SceneBuildUtil.Go("CountryCardTemplate", parent);
            card.layer = LayerMask.NameToLayer("UI");

            var rect = card.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(350f, 480f);

            var bg = card.AddComponent<Image>();
            bg.sprite = SceneBuildUtil.UISprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.13f, 0.17f, 0.24f, 1f);
            bg.raycastTarget = true;

            var view = card.AddComponent<CountryCardView>();

            TextMeshProUGUI name = SceneBuildUtil.Text("Name", card.transform, "Country",
                42f, Ink, TextAlignmentOptions.Center, new Vector2(0f, 185f),
                new Vector2(320f, 70f));

            TextMeshProUGUI traffic = SceneBuildUtil.Text("TrafficSide", card.transform,
                "Traffic keeps left", 28f, Accent, TextAlignmentOptions.Center,
                new Vector2(0f, 120f), new Vector2(320f, 40f));

            TextMeshProUGUI cabin = SceneBuildUtil.Text("CabinSide", card.transform,
                "Driver sits right", 28f, AccentWarm, TextAlignmentOptions.Center,
                new Vector2(0f, 80f), new Vector2(320f, 40f));

            TextMeshProUGUI differences = SceneBuildUtil.Text("Differences", card.transform,
                "", 24f, InkDim, TextAlignmentOptions.Top,
                new Vector2(0f, -40f), new Vector2(310f, 190f));

            // No persistent listener: CountryCardView.Bind wires this at runtime,
            // because each instantiated card needs the profile it was bound to.
            Button select = SceneBuildUtil.Btn("SelectButton", card.transform, "CHOOSE",
                new Vector2(0f, -195f), new Vector2(280f, 72f), null, Accent, 30f,
                boundAtRuntime: true);

            SceneBuildUtil.Wire(view, "nameText", name);
            SceneBuildUtil.Wire(view, "trafficSideText", traffic);
            SceneBuildUtil.Wire(view, "cabinSideText", cabin);
            SceneBuildUtil.Wire(view, "differencesText", differences);
            SceneBuildUtil.Wire(view, "selectButton", select);

            card.SetActive(false);
            return card;
        }

        private static GameObject BuildRecenterPanel(Transform canvas, UIManager m,
                                                     out TMP_Text instructions,
                                                     out TMP_Text status)
        {
            GameObject p = SceneBuildUtil.Panel("RecenterPanel", canvas, PanelBg);

            SceneBuildUtil.Text("Title", p.transform, "SIT IN THE DRIVING SEAT",
                                46f, Ink, TextAlignmentOptions.Center,
                                new Vector2(0f, 300f), new Vector2(1140f, 70f));

            instructions = SceneBuildUtil.Text("Instructions", p.transform, "",
                                               30f, InkDim, TextAlignmentOptions.Center,
                                               new Vector2(0f, 110f), new Vector2(980f, 220f));

            status = SceneBuildUtil.Text("Status", p.transform, "",
                                         28f, new Color(0.45f, 0.95f, 0.70f),
                                         TextAlignmentOptions.Center,
                                         new Vector2(0f, -30f), new Vector2(900f, 60f));

            SceneBuildUtil.Btn("RecenterButton", p.transform, "RECENTER",
                               new Vector2(-160f, -170f), new Vector2(300f, 92f),
                               m.OnRecenterPressed, Accent, 34f);

            SceneBuildUtil.Btn("ConfirmButton", p.transform, "I'M READY",
                               new Vector2(170f, -170f), new Vector2(300f, 92f),
                               m.OnRecenterConfirmPressed,
                               new Color(0.18f, 0.60f, 0.42f), 34f);

            return p;
        }

        private static GameObject BuildReadyPanel(Transform canvas, UIManager m,
                                                  out TMP_Text instructions)
        {
            GameObject p = SceneBuildUtil.Panel("ReadyPanel", canvas, PanelBg);

            SceneBuildUtil.Text("Title", p.transform, "THE CONTROLS",
                                46f, Ink, TextAlignmentOptions.Center,
                                new Vector2(0f, 310f), new Vector2(1140f, 70f));

            // Taller and a size smaller: this panel now carries the coached
            // procedures as well as the controls, because a driver who is told
            // the procedure only after failing it has not been trained.
            instructions = SceneBuildUtil.Text("Instructions", p.transform, "",
                                               28f, InkDim, TextAlignmentOptions.Center,
                                               new Vector2(0f, 60f), new Vector2(1040f, 390f));

            SceneBuildUtil.Btn("BeginDriveButton", p.transform, "START DRIVING",
                               new Vector2(0f, -190f), new Vector2(460f, 100f),
                               m.OnBeginDrivePressed, new Color(0.18f, 0.60f, 0.42f), 38f);

            return p;
        }

        private static GameObject BuildAnalysingPanel(Transform canvas)
        {
            GameObject p = SceneBuildUtil.Panel("AnalysingPanel", canvas, PanelBg);

            SceneBuildUtil.Text("Title", p.transform, "SCORING YOUR DRIVE",
                                50f, Ink, TextAlignmentOptions.Center,
                                new Vector2(0f, 60f), new Vector2(1140f, 80f));

            SceneBuildUtil.Text("Body", p.transform,
                                "Reading the telemetry from the last three minutes.",
                                30f, InkDim, TextAlignmentOptions.Center,
                                new Vector2(0f, -30f), new Vector2(980f, 70f));

            return p;
        }

        private static GameObject BuildReportPanel(Transform canvas, Ui ui)
        {
            GameObject p = SceneBuildUtil.Panel("ReportPanel", canvas, PanelBg);

            SceneBuildUtil.Text("Title", p.transform, "SESSION COMPLETE",
                                42f, Ink, TextAlignmentOptions.Center,
                                new Vector2(0f, 375f), new Vector2(1140f, 55f));

            TextMeshProUGUI composite = SceneBuildUtil.Text("CompositeValue", p.transform,
                "—", 72f, Ink, TextAlignmentOptions.Center,
                new Vector2(-400f, 268f), new Vector2(340f, 76f));

            TextMeshProUGUI band = SceneBuildUtil.Text("BandValue", p.transform,
                "", 32f, AccentWarm, TextAlignmentOptions.Center,
                new Vector2(-400f, 204f), new Vector2(340f, 42f));

            SceneBuildUtil.Text("CompositeLabel", p.transform, "OVERALL RISK",
                24f, InkDim, TextAlignmentOptions.Center,
                new Vector2(-400f, 325f), new Vector2(340f, 32f));

            Image compositeBar = Bar("CompositeBar", p.transform,
                                     new Vector2(-400f, 172f), new Vector2(330f, 18f));

            // ---- five skill rows -------------------------------------------------

            var skills = new (SkillArea area, string label)[]
            {
                (SkillArea.LaneKeeping, "Lane keeping"),
                (SkillArea.SteeringSmoothness, "Steering smoothness"),
                (SkillArea.MirrorAwareness, "Mirror awareness"),
                (SkillArea.GapAcceptance, "Gap selection"),
                (SkillArea.HazardResponse, "Hazard response")
            };

            float y = 300f;
            foreach ((SkillArea area, string label) in skills)
            {
                SceneBuildUtil.Text($"{area}Label", p.transform, label,
                    26f, InkDim, TextAlignmentOptions.Left,
                    new Vector2(190f, y), new Vector2(330f, 38f));

                TextMeshProUGUI value = SceneBuildUtil.Text($"{area}Value", p.transform,
                    "—", 26f, Ink, TextAlignmentOptions.Right,
                    new Vector2(530f, y), new Vector2(160f, 38f));

                Image bar = Bar($"{area}Bar", p.transform,
                                new Vector2(345f, y - 28f), new Vector2(430f, 14f));

                ui.Report.ConfigureSkill(area, bar, value);
                y -= 86f;
            }

            // ---- narrative ---------------------------------------------------------

            SceneBuildUtil.Text("WeakestLabel", p.transform, "MAIN IMPROVEMENT AREA",
                22f, InkDim, TextAlignmentOptions.Left,
                new Vector2(-400f, 90f), new Vector2(340f, 34f));

            TextMeshProUGUI weakest = SceneBuildUtil.Text("WeakestValue", p.transform,
                "", 32f, AccentWarm, TextAlignmentOptions.Left,
                new Vector2(-400f, 48f), new Vector2(340f, 48f));

            SceneBuildUtil.Text("NextLabel", p.transform, "RECOMMENDED NEXT SESSION",
                22f, InkDim, TextAlignmentOptions.Left,
                new Vector2(-400f, -5f), new Vector2(340f, 34f));

            TextMeshProUGUI recommendation = SceneBuildUtil.Text("NextValue", p.transform,
                "", 25f, Ink, TextAlignmentOptions.TopLeft,
                new Vector2(-400f, -100f), new Vector2(340f, 130f));

            TextMeshProUGUI trend = SceneBuildUtil.Text("Trend", p.transform,
                "", 24f, InkDim, TextAlignmentOptions.TopLeft,
                new Vector2(-400f, -245f), new Vector2(340f, 70f));

            TextMeshProUGUI scenarios = SceneBuildUtil.Text("ScenarioSummary", p.transform,
                "", 24f, InkDim, TextAlignmentOptions.TopLeft,
                new Vector2(200f, -192f), new Vector2(620f, 170f));

            ui.Report.ConfigureRoot(p);
            ui.Report.ConfigureHeadline(composite, band, compositeBar);
            ui.Report.ConfigureNarrative(weakest, recommendation, trend, scenarios);

            // Diagnostics, on the report where the driver is stationary and can
            // read them. Sits in the band between the narrative and the buttons.
            TextMeshProUGUI diagnostics = SceneBuildUtil.Text("Diagnostics", p.transform,
                "", 17f, new Color(0.55f, 0.75f, 0.95f, 1f),
                TextAlignmentOptions.Center,
                new Vector2(0f, -399f), new Vector2(1180f, 58f));

            Object.FindAnyObjectByType<IDS.Telemetry.SessionDiagnostics>()
                ?.Configure(diagnostics);

            SceneBuildUtil.Btn("DriveAgainButton", p.transform, "DRIVE AGAIN",
                               new Vector2(-180f, -325f), new Vector2(330f, 76f),
                               ui.Manager.OnDriveAgainPressed,
                               new Color(0.18f, 0.60f, 0.42f), 32f);

            SceneBuildUtil.Btn("DoneButton", p.transform, "DONE",
                               new Vector2(190f, -325f), new Vector2(260f, 76f),
                               ui.Manager.OnReportDonePressed,
                               new Color(0.25f, 0.28f, 0.34f), 32f);

            return p;
        }

        private static Image Bar(string name, Transform parent, Vector2 position, Vector2 size)
        {
            GameObject track = SceneBuildUtil.Go($"{name}Track", parent);
            track.layer = LayerMask.NameToLayer("UI");

            var trackRect = track.AddComponent<RectTransform>();
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.anchoredPosition = position;
            trackRect.sizeDelta = size;

            var trackImage = track.AddComponent<Image>();
            trackImage.sprite = SceneBuildUtil.UISprite();
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(1f, 1f, 1f, 0.10f);
            trackImage.raycastTarget = false;

            GameObject fill = SceneBuildUtil.Go(name, track.transform);
            fill.layer = LayerMask.NameToLayer("UI");

            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.pivot = new Vector2(0f, 0.5f);

            var image = fill.AddComponent<Image>();
            image.sprite = SceneBuildUtil.UISprite();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 0f;
            image.raycastTarget = false;

            return image;
        }

        private static GameObject BuildCompletePanel(Transform canvas, UIManager m,
                                                     out TMP_Text recommendation,
                                                     out TMP_Text sessionCount)
        {
            GameObject p = SceneBuildUtil.Panel("CompletePanel", canvas, PanelBg);

            SceneBuildUtil.Text("Title", p.transform, "TRAINING RECORDED",
                                50f, Ink, TextAlignmentOptions.Center,
                                new Vector2(0f, 280f), new Vector2(1140f, 80f));

            SceneBuildUtil.Text("Label", p.transform, "YOUR NEXT SESSION",
                                26f, InkDim, TextAlignmentOptions.Center,
                                new Vector2(0f, 150f), new Vector2(900f, 40f));

            recommendation = SceneBuildUtil.Text("Recommendation", p.transform, "",
                                                 36f, Accent, TextAlignmentOptions.Center,
                                                 new Vector2(0f, 70f), new Vector2(960f, 120f));

            sessionCount = SceneBuildUtil.Text("SessionCount", p.transform, "",
                                               26f, InkDim, TextAlignmentOptions.Center,
                                               new Vector2(0f, -30f), new Vector2(900f, 50f));

            SceneBuildUtil.Text("DataNote", p.transform,
                                "Your telemetry and profile are saved on this headset only.",
                                22f, InkDim, TextAlignmentOptions.Center,
                                new Vector2(0f, -110f), new Vector2(900f, 50f));

            SceneBuildUtil.Btn("NewSessionButton", p.transform, "NEW SESSION",
                               new Vector2(-170f, -240f), new Vector2(320f, 90f),
                               m.OnNewSessionPressed, Accent, 32f);

            SceneBuildUtil.Btn("DoneButton", p.transform, "EXIT",
                               new Vector2(180f, -240f), new Vector2(260f, 90f),
                               m.OnExitPressed, new Color(0.25f, 0.28f, 0.34f), 32f);

            return p;
        }

        private static void BuildHud(Car car, Rig rig, Ui ui)
        {
            // z = −0.13 puts the canvas just in front of the dash fascia, which is
            // 0.22 m deep and centred on the dashboard anchor. At −0.06 it would
            // be inside the fascia box and z-fight with it.
            Canvas hud = SceneBuildUtil.WorldCanvas(
                "HudCanvas", car.Dashboard,
                new Vector3(0f, 0.12f, -0.13f), Quaternion.Euler(-18f, 0f, 0f),
                new Vector2(620f, 260f), 0.00072f, rig.Head, interactive: false);

            GameObject root = SceneBuildUtil.Panel("HudRoot", hud.transform,
                                                   new Color(0.03f, 0.04f, 0.06f, 0.78f));

            TextMeshProUGUI speed = SceneBuildUtil.Text("Speed", root.transform, "0",
                110f, Ink, TextAlignmentOptions.Center,
                new Vector2(-200f, 14f), new Vector2(200f, 126f));

            TextMeshProUGUI unit = SceneBuildUtil.Text("Unit", root.transform, "km/h",
                28f, InkDim, TextAlignmentOptions.Center,
                new Vector2(-200f, -72f), new Vector2(200f, 36f));

            TextMeshProUGUI mode = SceneBuildUtil.Text("Mode", root.transform,
                "TRAINING DRIVE", 28f, Accent, TextAlignmentOptions.Right,
                new Vector2(105f, 80f), new Vector2(390f, 36f));

            TextMeshProUGUI mirror = SceneBuildUtil.Text("Mirror", root.transform, "",
                26f, AccentWarm, TextAlignmentOptions.Right,
                new Vector2(105f, 30f), new Vector2(390f, 36f));

            TextMeshProUGUI timeLeft = SceneBuildUtil.Text("TimeLeft", root.transform, "",
                34f, InkDim, TextAlignmentOptions.Right,
                new Vector2(105f, -30f), new Vector2(390f, 44f));

            // Diagnostic strip. Every "it doesn't move / nothing happens" report so
            // far has been impossible to diagnose from the outside, because the
            // one thing nobody could see was whether the headset was delivering
            // any input at all. This puts the raw numbers where the driver is
            // already looking. Turn showDiagnostics off on DrivingHUD for a demo.
            TextMeshProUGUI diag = SceneBuildUtil.Text("Diagnostics", root.transform, "",
                20f, new Color(0.55f, 0.75f, 0.95f, 1f), TextAlignmentOptions.Center,
                new Vector2(0f, -110f), new Vector2(600f, 30f));

            ui.Hud.Configure(root, speed, unit, mode, mirror, timeLeft, car.Controller);
            ui.Hud.ConfigureDiagnostics(diag);

            SceneBuildUtil.Wire(ui.Hud, "countryProfiles",
                                Object.FindAnyObjectByType<CountryProfileManager>());

            root.SetActive(false);
        }

        private static void BuildAlertBanner(Car car, Rig rig, Ui ui)
        {
            // Above the menu's position and nearer, so a critical alert is in the
            // driver's view without covering the road ahead. On the seat anchor
            // for the same reason the menu is.
            // Non-interactive: it sits between the driver and the menu, so a
            // raycaster on it would swallow rays aimed at the menu behind it.
            Canvas canvas = SceneBuildUtil.WorldCanvas(
                "AlertCanvas", car.SeatAnchor,
                new Vector3(0f, 0.30f, 1.05f), Quaternion.identity,
                new Vector2(900f, 150f), 0.00095f, rig.Head, interactive: false);

            GameObject root = SceneBuildUtil.Panel("AlertRoot", canvas.transform,
                                                   new Color(0.10f, 0.03f, 0.03f, 0.88f));

            TextMeshProUGUI text = SceneBuildUtil.Text("AlertText", root.transform, "",
                54f, new Color(1f, 0.45f, 0.38f), TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(860f, 130f));

            SceneBuildUtil.Wire(ui.Alerts, "alertRoot", root);
            SceneBuildUtil.Wire(ui.Alerts, "alertText", text);

            root.SetActive(false);
        }

        // =====================================================================
        //  cross-cutting wiring
        // =====================================================================

        private static void WireEverything(Services s, Env env, Car car, Rig rig,
                                           Traffic traffic, Ui ui, Hazards hazards,
                                           List<CountryProfile> profiles)
        {
            // ---- telemetry -----------------------------------------------------
            s.Recorder.Configure(car.Controller, env.LaneReference, rig.Head.transform,
                                 s.Mirrors, s.ProfileHolder);
            s.Mirrors.Configure(rig.Head.transform, car.Controller);
            s.Features.Configure(s.Recorder, s.Mirrors, env.GapDetector);
            SceneBuildUtil.Wire(env.GapDetector, "vehicleRef", car.Controller);

            // ---- scoring -------------------------------------------------------
            SceneBuildUtil.Wire(s.RuleScorer, "profileManager", s.Countries);
            SceneBuildUtil.Wire(s.Scoring, "ruleScorer", s.RuleScorer);
            SceneBuildUtil.Wire(s.Scoring, "classifierScorer", s.Classifier);
            SceneBuildUtil.Wire(s.Scoring, "featureExtractor", s.Features);
            SceneBuildUtil.Wire(s.Scoring, "profileManager", s.Drivers);
            SceneBuildUtil.Wire(s.Scoring, "countryProfiles", s.Countries);
            SceneBuildUtil.Wire(s.Scoring, "preferClassifier", false);
            SceneBuildUtil.Wire(s.Classifier, "ruleScorerForSubScores", s.RuleScorer);

            // ---- driver profile -------------------------------------------------
            SceneBuildUtil.Wire(s.Drivers, "countryProfiles", s.Countries);
            SceneBuildUtil.Wire(s.Drivers, "selector", s.Selector);
            SceneBuildUtil.Wire(s.Drivers, "driverId", "P00");

            // ---- session --------------------------------------------------------
            SceneBuildUtil.Wire(s.Session, "telemetryRecorderRef", s.Recorder);
            SceneBuildUtil.Wire(s.Session, "scenarioManagerRef", hazards.Manager);
            SceneBuildUtil.Wire(s.Session, "driveDurationSeconds", 180f);
            SceneBuildUtil.Wire(s.Session, "fastStartForTesting", false);

            // Where Drive Again puts the car back to. Without this the second
            // drive starts wherever the first one stopped.
            SceneBuildUtil.Wire(s.Session, "vehicleHome", env.VehicleStart);

            // ---- scenarios ------------------------------------------------------
            SceneBuildUtil.Wire(hazards.Manager, "selector", s.Selector);
            SceneBuildUtil.Wire(hazards.Manager, "profileManager", s.Drivers);

            foreach (HazardScenarioBase h in new HazardScenarioBase[]
                     { hazards.Pedestrian, hazards.Cyclist, hazards.LeadBrake })
                SceneBuildUtil.Wire(h, "vehicleRef", car.Controller);

            SceneBuildUtil.Wire(hazards.Cyclist, "mirrorDetector", s.Mirrors);
            SceneBuildUtil.Wire(hazards.Cyclist, "countryProfiles", s.Countries);

            // ---- vehicle --------------------------------------------------------
            SceneBuildUtil.Wire(car.Controller, "inputAdapter", rig.Input);
            car.EndGuard.Configure(DriveEndZ, env.VehicleStart);
            SceneBuildUtil.Wire(car.EndGuard, "vehicleRef", car.Controller);
            SceneBuildUtil.Wire(car.EndGuard, "vehicle", car.Controller);

            // ---- UI ---------------------------------------------------------------
            SceneBuildUtil.Wire(ui.Manager, "countryProfiles", s.Countries);
            SceneBuildUtil.Wire(ui.Manager, "seatRecenter", rig.Recenter);
            SceneBuildUtil.Wire(ui.Manager, "driverProfiles", s.Drivers);
            SceneBuildUtil.Wire(ui.Manager, "report", ui.Report);
            SceneBuildUtil.Wire(ui.Report, "profileManager", s.Drivers);
            SceneBuildUtil.Wire(ui.Report, "scenarioManager", hazards.Manager);
            SceneBuildUtil.Wire(ui.AlertTriggers, "vehicleRef", car.Controller);
            SceneBuildUtil.Wire(ui.AlertTriggers, "laneRef", env.LaneReference);
            SceneBuildUtil.Wire(ui.AlertTriggers, "countryProfiles", s.Countries);

            // ---- XR pointer -------------------------------------------------------
            rig.Pointer.Configure(rig.Head, rig.LeftAnchor, rig.RightAnchor,
                                  rig.LeftLine, rig.RightLine,
                                  rig.ReticleRoot, rig.ReticleFill);

            // ---- environment ------------------------------------------------------
            env.Configurator.Configure(env.EgoLaneRoot, env.OncomingLaneRoot,
                                       env.CyclistPathRoot, env.PedestrianStart,
                                       env.PedestrianEnd, env.VehicleStart,
                                       car.Controller, env.Signs);

            env.Configurator.ConfigureJunction(env.ConflictPoint, env.JunctionApproach,
                                               env.ManoeuvreZone);

            Line("Wiring: telemetry, scoring, session, scenarios, vehicle, UI, " +
                 "pointer and environment all wired by reference — no component " +
                 "left to find its own dependencies at runtime");
        }

        private static void SetBuildList()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            Line($"Build list: index 0 = {ScenePath} (the only scene). Whatever sits " +
                 "at index 0 is what launches on the headset; getting that wrong is " +
                 "silent, and cost this project an evening.");
        }

        // =====================================================================
        //  validation
        // =====================================================================

        /// <summary>
        /// Checks the scene it just built. Every check here corresponds to
        /// something that has actually gone wrong on this project and was only
        /// discovered in the headset.
        /// </summary>
        /// <summary>
        /// Fires five rays out of the driver's eye point — dead ahead and at the
        /// four corners of a narrow ±8° / ±4° cone — and names any mesh belonging
        /// to the car that they hit within 6 m.
        ///
        /// This exists because the original cabin had a 0.85 m-tall bulkhead
        /// centred at eye height 1.1 m in front of the seat. Everything else
        /// validated; the scene was simply unusable, and nothing caught it. The
        /// cone is deliberately narrow: you are *supposed* to see the bonnet, the
        /// A-pillars and the mirrors at the edges of your vision, so testing a
        /// wide cone only produces false alarms. What must never be obstructed is
        /// the small patch of view containing the road ahead.
        ///
        /// Renderer bounds rather than Physics.Raycast: SceneBuildUtil.Box creates
        /// colliders only on request, and the cabin meshes are visual only.
        /// </summary>
        /// <summary>
        /// The elevation, in degrees from the driver's eye, of the highest point
        /// of the dashboard's silhouette. Everything below this is behind the
        /// dash from the seat, at every distance. NaN if there is no dashboard.
        /// </summary>
        private static float DashboardCutAngle(Car car)
        {
            if (car == null || car.SeatAnchor == null || car.CabinRoot == null)
                return float.NaN;

            // The fascia hangs off the cabin, not off the Dashboard anchor — the
            // anchor carries the HUD and mirrors with the seat, the fascia does
            // not — so it has to be found by name.
            Renderer fascia = null;
            foreach (Renderer r in car.CabinRoot.GetComponentsInChildren<Renderer>(true))
                if (r.name == "DashFascia") { fascia = r; break; }

            if (fascia == null) return float.NaN;

            Vector3 eye = car.SeatAnchor.position;
            Bounds b = fascia.bounds;

            // The far top edge is the top of the silhouette: the near top edge is
            // the same height but closer, so it subtends a steeper (lower) angle.
            float dy = b.max.y - eye.y;
            float dz = b.max.z - eye.z;
            if (dz <= 0.01f) return float.NaN;

            return Mathf.Atan2(dy, dz) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The elevation of the menu canvas's bottom edge in one of its two
        /// placements, measured the same way.
        /// </summary>
        private static float MenuBottomAngle(Ui ui, Car car, bool inCabin)
        {
            if (ui == null || ui.Staging == null || car == null) return float.NaN;

            Canvas canvas = null;
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (c.name == "MenuCanvas") { canvas = c; break; }

            if (canvas == null) return float.NaN;

            float height = canvas.GetComponent<RectTransform>().sizeDelta.y;
            Vector3 local = inCabin ? ui.Staging.CabinPosition : ui.Staging.HallPosition;
            float scale = inCabin ? ui.Staging.CabinScale : ui.Staging.HallScale;

            // Both placements are authored in seat-anchor local space, with the
            // eye at the seat anchor's origin, so the offsets are the angles.
            float dy = local.y - height * scale * 0.5f;
            float dz = local.z;
            if (dz <= 0.01f) return float.NaN;

            return Mathf.Atan2(dy, dz) * Mathf.Rad2Deg;
        }

        private static List<string> ForwardViewBlockers(Car car)
        {
            var hits = new List<string>();
            if (car == null || car.SeatAnchor == null || car.Root == null) return hits;

            Transform root = car.Root.transform;
            Vector3 eye = car.SeatAnchor.position;

            // Dead ahead plus the four cone edges — not the diagonals, which add
            // nothing a wall-shaped obstruction would not already trip.
            var cone = new[]
            {
                new Vector2(0f, 0f), new Vector2(-8f, 0f), new Vector2(8f, 0f),
                new Vector2(0f, -4f), new Vector2(0f, 4f)
            };

            var directions = new List<Vector3>(cone.Length);
            foreach (Vector2 ae in cone)
                directions.Add(root.rotation
                               * (Quaternion.Euler(-ae.y, ae.x, 0f) * Vector3.forward));

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                // The mirror glass is a render target, not an obstruction, and the
                // priority highlight is a flat decal parented onto a mirror.
                if (r.name == "Glass" || r.name == "PriorityHighlight") continue;

                Bounds b = r.bounds;
                if (b.Contains(eye)) continue;

                foreach (Vector3 dir in directions)
                {
                    if (!b.IntersectRay(new Ray(eye, dir), out float distance)) continue;
                    if (distance > 6f) continue;

                    hits.Add($"{r.name} ({distance:F2} m ahead)");
                    break;
                }
            }

            return hits;
        }

        private static int Validate(Services s, Env env, Car car, Rig rig, Ui ui,
                                    Hazards hazards, Traffic traffic)
        {
            int problems = 0;
            Line("");
            Line("── validation ──");

            void Check(bool ok, string what, string ifBad)
            {
                if (ok) { Line($"  ok    {what}"); return; }
                Line($"  FAIL  {what} — {ifBad}");
                problems++;
            }

            Check(rig.Root.transform.parent == car.SeatAnchor,
                  "XR Origin is parented to the seat anchor",
                  "the driver would not move with the car");

            Check(rig.Head != null && rig.Head.CompareTag("MainCamera"),
                  "head camera is tagged MainCamera",
                  "Camera.main returns null and head-based detection silently stops");

            Check(rig.Head != null && rig.Head.GetComponent<XRHeadAnchor>() != null,
                  "the head camera has a pose driver",
                  "the camera would not follow the headset at all");

            Check(rig.Head != null && rig.Head.transform.parent == rig.CameraOffset,
                  "the head camera is a child of the Camera Offset object",
                  "the tracking-space pose would be applied in the wrong space");

            Check(car.Wheel != null && car.Wheel.parent == car.SteeringColumn,
                  "steering wheel is a child of the non-rotating column",
                  "hand-angle measurement would feed back and the wheel would run away");

            Check(Object.FindObjectsByType<Camera>().Length == 4,
                  "exactly 4 cameras (head + 3 mirrors)",
                  "an extra camera renders the scene twice and halves frame rate");

            Check(Object.FindAnyObjectByType<EventSystem>() != null,
                  "an EventSystem exists",
                  "no menu button could ever be pressed");

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            int missingCamera = canvases.Count(c => c.renderMode == RenderMode.WorldSpace
                                                    && c.worldCamera == null);
            Check(missingCamera == 0,
                  $"all {canvases.Length} world-space canvases have a camera assigned",
                  $"{missingCamera} canvas(es) would ignore every ray");

            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include);
            int unwired = buttons.Count(b => b.onClick.GetPersistentEventCount() == 0
                                             && b.name != "SelectButton");
            Check(unwired == 0,
                  $"{buttons.Length} buttons found, all wired " +
                  "(the country card's own button is bound at runtime)",
                  $"{unwired} button(s) would do nothing when pressed");

            int noGraphic = buttons.Count(b => b.targetGraphic == null);
            Check(noGraphic == 0, "every button has a target graphic",
                  $"{noGraphic} button(s) would give no visual press feedback");

            Check(Object.FindObjectsByType<MonoBehaviour>()
                        .All(m => m.GetType().Name != "GravityProvider"),
                  "no GravityProvider anywhere in the scene",
                  "the rig would fall out of the world at terminal velocity");

            Check(Object.FindObjectsByType<CharacterController>().Length == 0,
                  "no CharacterController on the rig",
                  "it would collide with the car's own interior");

            var scenarios = Object.FindObjectsByType<HazardScenarioBase>(
                FindObjectsInactive.Include);
            Check(scenarios.Length == 3, $"3 hazard scenarios in the scene",
                  $"found {scenarios.Length}");

            Check(env.LeadPath != null && env.LeadPath.transform.parent == env.EgoLaneRoot,
                  "the lead vehicle's path is inside the ego lane",
                  "it would stay in the oncoming lane when the drive side flips, " +
                  "and the following-distance hazard would never trigger");

            Check(env.ConflictPoint != null && env.JunctionApproach != null
                  && env.ManoeuvreZone != null,
                  "the junction and manoeuvre anchors exist and are wired to the " +
                  "environment configurator",
                  "they would stay on the authored side and the gap measurement " +
                  "would never be taken");

            MirrorView[] mirrors = Object.FindObjectsByType<MirrorView>(
                FindObjectsInactive.Include);
            Check(mirrors.Length == 3, "3 mirrors with a MirrorView each",
                  $"found {mirrors.Length}");

            Check(Object.FindAnyObjectByType<SessionManager>() != null
                  && env.VehicleStart != null,
                  "the session knows where to put the car back on Drive Again",
                  "a second drive would start wherever the first one ended, with "
                  + "no road left for the hazards to spawn into");

            // The hazard queue is what actually broke in the field: a hazard that
            // cannot trigger never resolves, and nothing behind it ever runs.
            // These are the three invariants that made that possible.
            var hazardList = Object.FindObjectsByType<HazardScenarioBase>(
                FindObjectsInactive.Include);

            Check(hazardList.Length >= 2,
                  $"{hazardList.Length} hazard scenario(s) available to the queue",
                  "a session plans 2 hazards; with fewer the plan cannot complete");

            Check(hazards.Manager != null,
                  "the scenario queue exists and owns the hazards",
                  "no hazard would ever be armed");

            int wiredProfiles = 0;
            if (s.Countries != null && s.Countries.Available != null)
                foreach (CountryProfile p in s.Countries.Available)
                    if (p != null) wiredProfiles++;

            Check(wiredProfiles >= 3,
                  $"{wiredProfiles} country profile(s) resolved in CountryProfileManager",
                  "the country-selection screen would render its title and hint and "
                  + "then no cards at all — this is checked here because the symptom "
                  + "on the headset looks like a layout bug, not a missing reference");

            // The dashboard's top edge cuts the driver's view at a fixed angle,
            // so a menu placed below it is hidden no matter how far away it is.
            // This is the check that was missing when the country cards, and
            // every other button in the game, sat behind the dash.
            float dashCut = DashboardCutAngle(car);
            float cabinBottom = MenuBottomAngle(ui, car, inCabin: true);
            Check(float.IsNaN(dashCut) || cabinBottom > dashCut,
                  $"the in-cabin menu clears the dashboard line "
                  + $"(menu bottom {cabinBottom:F1}°, dash cuts at {dashCut:F1}°)",
                  $"the bottom of every menu panel would be behind the dashboard "
                  + "— visible to the pointer, invisible to the driver");

            List<string> blockers = ForwardViewBlockers(car);
            Check(blockers.Count == 0,
                  "nothing in the car body blocks the driver's view straight ahead",
                  string.Join(", ", blockers) + " sits across the forward sightline "
                  + "— the driver cannot see the road, the lead vehicle, or any hazard");

            var stranded = new List<string>();
            foreach (var mover in new (string, Transform)[]
                     {
                         ("LeadVehicle", traffic.Lead != null ? traffic.Lead.transform : null),
                         ("CrossVehicle", traffic.Cross != null ? traffic.Cross.transform : null),
                         ("Pedestrian", traffic.Pedestrian != null ? traffic.Pedestrian.transform : null),
                         ("Cyclist", traffic.Cyclist != null ? traffic.Cyclist.transform : null)
                     })
            {
                if (mover.Item2 != null && mover.Item2.position.sqrMagnitude < 0.01f)
                    stranded.Add(mover.Item1);
            }

            Check(stranded.Count == 0,
                  "every NPC starts on its own route, not at the world origin",
                  string.Join(", ", stranded) + " would drive across the map to "
                  + "reach waypoint 0, in full view of the driver");

            Check(env.VehicleStart.position.z < DriveEndZ,
                  $"the drive ends at z={DriveEndZ:F0}, before the road does " +
                  $"(z={RoadStartZ + RoadLength:F0})",
                  "the car could drive off the end of the surface");

            Check(EditorBuildSettings.scenes.Length == 1
                  && EditorBuildSettings.scenes[0].path == ScenePath
                  && EditorBuildSettings.scenes[0].enabled,
                  "Main.unity is the only scene in the build list, enabled",
                  "the headset would launch a different scene");

            Check(s.Recorder != null && s.Features != null && s.Mirrors != null,
                  "telemetry, features and mirror detection are present",
                  "no session would be measured");

            Check(ui.Report != null && ui.Report.transform == ui.Root.transform,
                  "the report panel's component is on the always-active UI root",
                  "it would never receive SessionEnded and would show nothing");

            Line("");
            Line(problems == 0
                ? "All checks passed."
                : $"{problems} problem(s) — fix these before building an APK.");

            return problems;
        }
    }
}
#endif
