using System.Collections;
using UnityEngine;

namespace IDS.Core
{
    /// <summary>
    /// Drives the whole session. This is the single object that knows the order of
    /// events; every other component reacts to phase changes.
    ///
    /// Exactly one of these belongs in Main.unity.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        [Header("Session configuration")]
        [Tooltip("Drive duration before the session auto-ends. 0 = manual end only.")]
        [SerializeField] private float driveDurationSeconds = 180f;

        [Tooltip("Skip Welcome / Country / Recenter and go straight to driving. " +
                 "Editor convenience only — must be OFF for any build a " +
                 "participant will wear.")]
        [SerializeField] private bool fastStartForTesting;

        [Header("Wiring (leave empty to auto-resolve from the registry)")]
        [SerializeField] private MonoBehaviour telemetryRecorderRef; // ITelemetrySink
        [SerializeField] private MonoBehaviour scenarioManagerRef;   // IScenarioHost

        [Header("Reset")]
        [Tooltip("Where the car is put back to when a drive restarts. Without "
                 + "this, Drive Again starts you wherever the last drive ended.")]
        [SerializeField] private Transform vehicleHome;

        public SessionPhase Phase { get; private set; } = SessionPhase.Boot;
        public string SessionId { get; private set; } = "";
        public float ElapsedDriveTime { get; private set; }

        /// <summary>Seconds left in the drive. Negative when untimed.</summary>
        public float DriveTimeRemaining => driveDurationSeconds <= 0f
            ? -1f : Mathf.Max(0f, driveDurationSeconds - ElapsedDriveTime);

        private ITelemetrySink _telemetry;
        private IScenarioHost _scenarios;
        private IAnalysisHost _analysis;
        private IDS.Vehicle.VehicleController _vehicle;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            _telemetry = telemetryRecorderRef as ITelemetrySink
                         ?? ServiceRegistry.Require<ITelemetrySink>("telemetry");
            _scenarios = scenarioManagerRef as IScenarioHost
                         ?? ServiceRegistry.Require<IScenarioHost>("scenarios");
            _analysis = ServiceRegistry.Require<IAnalysisHost>("scoring");

