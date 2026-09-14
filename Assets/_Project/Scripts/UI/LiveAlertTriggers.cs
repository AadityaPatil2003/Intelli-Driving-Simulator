using UnityEngine;
using IDS.Core;
using IDS.AI;

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

        [Header("Sources")]
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState
        [SerializeField] private MonoBehaviour laneRef;      // ILaneReference
        [SerializeField] private CountryProfileManager countryProfiles;

        private IVehicleState _vehicle;
        private ILaneReference _lane;
        private float _laneViolationFor;
        private float _speedViolationFor;
        private bool _enabled;

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            _lane = laneRef as ILaneReference ?? ServiceRegistry.Resolve<ILaneReference>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
        }

        private void OnEnable()  => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            _enabled = phase == SessionPhase.Driving || phase == SessionPhase.HazardActive;
            if (!_enabled) { _laneViolationFor = 0f; _speedViolationFor = 0f; }
        }

        private void Update()
        {
            if (!_enabled || _vehicle == null) return;

            CheckLane();
            CheckSpeed();
        }

        private void CheckLane()
        {
            if (_lane == null) return;

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
