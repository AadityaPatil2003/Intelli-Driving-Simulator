using UnityEngine;
using IDS.Core;
using IDS.AI;
using IDS.Telemetry;

namespace IDS.UI
{
    /// <summary>
    /// Watches the live driving state and raises alerts. Separated from
    /// AlertManager so the trigger logic (when to warn) and the presentation
    /// (how and whether to show it) can be tuned independently.
    ///
    /// Every threshold here has a sustain time. Instantaneous thresholds fire
    /// constantly at the boundary — a driver sitting at exactly 51 km/h would get
    /// a speed alert every frame without them.
    ///
    /// OWNER: Omkar. Thresholds agreed with Anushka.
    /// </summary>
    public class LiveAlertTriggers : MonoBehaviour
    {
        [Header("Lane departure")]
        [SerializeField] private float laneOffsetWarnMetres = 1.1f;
        [SerializeField] private float laneSustainSeconds = 1.2f;

        [Header("Speed")]
        [SerializeField] private float speedOverLimitTolerance = 5f;
        [SerializeField] private float speedSustainSeconds = 2f;

        [Header("Mirror checks")]
        [Tooltip("Prompt the driver if they have not checked the mirror the "
                 + "current country treats as the priority one for this long.")]
        [SerializeField] private float mirrorSilenceSeconds = 18f;

        [Header("Sources")]
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState
        [SerializeField] private MonoBehaviour laneRef;      // ILaneReference
        [SerializeField] private CountryProfileManager countryProfiles;

        private IVehicleState _vehicle;
        private ILaneReference _lane;
        private MirrorCheckDetector _mirrors;
        private AlertManager _alerts;
        private float _laneViolationFor;
        private float _speedViolationFor;
        private bool _enabled;
        private bool _hazardActive;
        private bool _mirrorPromptUp;
        private int _checksWhenPrompted;

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            _lane = laneRef as ILaneReference ?? ServiceRegistry.Resolve<ILaneReference>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
            _mirrors = ServiceRegistry.Resolve<MirrorCheckDetector>();
            _alerts = ServiceRegistry.Resolve<AlertManager>();
        }

        private void OnEnable()  => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            _enabled = phase == SessionPhase.Driving || phase == SessionPhase.HazardActive;
            _hazardActive = phase == SessionPhase.HazardActive;
            if (!_enabled)
            {
                _laneViolationFor = 0f;
                _speedViolationFor = 0f;
                _mirrorPromptUp = false;
            }
        }

        private void Update()
        {
            if (!_enabled || _vehicle == null) return;

            CheckLane();
            CheckSpeed();
            CheckMirrors();
        }

        /// <summary>
        /// Prompts for a mirror check, then takes the prompt down the instant one
        /// happens — replaced by an acknowledgement rather than just vanishing,
        /// so the driver can tell the difference between "obeyed" and "timed out".
        /// </summary>
        private void CheckMirrors()
        {
            if (_mirrors == null) return;

            if (_mirrorPromptUp)
            {
                if (_mirrors.TotalChecks > _checksWhenPrompted)
                {
                    _mirrorPromptUp = false;
                    _alerts?.DismissIfShowing("CHECK MIRROR", "GOOD CHECK");
                }
                return;
            }

            TrafficSide priority = countryProfiles?.Current != null
                ? countryProfiles.Current.priorityMirror
                : TrafficSide.Right;

            if (_mirrors.SecondsSinceCheck(priority) < mirrorSilenceSeconds) return;

            _checksWhenPrompted = _mirrors.TotalChecks;
            _mirrorPromptUp = true;
            AlertManager.CheckMirror();
        }

        private void CheckLane()
        {
            if (_lane == null) return;

            // Silent during a hazard. Passing a cyclist REQUIRES leaving your
            // lane, and the simulator telling you to maintain lane while also
            // scoring you on whether you moved out safely is not just noise — it
            // is two parts of the same system giving opposite instructions, and
            // the driver has no way to know which one to obey.
            if (_hazardActive) return;

            float offset = Mathf.Abs(_lane.GetSignedLaneOffset(_vehicle.Position));

            if (offset > laneOffsetWarnMetres)
            {
                _laneViolationFor += Time.deltaTime;
                if (_laneViolationFor >= laneSustainSeconds)
                {
                    AlertManager.MaintainLane();
                    _laneViolationFor = 0f;
                }
            }
            else _laneViolationFor = 0f;
        }

        private void CheckSpeed()
        {
            float limit = countryProfiles?.Current != null
                ? countryProfiles.Current.defaultSpeedLimitKph : 50f;

            if (_vehicle.CurrentSpeedKph > limit + speedOverLimitTolerance)
            {
                _speedViolationFor += Time.deltaTime;
                if (_speedViolationFor >= speedSustainSeconds)
                {
                    AlertManager.ReduceSpeed();
                    _speedViolationFor = 0f;
                }
            }
            else _speedViolationFor = 0f;
        }
    }
}
