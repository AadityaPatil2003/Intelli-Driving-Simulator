using System.Collections;
using UnityEngine;
using IDS.Core;

namespace IDS.Core
{
    /// <summary>
    /// Drives the whole session. This is the single object that knows the order
    /// of events; every other component reacts to phase changes.
    ///
    /// Put exactly one of these in Main.unity. It is safe to leave it out of the
    /// individual test scenes.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        [Header("Session configuration")]
        [Tooltip("Drive duration before the session auto-ends. 0 = manual end only.")]
        [SerializeField] private float driveDurationSeconds = 180f;

        [Tooltip("Skip Welcome/Country/Calibration and go straight to Driving. " +
                 "Useful in the editor, must be OFF for milestone builds.")]
        [SerializeField] private bool fastStartForTesting;

        [Header("Wiring (leave empty to auto-resolve from the registry)")]
        [SerializeField] private MonoBehaviour telemetryRecorderRef; // ITelemetrySink
        [SerializeField] private MonoBehaviour scenarioManagerRef;

        public SessionPhase Phase { get; private set; } = SessionPhase.Boot;
        public string SessionId { get; private set; } = "";
        public float ElapsedDriveTime { get; private set; }

        private ITelemetrySink _telemetry;
        private IScenarioHost _scenarios;
        private IAnalysisHost _analysis;

        private void Awake()
        {
            ServiceRegistry.Register(this);
        }

        private void Start()
        {
            _telemetry = telemetryRecorderRef as ITelemetrySink
                         ?? ServiceRegistry.Require<ITelemetrySink>("Anushka");
            _scenarios = scenarioManagerRef as IScenarioHost
                         ?? ServiceRegistry.Require<IScenarioHost>("Omkar");
            _analysis  = ServiceRegistry.Require<IAnalysisHost>("Anushka");

            SetPhase(fastStartForTesting ? SessionPhase.Ready : SessionPhase.Welcome);
            if (fastStartForTesting) BeginDrive();
        }

        private void Update()
        {
            if (Phase != SessionPhase.Driving && Phase != SessionPhase.HazardActive) return;

            ElapsedDriveTime += Time.deltaTime;

            if (driveDurationSeconds > 0f && ElapsedDriveTime >= driveDurationSeconds)
                EndDrive();
        }

        // ---- public transitions, called by Omkar's UI buttons -----------------

        public void OnWelcomeConfirmed()   => SetPhase(SessionPhase.CountrySelection);
        public void OnCountryConfirmed()   => SetPhase(SessionPhase.Calibration);

        public void OnCalibrationConfirmed()
        {
            SessionEvents.RaiseCalibrationCompleted();
            SetPhase(SessionPhase.Ready);
        }

        public void BeginDrive()
        {
            SessionId = $"session_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            ElapsedDriveTime = 0f;

            _telemetry?.RecordEvent("session_start", SessionId);
            _scenarios?.ArmScenariosForSession();

            SessionEvents.RaiseSessionStarted(SessionId);
            SetPhase(SessionPhase.Driving);
        }

        public void EndDrive()
        {
            if (Phase == SessionPhase.Analysing || Phase == SessionPhase.Report) return;
            StartCoroutine(EndDriveRoutine());
        }

        private IEnumerator EndDriveRoutine()
        {
            SetPhase(SessionPhase.Analysing);
            _telemetry?.RecordEvent("session_end", SessionId);
            _scenarios?.DisarmAll();

            // Give the recorder a frame to flush its final samples to disk.
            yield return null;

            RiskAssessment assessment = _analysis != null
                ? _analysis.AnalyseCurrentSession()
                : default;

            SessionEvents.RaiseSessionEnded(assessment);
            SetPhase(SessionPhase.Report);
        }

        public void OnReportDismissed() => SetPhase(SessionPhase.Complete);

        public void RestartSession()
        {
            _scenarios?.ResetAll();
            SetPhase(SessionPhase.Ready);
        }

        // ---- scenario phase bridge, called by Omkar's ScenarioManager ---------

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
            if (ServiceRegistry.Resolve<SessionManager>() == this)
                ServiceRegistry.Clear();
        }
    }

    /// <summary>Implemented by Omkar's ScenarioManager.</summary>
    public interface IScenarioHost
    {
        void ArmScenariosForSession();
        void DisarmAll();
        void ResetAll();
    }

    /// <summary>Implemented by Anushka's RiskScoringSystem.</summary>
    public interface IAnalysisHost
    {
        RiskAssessment AnalyseCurrentSession();
    }
}
