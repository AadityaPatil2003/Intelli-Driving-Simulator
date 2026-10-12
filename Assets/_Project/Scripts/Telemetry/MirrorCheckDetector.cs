using UnityEngine;
using IDS.Core;

namespace IDS.Telemetry
{
    /// <summary>
    /// Detects mirror and shoulder checks from HEAD ORIENTATION. Quest 3 gives no
    /// eye tracking, so this is a proxy and the report must call it one: it cannot
    /// distinguish a mirror glance from a head turn that happens to point the same
    /// way. That limitation is real and should be stated, not hidden.
    ///
    /// A valid check requires all three:
    ///   1. head yaw enters a mirror zone, measured against the vehicle's forward
    ///   2. it stays there for at least dwellTime — a glance, not a sweep past
    ///   3. the cooldown since the last registered check has elapsed
    ///
    /// Without the dwell requirement a single slow head turn registers dozens of
    /// checks. Without the cooldown, holding the head still in a zone registers
    /// one per frame. Both were real bugs; neither guard may be removed.
    ///
    /// PER-SIDE TIMESTAMPS. CheckedSideRecently previously consulted one shared
    /// "last check" timestamp and then fell back to `LeftChecks > 0`. Two faults
    /// compounded: a check on the RIGHT satisfied a query about the LEFT, and the
    /// counter fallback meant one check anywhere in the session satisfied every
    /// later query forever. The cyclist blind-spot test — the scenario that most
    /// directly probes this project's central claim — therefore passed almost
    /// unconditionally. Each side now keeps its own timestamp and there is no
    /// counter fallback, so the question asked is the question answered.
    /// </summary>
    public class MirrorCheckDetector : MonoBehaviour
    {
        [Header("Zones — degrees of head yaw relative to vehicle forward")]
        [Tooltip("Left mirror / left shoulder. Negative yaw is left.")]
        [SerializeField] private Vector2 leftZone = new Vector2(-95f, -22f);

        [Tooltip("Right mirror / right shoulder.")]
        [SerializeField] private Vector2 rightZone = new Vector2(22f, 95f);

        [Tooltip("Centre (rear-view) mirror: near-zero yaw with the head pitched up.")]
        [SerializeField] private Vector2 centreYawZone = new Vector2(-32f, 32f);
        [SerializeField] private float centreMinPitchUp = 10f;

        [Header("Timing")]
        [SerializeField] private float dwellTime = 0.18f;
        [SerializeField] private float cooldown = 0.9f;

        [Header("Sources")]
        [SerializeField] private Transform headTransform;
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState

        public int LeftChecks { get; private set; }
        public int RightChecks { get; private set; }
        public int CentreChecks { get; private set; }
        public int TotalChecks => LeftChecks + RightChecks + CentreChecks;

        private enum MirrorZone { None, Left, Right, Centre }

        private IVehicleState _vehicle;
        private MirrorZone _currentZone = MirrorZone.None;
        private float _zoneEnteredAt = -99f;
        private bool _registeredCurrentDwell;

        // One timestamp per side. Never collapse these back into one.
        private float _lastLeftAt = NeverChecked;
        private float _lastRightAt = NeverChecked;
        private float _lastCentreAt = NeverChecked;

        private const float NeverChecked = -9999f;

        /// Set for one sample when a check registers; the recorder consumes it.
        private bool _checkFlag;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;

            if (headTransform == null)
                Debug.LogError("[Mirror] no head transform — mirror checks cannot be " +
                               "detected and mirror_check_freq will be zero for every " +
                               "session.");
        }

        private void OnEnable() => SessionEvents.SessionStarted += OnSessionStarted;
        private void OnDisable() => SessionEvents.SessionStarted -= OnSessionStarted;

        /// <summary>
        /// Counters and timestamps are per session. Without this, a second drive
        /// in the same app run starts with the first drive's checks already
        /// counted, which inflates mirror_check_freq for every session after the
        /// first — the kind of error that looks like the driver improving.
        /// </summary>
        private void OnSessionStarted(string sessionId) => ResetCounters();

