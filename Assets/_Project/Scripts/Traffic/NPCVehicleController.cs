using UnityEngine;
using IDS.Core;

namespace IDS.Traffic
{
    /// <summary>
    /// Waypoint-following NPC car. Kinematic on purpose: full WheelCollider
    /// physics for background traffic costs frame budget a standalone headset does
    /// not have, and nothing in this project measures how realistically the NPCs
    /// handle.
    ///
    /// The lead-brake scenario drives this through EmergencyBrake() — the scenario
    /// decides WHEN, this class decides HOW. That split keeps hazard logic out of
    /// the traffic system.
    ///
    /// THE LOOK-AHEAD RAY IGNORES THIS VEHICLE'S OWN COLLIDERS. Physics.Raycast
    /// from a point inside a collider can report that collider as a hit, so a car
    /// raycasting from its own centre sees itself, decides something is in the
    /// way, and stops forever on the start line. The ray starts at the front
    /// bumper and any hit belonging to this vehicle is discarded.
    /// </summary>
    public class NPCVehicleController : MonoBehaviour
    {
        [SerializeField] private TrafficPath path;
        [SerializeField] private float targetSpeedKph = 35f;
        [SerializeField] private float accelerationKphPerSec = 8f;
        [SerializeField] private float brakingKphPerSec = 25f;
        [SerializeField] private float emergencyBrakingKphPerSec = 45f;
        [SerializeField] private float waypointTolerance = 1.5f;
        [SerializeField] private float turnRateDegPerSec = 90f;

        [Header("Following behaviour")]
        [Tooltip("Slow down if something is this close ahead. 0 disables.")]
        [SerializeField] private float lookAheadDistance = 8f;

        [Tooltip("Distance ahead of the transform origin the ray starts from. " +
                 "Must clear this vehicle's own collider.")]
        [SerializeField] private float bumperOffset = 2.2f;

        [SerializeField] private LayerMask obstacleLayers = ~0;

        public float CurrentSpeedKph { get; private set; }
        public bool IsEmergencyBraking { get; private set; }

        private int _waypointIndex;
        private float _commandedSpeedKph;
        private bool _halted;
        private bool _driving;
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        private void Start()
        {
            _commandedSpeedKph = targetSpeedKph;

            if (path == null)
            {
                Debug.LogWarning($"[NPC] {name} has no TrafficPath and will not move.");
                return;
            }

            // On its route from the first frame. Without this the car starts
            // wherever the scene left it — the world origin — and drives there
            // from across the map to reach waypoint 0. For the cross-traffic car
            // that is 520 m over the grass, in full view of the driver, before
            // the session has even started.
            ResetToStart();
        }

        private void OnEnable() => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            _driving = phase == SessionPhase.Driving || phase == SessionPhase.HazardActive;
        }

        private void Update()
        {
            if (path == null) return;

            // Parked outside the drive. Traffic circulating behind the menus is
            // movement the driver can see but not react to, and the lead car
            // would reach the end of its route before the drive began.
            if (!_driving)
            {
                CurrentSpeedKph = 0f;
                return;
            }

            float desired = _halted ? 0f : _commandedSpeedKph;
            if (lookAheadDistance > 0f && SomethingAhead()) desired = 0f;

            float rate = desired < CurrentSpeedKph
                ? (IsEmergencyBraking ? emergencyBrakingKphPerSec : brakingKphPerSec)
                : accelerationKphPerSec;

            CurrentSpeedKph = Mathf.MoveTowards(CurrentSpeedKph, desired,
                                                rate * Time.deltaTime);

            if (IsEmergencyBraking && CurrentSpeedKph <= 0.5f) IsEmergencyBraking = false;

            MoveAlongPath();
        }

        private bool SomethingAhead()
        {
            Vector3 origin = transform.position
                             + transform.forward * bumperOffset
                             + Vector3.up * 0.5f;

            // RaycastNonAlloc into a reused buffer: this runs every frame on
            // every NPC, and RaycastAll would allocate an array each time.
            int count = Physics.RaycastNonAlloc(origin, transform.forward, _hits,
                                                lookAheadDistance, obstacleLayers,
                                                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hit = _hits[i].collider.transform;
                if (hit == transform || hit.IsChildOf(transform)) continue;   // itself
                return true;
            }

            return false;
        }

