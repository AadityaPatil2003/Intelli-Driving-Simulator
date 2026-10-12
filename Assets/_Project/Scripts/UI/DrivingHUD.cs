using UnityEngine;
using TMPro;
using IDS.Core;
using IDS.AI;
using IDS.Telemetry;
using IDS.XR;
using IDS.Scenarios;

namespace IDS.UI
{
    /// <summary>
    /// The in-drive display. It carries almost nothing, on purpose: speed, the
    /// mirror the system wants watched, the training mode, and the time left. A
    /// dashboard showing every telemetry channel is useless in motion — the detail
    /// belongs in the post-drive report, where the driver is stationary and can
    /// read it.
    ///
    /// PLACEMENT: a world-space canvas on the dashboard, parented to the vehicle.
    /// NOT head-locked. A head-locked HUD moves with the eyes, so it gives the
    /// vestibular system no information and reads as a helmet visor; dashboard-
    /// mounted, it is part of the static reference frame that makes a cockpit view
    /// comfortable.
    ///
    /// This component lives on an always-active object with hudRoot pointing at
    /// the canvas it shows and hides. A component inside a deactivated hierarchy
    /// never gets OnEnable, so one that starts hidden and holds its own
    /// subscription never learns the drive has begun.
    /// </summary>
    public class DrivingHUD : MonoBehaviour
    {
        [Header("Elements")]
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private TMP_Text speedText;
        [SerializeField] private TMP_Text speedUnitText;
        [SerializeField] private TMP_Text trainingModeText;
        [SerializeField] private TMP_Text priorityMirrorText;
        [SerializeField] private TMP_Text timeRemainingText;

        [Header("Speed colouring")]
        [SerializeField] private Color normalColour = Color.white;
        [SerializeField] private Color overLimitColour = new Color(1f, 0.5f, 0.4f);
        [SerializeField] private float overLimitTolerance = 5f;

        [Header("Sources")]
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState
        [SerializeField] private CountryProfileManager countryProfiles;

        [Header("Mirror status")]
        [Tooltip("A mirror checked more recently than this counts as current.")]
        [SerializeField] private float mirrorFreshSeconds = 12f;
        [SerializeField] private Color mirrorOkColour = new Color(0.45f, 0.95f, 0.70f);
        [SerializeField] private Color mirrorDueColour = new Color(0.95f, 0.62f, 0.25f);

        [Tooltip("For this long into a drive, any mirror may be prompted for. "
                 + "After it, only mirrors the detector has actually registered "
                 + "at least once — so the prompt can never stick on a mirror "
                 + "the driver has no way of satisfying.")]
        [SerializeField] private float graceSeconds = 45f;

        [Header("Diagnostics")]
        [Tooltip("Raw pedal/steer input and scenario state on the HUD. OFF: this "
                 + "is debugging furniture and it has no place in front of a "
                 + "participant or in a demo recording.")]
        [SerializeField] private bool showDiagnostics = false;
        [SerializeField] private TMP_Text diagnosticsText;

        private VehicleInputAdapter _input;
        private ScenarioManager _scenarios;
        private MirrorCheckDetector _mirrors;
        private IVehicleState _vehicle;
        private SessionManager _session;
        private float _limitKph = 50f;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            _session = ServiceRegistry.Resolve<SessionManager>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
            _mirrors = ServiceRegistry.Resolve<MirrorCheckDetector>();

            if (speedUnitText != null) speedUnitText.text = "km/h";

            RefreshProfileLabels();
            SetVisible(false);
        }

        private void OnEnable()
        {
            SessionEvents.PhaseChanged += OnPhaseChanged;
            SessionEvents.CountryProfileChanged += OnCountryProfileChanged;
        }

        private void OnDisable()
        {
            SessionEvents.PhaseChanged -= OnPhaseChanged;
            SessionEvents.CountryProfileChanged -= OnCountryProfileChanged;
        }

        // Named handlers, not lambdas. A lambda cannot be unsubscribed, so the
        // subscription outlives the object and fires on a destroyed one.
        private void OnCountryProfileChanged(string profileId) => RefreshProfileLabels();

        private void OnPhaseChanged(SessionPhase phase)
        {
            bool driving = phase == SessionPhase.Driving || phase == SessionPhase.HazardActive;
            SetVisible(driving);

            if (trainingModeText != null)
                trainingModeText.text = phase == SessionPhase.HazardActive
                    ? "HAZARD" : "TRAINING DRIVE";
        }

        private void Update()
        {
            RefreshMirrorStatus();
            RefreshDiagnostics();
            if (hudRoot != null && !hudRoot.activeSelf) return;

            if (_vehicle != null && speedText != null)
            {
                float kph = Mathf.Abs(_vehicle.CurrentSpeedKph);
                speedText.text = Mathf.RoundToInt(kph).ToString();
                speedText.color = kph > _limitKph + overLimitTolerance
                    ? overLimitColour : normalColour;
            }

            if (timeRemainingText == null || _session == null) return;

            float remaining = _session.DriveTimeRemaining;
            timeRemainingText.text = remaining < 0f
                ? ""
                : $"{Mathf.FloorToInt(remaining / 60f):0}:{Mathf.FloorToInt(remaining % 60f):00}";
        }

        private void RefreshProfileLabels()
        {
            CountryProfile profile = countryProfiles?.Current;
            if (profile == null) return;

            _limitKph = profile.defaultSpeedLimitKph;

            RefreshMirrorStatus();
        }