        private void Update()
        {
            if (headTransform == null || _vehicle == null) return;

            GetHeadAngles(out float yaw, out float pitch);
            MirrorZone zone = Classify(yaw, pitch);

            if (zone != _currentZone)
            {
                _currentZone = zone;
                _zoneEnteredAt = Time.time;
                _registeredCurrentDwell = false;
                return;
            }

            if (zone == MirrorZone.None || _registeredCurrentDwell) return;
            if (Time.time - _zoneEnteredAt < dwellTime) return;
            if (Time.time - LastRegisteredAt() < cooldown) return;

            Register(zone);
        }

        private float LastRegisteredAt()
            => Mathf.Max(_lastLeftAt, Mathf.Max(_lastRightAt, _lastCentreAt));

        private void GetHeadAngles(out float yaw, out float pitch)
        {
            Quaternion vehicleRot = Quaternion.LookRotation(_vehicle.Forward, Vector3.up);
            Vector3 local = Quaternion.Inverse(vehicleRot) * headTransform.forward;
            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// CENTRE IS TESTED FIRST. The centre mirror sits about 23 deg to the
        /// driver's side of straight ahead — it is mounted on the car's
        /// centreline and the driver is not — so its yaw overlaps the door-mirror
        /// zones. What separates it is pitch: it is 23 deg UP, while the door
        /// mirrors are a couple of degrees down. Testing the door zones first
        /// meant a centre-mirror glance was filed as a door-mirror check, and the
        /// centre mirror could never be satisfied at all.
        /// </summary>
        private MirrorZone Classify(float yaw, float pitch)
        {
            if (yaw >= centreYawZone.x && yaw <= centreYawZone.y
                && -pitch >= centreMinPitchUp) return MirrorZone.Centre;

            if (yaw >= leftZone.x && yaw <= leftZone.y) return MirrorZone.Left;
            if (yaw >= rightZone.x && yaw <= rightZone.y) return MirrorZone.Right;
            return MirrorZone.None;
        }

        private void Register(MirrorZone zone)
        {
            float now = Time.time;

            switch (zone)
            {
                case MirrorZone.Left: LeftChecks++; _lastLeftAt = now; break;
                case MirrorZone.Right: RightChecks++; _lastRightAt = now; break;
                case MirrorZone.Centre: CentreChecks++; _lastCentreAt = now; break;
            }

            _registeredCurrentDwell = true;
            _checkFlag = true;
        }

        /// <summary>Reads and clears the one-shot flag. Called by the recorder.</summary>
        public bool ConsumeCheckFlag()
        {
            bool f = _checkFlag;
            _checkFlag = false;
            return f;
        }

        // ---- queries used by the cyclist scenario ----------------------------

        /// <summary>
        /// Seconds since the last check on this side, or a large number if that
        /// side has never been checked this session. Returned rather than only a
        /// boolean so the scenario can record HOW LATE the check was, which is
        /// more informative than whether it happened.
        /// </summary>
        public float SecondsSinceCheck(TrafficSide side)
        {
            float last = side == TrafficSide.Left ? _lastLeftAt : _lastRightAt;
            if (last <= NeverChecked) return float.MaxValue;
            return Time.time - last;
        }

        public float SecondsSinceCentreCheck()
            => _lastCentreAt <= NeverChecked ? float.MaxValue : Time.time - _lastCentreAt;

        /// <summary>
        /// Was a check on THIS side performed within the last `window` seconds?
        /// No fallback to the session counter: a check earlier in the drive is not
        /// a check for this manoeuvre.
        /// </summary>
        public bool CheckedSideRecently(TrafficSide side, float window)
            => SecondsSinceCheck(side) <= window;

        public void ResetCounters()
        {
            LeftChecks = RightChecks = CentreChecks = 0;
            _checkFlag = false;
            _currentZone = MirrorZone.None;
            _registeredCurrentDwell = false;
            _zoneEnteredAt = -99f;
            _lastLeftAt = _lastRightAt = _lastCentreAt = NeverChecked;
        }

        public void Configure(Transform head, MonoBehaviour vehicle)
        {
            headTransform = head;
            vehicleRef = vehicle;
        }
    }
}