            if (fastStartForTesting)
            {
                SetPhase(SessionPhase.Ready);
                BeginDrive();
            }
            else
            {
                SetPhase(SessionPhase.Welcome);
            }
        }

        private void Update()
        {
            if (Phase != SessionPhase.Driving && Phase != SessionPhase.HazardActive) return;

            ElapsedDriveTime += Time.deltaTime;

            if (driveDurationSeconds > 0f && ElapsedDriveTime >= driveDurationSeconds)
                EndDrive();
        }

        // ---- transitions, called by UIManager's buttons ----------------------

        public void OnWelcomeConfirmed() => SetPhase(SessionPhase.CountrySelection);

        /// <summary>
        /// Country chosen. The cabin reconfigures itself off
        /// CountryProfileChanged, which CountryProfileManager has already raised
        /// by the time this runs, so the driver arrives at the recentre step
        /// sitting on the correct side of a correctly built cabin.
        /// </summary>
        public void OnCountryConfirmed() => SetPhase(SessionPhase.Recenter);

        public void OnRecenterConfirmed() => SetPhase(SessionPhase.Ready);

        public void BeginDrive()
        {
            SessionId = $"session_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            ElapsedDriveTime = 0f;

            // Phase first: the recorder, HUD and alert triggers all gate on it,
            // and SessionStarted must not arrive while they still think the
            // session is in the Ready phase.
            SetPhase(SessionPhase.Driving);

            SessionEvents.RaiseSessionStarted(SessionId);
            _telemetry?.RecordEvent("session_start", SessionId);
            _scenarios?.ArmScenariosForSession();
        }

        private Coroutine _endDriveRoutine;

        public void EndDrive()
        {
            if (Phase == SessionPhase.Analysing || Phase == SessionPhase.Report
                || Phase == SessionPhase.Complete) return;
            _endDriveRoutine = StartCoroutine(EndDriveRoutine());
        }

        private IEnumerator EndDriveRoutine()
        {
            SetPhase(SessionPhase.Analysing);
            _telemetry?.RecordEvent("session_end", SessionId);
            _scenarios?.DisarmAll();

            // One frame so the recorder's last samples are captured before the
            // feature extractor reads the buffer.
            yield return null;

            RiskAssessment assessment = _analysis != null
                ? _analysis.AnalyseCurrentSession()
                : default;

            // SessionEnded stops the recorder and populates the report panel.
            SessionEvents.RaiseSessionEnded(assessment);
            SetPhase(SessionPhase.Report);
        }

        /// <summary>"Done" on the report — goes to the closing summary.</summary>
        public void OnReportDismissed() => SetPhase(SessionPhase.Complete);

        /// <summary>"Drive again" — straight back into the cabin, same driver.</summary>
        public void RestartSession()
        {
            ResetWorld();
            SetPhase(SessionPhase.Ready);
        }

        /// <summary>"New session" on the closing screen — back to the start.</summary>
        public void ReturnToWelcome()
        {
            ResetWorld();
            SetPhase(SessionPhase.Welcome);
        }

        /// <summary>
        /// Puts the world back to how a drive starts: scenarios disarmed, timer
        /// zeroed, traffic on its marks, and THE CAR BACK AT THE START LINE.
        ///
        /// That last part was missing, and it is the bug behind "Drive Again
        /// doesn't go back to the start". The car stayed wherever the previous
        /// drive ended — typically near the end of the 1400 m road — so the
        /// second drive had almost no road left, the hazards had nowhere to
        /// spawn ahead of the driver, and the whole run scored as if nothing had
        /// happened. Resetting the scenarios but not the vehicle they are
        /// measured against was only ever half a reset.
        /// </summary>
        private void ResetWorld()
        {
            // Cancel an end-of-drive still in flight. It yields a frame before
            // stamping the phase to Report, and a restart inside that window was
            // dragged straight back to the report screen.
            if (_endDriveRoutine != null)
            {
                StopCoroutine(_endDriveRoutine);
                _endDriveRoutine = null;
            }

            _scenarios?.ResetAll();
            ElapsedDriveTime = 0f;

            _vehicle ??= ServiceRegistry.Resolve<IDS.Vehicle.VehicleController>();

            if (_vehicle != null && vehicleHome != null)
            {
                _vehicle.ResetTo(vehicleHome.position, vehicleHome.rotation);
                _vehicle.ResetSessionCounters();
            }
            else
            {
                Debug.LogWarning("[Session] restart could not move the car back to "
                                 + $"the start (vehicle={_vehicle != null}, "
                                 + $"home={vehicleHome != null}). The next drive "
                                 + "will begin wherever the last one stopped.");
            }

            // Traffic too. An NPC left halfway down the road is scenery the next
            // driver will meet in the wrong place.
            foreach (IDS.Traffic.NPCVehicleController npc in
                     FindObjectsByType<IDS.Traffic.NPCVehicleController>(
                         FindObjectsInactive.Include))
                npc?.ResetToStart();

            // And the pedestrian and cyclist — which the previous version missed
            // entirely. Worse, the hazards SLIDE their route anchors up the road
            // to meet the driver, so resetting the follower alone would have put
            // it back at a start point that is itself in the wrong place.
            // RestoreHomeRoute puts the anchors back where the builder authored
            // them, then snaps the follower to them.
            foreach (IDS.Traffic.NPCPathFollower walker in
                     FindObjectsByType<IDS.Traffic.NPCPathFollower>(
                         FindObjectsInactive.Include))
                walker?.RestoreHomeRoute();
        }

        public void ConfigureHome(Transform home) => vehicleHome = home;

        // ---- scenario phase bridge, called by HazardScenarioBase -------------

        public void NotifyHazardActive(bool active)
        {
            if (Phase != SessionPhase.Driving && Phase != SessionPhase.HazardActive) return;
            SetPhase(active ? SessionPhase.HazardActive : SessionPhase.Driving);
        }

        private void SetPhase(SessionPhase next)
        {
            if (Phase == next) return;
            Phase = next;
            Debug.Log($"[Session] phase → {next}");
            SessionEvents.RaisePhaseChanged(next);
        }

        private void OnDestroy()
        {
            if (ServiceRegistry.Resolve<SessionManager>() != this) return;
            SessionEvents.ClearAll();
            ServiceRegistry.Clear();
        }
    }

    /// <summary>Implemented by ScenarioManager.</summary>
    public interface IScenarioHost
    {
        void ArmScenariosForSession();
        void DisarmAll();
        void ResetAll();
    }

    /// <summary>Implemented by RiskScoringSystem.</summary>
    public interface IAnalysisHost
    {
        RiskAssessment AnalyseCurrentSession();
    }
}
