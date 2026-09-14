using UnityEngine;
using IDS.Core;

namespace IDS.Telemetry
{
    /// <summary>
    /// Detects mirror/shoulder checks from HEAD ORIENTATION. Quest 3 gives us no
    /// eye tracking, so this is a proxy and the final report must call it one.
    /// It cannot tell a mirror glance from a head turn that happens to point the
    /// same way — that limitation is real and should be stated, not hidden.
    ///
    /// A valid check requires all three:
    ///   1. head yaw enters a mirror zone (relative to the vehicle's forward)
    ///   2. it stays there for at least dwellTime (a glance, not a sweep past)
    ///   3. cooldown since the last registered check has elapsed
    ///
    /// Without the dwell requirement, a single slow head turn registers dozens of
    /// checks. Without the cooldown, holding the head still in the zone registers
    /// one per frame. Both were real bugs; do not remove either.
    ///
    /// OWNER: Anushka.
    /// </summary>
    public class MirrorCheckDetector : MonoBehaviour
    {
        [Header("Zones - degrees of head yaw relative to vehicle forward")]
        [Tooltip("Left mirror / left shoulder. Negative yaw is left.")]
        [SerializeField] private Vector2 leftZone = new Vector2(-95f, -35f);
        [Tooltip("Right mirror / right shoulder.")]
        [SerializeField] private Vector2 rightZone = new Vector2(35f, 95f);
        [Tooltip("Centre (rear-view) mirror zone, mostly pitch-up and near-zero yaw.")]
        [SerializeField] private Vector2 centreYawZone = new Vector2(-12f, 12f);
        [SerializeField] private float centreMinPitchUp = 10f;

        [Header("Timing")]
        [SerializeField] private float dwellTime = 0.18f;
        [SerializeField] private float cooldown = 0.9f;

        [Header("Sources")]
        [SerializeField] private Transform headTransform;
        [SerializeField] private MonoBehaviour vehicleRef;  // IVehicleState

        public int LeftChecks   { get; private set; }
        public int RightChecks  { get; private set; }
        public int CentreChecks { get; private set; }
        public int TotalChecks => LeftChecks + RightChecks + CentreChecks;

        /// Set for one sample when a check is registered; the recorder consumes it.
        private bool _checkFlag;

        private IVehicleState _vehicle;
        private MirrorZone _currentZone = MirrorZone.None;
        private float _zoneEnteredAt = -99f;
        private float _lastRegisteredAt = -99f;
        private bool  _registeredCurrentDwell;

        private enum MirrorZone { None, Left, Right, Centre }

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;
        }

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
            if (Time.time - _lastRegisteredAt < cooldown) return;

            Register(zone);
        }

        private void GetHeadAngles(out float yaw, out float pitch)
        {
            Quaternion vehicleRot = Quaternion.LookRotation(_vehicle.Forward, Vector3.up);
            Vector3 local = Quaternion.Inverse(vehicleRot) * headTransform.forward;
            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private MirrorZone Classify(float yaw, float pitch)
        {
            if (yaw >= leftZone.x && yaw <= leftZone.y) return MirrorZone.Left;
            if (yaw >= rightZone.x && yaw <= rightZone.y) return MirrorZone.Right;
            if (yaw >= centreYawZone.x && yaw <= centreYawZone.y
                && -pitch >= centreMinPitchUp) return MirrorZone.Centre;
            return MirrorZone.None;
        }

        private void Register(MirrorZone zone)
        {
            switch (zone)
            {
                case MirrorZone.Left:   LeftChecks++;   break;
                case MirrorZone.Right:  RightChecks++;  break;
                case MirrorZone.Centre: CentreChecks++; break;
            }

            _lastRegisteredAt = Time.time;
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

        /// <summary>Was a check on this side performed in the last window seconds?
        /// Omkar's cyclist scenario uses this to score the blind-spot check.</summary>
        public bool CheckedSideRecently(TrafficSide side, float window)
        {
            if (Time.time - _lastRegisteredAt > window) return false;
            return side == TrafficSide.Left
                ? _currentZone == MirrorZone.Left  || LeftChecks  > 0
                : _currentZone == MirrorZone.Right || RightChecks > 0;
        }

        public void ResetCounters()
        {
            LeftChecks = RightChecks = CentreChecks = 0;
            _checkFlag = false;
            _lastRegisteredAt = -99f;
        }
    }
}
