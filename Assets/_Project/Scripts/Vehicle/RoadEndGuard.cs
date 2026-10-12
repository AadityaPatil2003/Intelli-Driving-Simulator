using UnityEngine;
using IDS.Core;

namespace IDS.Vehicle
{
    /// <summary>
    /// Ends the drive before the car can leave the road surface.
    ///
    /// This exists because of a specific failure: telemetry from a real session
    /// showed pos_z reaching 386 m on a 400 m road, then pos_y dropping to
    /// −1.562 — the car had driven off the end and was falling. From inside the
    /// headset that reads as the world tilting and vanishing, and the last ten
    /// seconds of every such session are recorded as driver behaviour.
    ///
    /// Two independent guards, because the first one can be outrun:
    ///
    ///   1. DISTANCE. Past endZ, the drive ends normally and the driver sees the
    ///      report. This is the intended path.
    ///   2. ALTITUDE. Below minY the car is already falling, so it is reset to
    ///      the start and the drive ends. Not a graceful outcome, but it is
    ///      bounded, logged, and marked in the telemetry so the session can be
    ///      excluded from analysis rather than silently included.
    /// </summary>
    public class RoadEndGuard : MonoBehaviour
    {
        [Header("Bounds")]
        [Tooltip("Ending the drive once the car's Z exceeds this. Set to the road " +
                 "length minus a margin long enough to stop in.")]
        [SerializeField] private float endZ = 1320f;

        [Tooltip("Below this height the car has left the surface entirely.")]
        [SerializeField] private float minY = -3f;

        [Tooltip("Lateral distance from the road centre beyond which the car has " +
                 "left the surface sideways. 0 disables the check.")]
        [SerializeField] private float maxAbsX = 40f;

        [Header("Sources")]
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState
        [SerializeField] private VehicleController vehicle;
        [SerializeField] private Transform resetTo;

        private IVehicleState _state;
        private SessionManager _session;
        private bool _fired;

        private void Start()
        {
            _state = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            vehicle ??= ServiceRegistry.Resolve<VehicleController>();
            _session = ServiceRegistry.Resolve<SessionManager>();
        }

        private void OnEnable() => SessionEvents.SessionStarted += OnSessionStarted;
        private void OnDisable() => SessionEvents.SessionStarted -= OnSessionStarted;

        private void OnSessionStarted(string _) => _fired = false;

        private void Update()
        {
            if (_fired || _state == null || _session == null) return;
            if (_session.Phase != SessionPhase.Driving &&
                _session.Phase != SessionPhase.HazardActive) return;

            Vector3 p = _state.Position;

            if (p.y < minY)
            {
                Fire("the car left the road surface and was falling", recover: true);
                return;
            }

            if (maxAbsX > 0f && Mathf.Abs(p.x) > maxAbsX)
            {
                Fire("the car left the road surface sideways", recover: true);
                return;
            }

            if (p.z >= endZ)
                Fire("reached the end of the training route", recover: false);
        }

        private void Fire(string reason, bool recover)
        {
            _fired = true;

            ServiceRegistry.Resolve<ITelemetrySink>()?
                .RecordEvent(recover ? "off_road_abort" : "route_complete", reason);

            // END THE DRIVE FIRST, THEN TELEPORT.
            //
            // The old order teleported the car and ended the drive afterwards, so
            // the recorder — which samples until the drive ends — captured the
            // jump back to the start line as a frame of driving. That is a
            // 1,300 m displacement in one sample: it wrecks the speed and lane
            // statistics for the session and, because the result is written into
            // the stored driver profile, poisons the "improved by N points"
            // comparison for every session after it.
            _session.EndDrive();

            if (recover && vehicle != null && resetTo != null)
            {
                vehicle.ResetTo(resetTo.position, resetTo.rotation);
                SessionEvents.RaiseAlert("DRIVE ENDED", AlertPriority.Warning);
                Debug.LogWarning($"[RoadEnd] {reason} — drive ended, then car reset. " +
                                 "This session's telemetry carries an off_road_abort " +
                                 "event and should not be pooled with clean runs.");
            }
            else
            {
                Debug.Log($"[RoadEnd] {reason} — ending the drive.");
            }
        }

        public void Configure(float roadEndZ, Transform startPoint)
        {
            endZ = roadEndZ;
            resetTo = startPoint;
        }
    }
}
