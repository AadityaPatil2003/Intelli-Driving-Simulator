using UnityEngine;
using IDS.Core;
using IDS.AI;

namespace IDS.Vehicle
{
    /// <summary>
    /// The other half of "both road sides, one codebase". CabinConfigurator flips
    /// the cabin; this flips the road around it.
    ///
    /// What moves when the country profile changes:
    ///   - the ego lane's centreline, to the correct side of the carriageway
    ///   - the oncoming lane, to the other side
    ///   - the cyclist's path, to the kerb side of the ego lane
    ///   - the pedestrian's step-out point, to the kerb
    ///   - the vehicle's own start position
    ///
    /// Everything is driven from one number: the sign of the traffic side. The
    /// scene is authored for LEFT-hand traffic — the Australian and Indian case —
    /// and mirrored for right-hand traffic. Nothing is authored twice.
    ///
    /// Applied on CountryProfileChanged, which is raised during country selection,
    /// before the driver ever enters the cabin. Moving the lane under a car that
    /// is already driving would be a measurement artefact, so it is also ignored
    /// once a drive is under way.
    /// </summary>
    public class EnvironmentConfigurator : MonoBehaviour
    {
        [Header("Lane roots — authored for LEFT-hand traffic")]
        [Tooltip("Parent of the ego lane's LanePath.")]
        [SerializeField] private Transform egoLaneRoot;
        [SerializeField] private Transform oncomingLaneRoot;

        [Tooltip("Distance of a lane centreline from the road centre.")]
        [SerializeField] private float laneOffsetMetres = 1.75f;

        [Header("Junction — these sit in the ego lane, so they move with it")]
        [Tooltip("Where the gap measurement is taken.")]
        [SerializeField] private Transform conflictPoint;

        [Tooltip("The trigger volume covering the ego's approach to the junction.")]
        [SerializeField] private Transform junctionApproach;

        [Tooltip("Where the driver has to move over for the cyclist.")]
        [SerializeField] private Transform manoeuvreZone;

        [Header("Hazard placement")]
        [Tooltip("Cyclist path root. Sits on the kerb side of the ego lane.")]
        [SerializeField] private Transform cyclistPathRoot;
        [SerializeField] private float cyclistOffsetMetres = 3.1f;

        [Tooltip("Pedestrian start, on the kerb; the end point is across the lane.")]
        [SerializeField] private Transform pedestrianStart;
        [SerializeField] private Transform pedestrianEnd;
        [SerializeField] private float kerbOffsetMetres = 4.6f;

        [Header("Vehicle")]
        [SerializeField] private Transform vehicleStart;
        [SerializeField] private VehicleController vehicle;

        [Header("Signs")]
        [Tooltip("Signs move to the kerb side, because a sign on the far side of " +
                 "oncoming traffic is not where a driver looks for it.")]
        [SerializeField] private Transform[] signs;
        [SerializeField] private float signOffsetMetres = 6.2f;

        private SessionManager _session;
        private bool _applied;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            _session = ServiceRegistry.Resolve<SessionManager>();

            // Apply whatever profile is already current, so a scene opened
            // straight into the Ready phase is still laid out correctly.
            CountryProfile current = ServiceRegistry.Resolve<CountryProfileManager>()?.Current;
            if (current != null) Apply(current);
        }

        private void OnEnable() => SessionEvents.CountryProfileChanged += OnProfileChanged;
        private void OnDisable() => SessionEvents.CountryProfileChanged -= OnProfileChanged;

        private void OnProfileChanged(string profileId)
        {
            if (_session != null &&
                (_session.Phase == SessionPhase.Driving ||
                 _session.Phase == SessionPhase.HazardActive))
            {
                Debug.LogWarning("[Environment] country changed mid-drive — the road " +
                                 "layout is NOT being moved. Moving the lane under a " +
                                 "moving car would put a step change into " +
                                 "lane_offset that reads as driver error.");
                return;
            }

            CountryProfile profile = ServiceRegistry.Resolve<CountryProfileManager>()?.Current;
            if (profile != null) Apply(profile);
        }

        public void Apply(CountryProfile profile)
        {
            if (profile == null) return;

            // +1 for left-hand traffic (the authored baseline), −1 for right.
            float sign = profile.trafficSide == TrafficSide.Left ? 1f : -1f;

            // Left-hand traffic keeps to the left, which is negative X when
            // travelling along +Z.
            float egoX = -laneOffsetMetres * sign;
            float oncomingX = laneOffsetMetres * sign;
            float kerbSign = sign;   // the kerb is on the same side as the ego lane

            SetX(egoLaneRoot, egoX);
            SetX(oncomingLaneRoot, oncomingX);

            // Anything that measures the ego's behaviour has to sit in the ego's
            // lane. Left behind on the authored side, the junction trigger ends up
            // in the oncoming lane and the gap measurement is never taken at all —
            // which the scorer then reads as "no junction in this session".
            SetX(conflictPoint, egoX);
            SetX(junctionApproach, egoX);
            SetX(manoeuvreZone, egoX);

            SetX(cyclistPathRoot, -cyclistOffsetMetres * kerbSign);
            SetX(pedestrianStart, -kerbOffsetMetres * kerbSign);
            SetX(pedestrianEnd, kerbOffsetMetres * kerbSign * 0.25f);

            if (signs != null)
                foreach (Transform sign1 in signs)
                    SetX(sign1, -signOffsetMetres * kerbSign);

            if (vehicleStart != null)
            {
                Vector3 p = vehicleStart.localPosition;
                p.x = egoX;
                vehicleStart.localPosition = p;

                // Only move the car itself outside a drive.
                if (vehicle != null && (_session == null ||
                    _session.Phase != SessionPhase.Driving))
                    vehicle.ResetTo(vehicleStart.position, vehicleStart.rotation);
            }

            _applied = true;

            Debug.Log($"[Environment] {profile.displayName}: traffic keeps " +
                      $"{profile.trafficSide}, ego lane x={egoX:F2}, " +
                      $"kerb on the {(kerbSign > 0f ? "left" : "right")}");
        }

        /// <summary>True once a profile has been applied. If this is still false
        /// when a drive starts, the road is in its authored left-hand layout.</summary>
        public bool HasApplied => _applied;

        private static void SetX(Transform t, float x)
        {
            if (t == null) return;
            Vector3 p = t.localPosition;
            p.x = x;
            t.localPosition = p;
        }

        public void Configure(Transform ego, Transform oncoming, Transform cyclist,
                              Transform pedStart, Transform pedEnd,
                              Transform startPoint, VehicleController car,
                              Transform[] signPosts)
        {
            egoLaneRoot = ego;
            oncomingLaneRoot = oncoming;
            cyclistPathRoot = cyclist;
            pedestrianStart = pedStart;
            pedestrianEnd = pedEnd;
            vehicleStart = startPoint;
            vehicle = car;
            signs = signPosts;
        }

        public void ConfigureJunction(Transform conflict, Transform approach,
                                      Transform manoeuvre)
        {
            conflictPoint = conflict;
            junctionApproach = approach;
            manoeuvreZone = manoeuvre;
        }
    }
}