        private void MoveAlongPath()
        {
            Vector3 target = path.GetWaypoint(_waypointIndex);
            Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
            Vector3 toTarget = flatTarget - transform.position;

            if (toTarget.magnitude <= waypointTolerance)
            {
                if (path.IsLastWaypoint(_waypointIndex)) { _halted = true; return; }
                _waypointIndex++;
                return;
            }

            if (toTarget.sqrMagnitude > 0.001f)
            {
                Quaternion want = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, want, turnRateDegPerSec * Time.deltaTime);
            }

            transform.position += transform.forward * (CurrentSpeedKph / 3.6f * Time.deltaTime);
        }

        // ---- API for the scenarios -------------------------------------------

        public void SetTargetSpeed(float kph)
        {
            _commandedSpeedKph = Mathf.Max(0f, kph);
            _halted = false;
        }

        public void EmergencyBrake()
        {
            IsEmergencyBraking = true;
            _commandedSpeedKph = 0f;
            _halted = false;
        }

        public void ResumeDriving()
        {
            IsEmergencyBraking = false;
            _halted = false;
            _commandedSpeedKph = targetSpeedKph;
        }

        public void ResetToStart()
        {
            _waypointIndex = 0;
            _halted = false;
            IsEmergencyBraking = false;
            CurrentSpeedKph = 0f;
            _commandedSpeedKph = targetSpeedKph;

            if (path == null || path.Count == 0) return;

            transform.position = path.GetWaypoint(0);

            // Face the next waypoint, or the first one is approached sideways.
            if (path.Count < 2) return;
            Vector3 toNext = path.GetWaypoint(1) - path.GetWaypoint(0);
            toNext.y = 0f;
            if (toNext.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toNext.normalized, Vector3.up);
        }

        /// <summary>
        /// Puts this vehicle on its own route at the waypoint nearest a target
        /// point, rather than back at waypoint 0.
        ///
        /// LeadBrakeScenario.OnArmed used to call ResetToStart(), which snapped
        /// the lead car to waypoint 0 at z = 150. Whenever this was not the first
        /// hazard of the drive, the car the driver was following visibly
        /// vanished from in front of them and reappeared far behind — and the
        /// scenario's "gap must be positive" test could never pass again, so it
        /// never triggered and the queue stalled behind it.
        /// </summary>
        public void PlaceNear(Vector3 target, float aheadMetres)
        {
            _halted = false;
            IsEmergencyBraking = false;
            CurrentSpeedKph = 0f;
            _commandedSpeedKph = targetSpeedKph;

            if (path == null || path.Count == 0) return;

            // Nearest waypoint to where we want to be, then step forward so the
            // car is genuinely ahead of the driver rather than level with them.
            int best = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < path.Count; i++)
            {
                float d = Vector3.SqrMagnitude(path.GetWaypoint(i) - target);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }

            _waypointIndex = Mathf.Min(best + 1, path.Count - 1);

            Vector3 spot = path.GetWaypoint(best);

            // Slide along the segment toward the next waypoint by aheadMetres.
            if (path.Count >= 2)
            {
                int next = Mathf.Min(best + 1, path.Count - 1);
                Vector3 dir = path.GetWaypoint(next) - spot;
                dir.y = 0f;

                if (dir.sqrMagnitude > 0.001f)
                {
                    dir.Normalize();
                    spot += dir * aheadMetres;
                    transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }

            transform.position = spot;
        }

        public void AssignPath(TrafficPath p)
        {
            path = p;
            _waypointIndex = 0;
        }

        public void Configure(TrafficPath p, float cruiseKph)
        {
            path = p;
            targetSpeedKph = cruiseKph;
            _commandedSpeedKph = cruiseKph;
        }
    }
}
