using UnityEngine;

namespace IDS.Traffic
{
    /// <summary>
    /// Waypoint-following NPC car. Kinematic on purpose: full WheelCollider
    /// physics for background traffic costs frame budget we do not have on a
    /// standalone headset, and nothing in the project measures how realistically
    /// the NPCs handle.
    ///
    /// Omkar's lead-brake scenario drives this through EmergencyBrake() — the
    /// scenario decides WHEN, this class decides HOW. That split is what keeps
    /// the hazard logic out of the traffic system.
    ///
    /// OWNER: Ananya. Consumer: Omkar (LeadBrakeScenario).
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
        [SerializeField] private LayerMask obstacleLayers = ~0;

        public float CurrentSpeedKph { get; private set; }
        public bool  IsEmergencyBraking { get; private set; }

        private int _waypointIndex;
        private float _commandedSpeedKph;
        private bool  _halted;

        private void Start()
        {
            _commandedSpeedKph = targetSpeedKph;
            if (path == null)
                Debug.LogWarning($"[NPC] {name} has no TrafficPath and will not move.");
        }

        private void Update()
        {
            if (path == null) return;

            float desired = _halted ? 0f : _commandedSpeedKph;
            if (lookAheadDistance > 0f && SomethingAhead()) desired = 0f;

            float rate = desired < CurrentSpeedKph
                ? (IsEmergencyBraking ? emergencyBrakingKphPerSec : brakingKphPerSec)
                : accelerationKphPerSec;

            CurrentSpeedKph = Mathf.MoveTowards(CurrentSpeedKph, desired, rate * Time.deltaTime);

            if (IsEmergencyBraking && CurrentSpeedKph <= 0.5f) IsEmergencyBraking = false;

            MoveAlongPath();
        }

        private bool SomethingAhead()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            return Physics.Raycast(origin, transform.forward, out _,
                                   lookAheadDistance, obstacleLayers,
                                   QueryTriggerInteraction.Ignore);
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

        // ---- API for Omkar's scenarios --------------------------------------

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
            if (path != null && path.Count > 0)
                transform.position = path.GetWaypoint(0);
        }

        public void AssignPath(TrafficPath p)
        {
            path = p;
            _waypointIndex = 0;
        }
    }
}