        /// <summary>
        /// Raw input and scenario state, on the dashboard.
        ///
        /// "The car will not move", "the cyclist is stationary" and "the
        /// pedestrian never appears" are three different symptoms of the same
        /// unanswered question: is anything actually arriving from the
        /// controllers, and has a scenario armed? Guessing at that from a cast
        /// screenshot wasted hours. Now it is on the glass.
        /// </summary>
        private void RefreshDiagnostics()
        {
            if (diagnosticsText == null) return;

            if (!showDiagnostics)
            {
                if (diagnosticsText.text.Length > 0) diagnosticsText.text = "";
                return;
            }

            _input ??= ServiceRegistry.Resolve<VehicleInputAdapter>();
            _scenarios ??= ServiceRegistry.Resolve<ScenarioManager>();

            string pedals = _input != null
                ? $"thr {_input.Accelerator:0.00}  brk {_input.Brake:0.00}  "
                  + $"str {_input.Steering:+0.00;-0.00} [{_input.ActiveSteeringProvider}]"
                : "no input adapter";

            string armed = _scenarios != null && !string.IsNullOrEmpty(_scenarios.ArmedScenarioId)
                ? _scenarios.ArmedScenarioId
                : "none armed";

            diagnosticsText.text = $"{pedals}   ·   {armed}";
        }

        /// <summary>
        /// Shows whether the priority mirror is CURRENT or OVERDUE, rather than a
        /// fixed "WATCH: LEFT MIRROR" that sits there for the whole drive.
        ///
        /// A permanent instruction is not an instruction. It never changes, so it
        /// carries no information, the driver stops reading it within about
        /// thirty seconds, and the one time it genuinely matters it is invisible.
        /// Tying it to MirrorCheckDetector — the same data the scoring uses —
        /// makes it mean something and makes the HUD agree with the report.
        /// </summary>
        private void RefreshMirrorStatus()
        {
            if (priorityMirrorText == null) return;

            CountryProfile profile = countryProfiles?.Current;
            if (profile == null) return;

            string side = profile.priorityMirror.ToString().ToUpper();

            // Lazy re-resolve: this runs every frame, and a one-shot lookup in
            // Start would leave the label dead for the whole session if the
            // detector happened to register later.
            _mirrors ??= ServiceRegistry.Resolve<MirrorCheckDetector>();
            _session ??= ServiceRegistry.Resolve<SessionManager>();

            if (_mirrors == null)
            {
                priorityMirrorText.text = $"{side} MIRROR";
                priorityMirrorText.color = mirrorDueColour;
                return;
            }

            // All three mirrors — but a mirror can only be NAMED if the detector
            // has proved it can register it.
            //
            // Both previous versions were wrong in opposite directions. Naming
            // only the priority mirror trained exactly one habit and told the
            // driver nothing about the other two. Naming the stalest of all three
            // pinned the prompt to whichever mirror the detector could not see,
            // which left one instruction on screen that no amount of checking
            // could satisfy. The fix is not a better guess about which mirror to
            // pick: it is to stop asking for something the system cannot observe.
            //
            // For the first `graceSeconds` of a drive every mirror is fair game,
            // because nothing has been checked yet. After that, a mirror that has
            // never once registered drops out of the prompt rotation — it is
            // either unreachable from this seat or mis-zoned, and either way
            // nagging about it is a bug the driver cannot fix.
            float elapsed = _session != null ? _session.ElapsedDriveTime : 0f;
            bool grace = elapsed < graceSeconds;

            string stalest = null;
            float worst = 1f;

            void Consider(string label, float since, float limit, int checks)
            {
                if (!grace && checks == 0) return;
                float ratio = since / Mathf.Max(0.01f, limit);
                if (ratio <= worst) return;
                worst = ratio;
                stalest = label;
            }

            float other = mirrorFreshSeconds * 2f;
            float priority = mirrorFreshSeconds;

            Consider("LEFT", _mirrors.SecondsSinceCheck(TrafficSide.Left),
                     profile.priorityMirror == TrafficSide.Left ? priority : other,
                     _mirrors.LeftChecks);

            Consider("RIGHT", _mirrors.SecondsSinceCheck(TrafficSide.Right),
                     profile.priorityMirror == TrafficSide.Right ? priority : other,
                     _mirrors.RightChecks);

            Consider("CENTRE", _mirrors.SecondsSinceCentreCheck(), other,
                     _mirrors.CentreChecks);

            if (stalest == null)
            {
                priorityMirrorText.text = "MIRRORS OK";
                priorityMirrorText.color = mirrorOkColour;
            }
            else
            {
                priorityMirrorText.text = $"CHECK {stalest} MIRROR";
                priorityMirrorText.color = mirrorDueColour;
            }
        }

        public void SetVisible(bool visible)
        {
            if (hudRoot != null && hudRoot.activeSelf != visible) hudRoot.SetActive(visible);
        }

        public void Configure(GameObject root, TMP_Text speed, TMP_Text unit,
                              TMP_Text mode, TMP_Text mirror, TMP_Text timeLeft,
                              MonoBehaviour vehicle)
        {
            hudRoot = root;
            speedText = speed;
            speedUnitText = unit;
            trainingModeText = mode;
            priorityMirrorText = mirror;
            timeRemainingText = timeLeft;
            vehicleRef = vehicle;
        }
    
        public void ConfigureDiagnostics(TMP_Text diagnostics) =>
            diagnosticsText = diagnostics;
}
}
