using System.Collections.Generic;
using UnityEngine;
using IDS.Core;
using IDS.Traffic;

namespace IDS.Telemetry
{
    /// <summary>
    /// Measures the time gap the driver accepts when crossing a conflict point at
    /// the intersection. This is the metric the plan warns not to fabricate: with
    /// no crossing traffic in the scene it reports "no measurement" rather than a
    /// plausible-looking number, and the scorer scores an unmeasured gap neutral
    /// rather than punishing a driver for a scenario the session did not contain.
    ///
    ///     ego reaches the conflict point at t_ego
    ///     the nearest cross-traffic vehicle would reach it at t_cross
    ///     gap = t_cross − t_ego      (positive = ego went first, with margin)
    ///
    /// Smaller gap = more aggressive. Below the profile's safeGapSeconds counts as
    /// an unsafe acceptance.
    ///
    /// SCENE SETUP: on a GameObject at the intersection with a trigger collider
    /// covering the ego's approach, with the cross-traffic NPCs listed.
    ///
    /// EGO-ONLY TRIGGER EXIT. OnTriggerExit previously cleared the "ego is
    /// approaching" flag for ANY collider leaving the volume — so the first NPC,
    /// pedestrian or cyclist to leave cancelled the measurement while the ego was
    /// still inside it. Since the volume sits at an intersection, which is exactly
    /// where other traffic is, the measurement was usually cancelled before it
    /// could be taken. Entry was already ego-checked; exit now is too.
    /// </summary>
    public class GapAcceptanceDetector : MonoBehaviour
    {
        [SerializeField] private Transform conflictPoint;
        [SerializeField] private List<NPCVehicleController> crossTraffic = new();

        [Tooltip("Cross traffic further than this from the conflict point is " +
                 "ignored — it is not a conflict, it is scenery.")]
        [SerializeField] private float relevanceRadius = 60f;

        [Tooltip("Distance from the conflict point at which the ego counts as " +
                 "having committed to the crossing.")]
        [SerializeField] private float commitRadius = 2.5f;

        [Header("Sources")]
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState

        /// All accepted gaps this session, in seconds.
        public List<float> AcceptedGaps { get; } = new();
        public bool HasMeasurement => AcceptedGaps.Count > 0;

        private IVehicleState _vehicle;
        private Transform _egoRoot;
        private bool _egoInApproach;
        private bool _recordedThisApproach;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            _egoRoot = (_vehicle as MonoBehaviour)?.transform;
            if (conflictPoint == null) conflictPoint = transform;
        }

        private void OnEnable() => SessionEvents.SessionStarted += OnSessionStarted;
        private void OnDisable() => SessionEvents.SessionStarted -= OnSessionStarted;

        /// <summary>
        /// Gaps are per session. Without this, a second drive inherits the first
        /// drive's gaps and MinimumAcceptedGap reports the tightest gap across
        /// both — which reads as the driver having got worse.
        /// </summary>
        private void OnSessionStarted(string sessionId) => ResetSession();

        // ---- approach tracking -----------------------------------------------

        private void OnTriggerEnter(Collider other)
        {
            if (!IsEgo(other)) return;
            _egoInApproach = true;
            _recordedThisApproach = false;
        }

        private void OnTriggerExit(Collider other)
        {
            // Ego only. Anything else leaving this volume is irrelevant.
            if (!IsEgo(other)) return;
            _egoInApproach = false;
        }

        /// <summary>
        /// True when the collider belongs to the ego vehicle. Checked through the
        /// attached rigidbody's transform, because the ego's colliders are
        /// children of the vehicle root, not the root itself.
        /// </summary>
        private bool IsEgo(Collider other)
        {
            if (_egoRoot == null)
            {
                _egoRoot = (_vehicle as MonoBehaviour)?.transform;
                if (_egoRoot == null) return false;
            }

            Rigidbody body = other.attachedRigidbody;
            if (body != null) return body.transform == _egoRoot;

            // No rigidbody on the collider: fall back to a hierarchy test.
            return other.transform.IsChildOf(_egoRoot);
        }

        private void Update()
        {
            if (!_egoInApproach || _recordedThisApproach || _vehicle == null) return;

            // Record at the moment the ego commits — crosses the point.
            float egoDistance = Vector3.Distance(_vehicle.Position, conflictPoint.position);
            if (egoDistance > commitRadius) return;

            float gap = ComputeNearestCrossGap();

            if (float.IsPositiveInfinity(gap))
            {
                // No relevant cross traffic. Not a measurement — do not invent one.
                _recordedThisApproach = true;
                ServiceRegistry.Resolve<ITelemetrySink>()?
                    .RecordEvent("gap_not_measured", "no relevant cross traffic");
                return;
            }

            AcceptedGaps.Add(gap);
            _recordedThisApproach = true;

            ServiceRegistry.Resolve<ITelemetrySink>()?
                .RecordEvent("gap_accepted", gap.ToString("F2"));

            Debug.Log($"[Gap] accepted gap {gap:F2}s");
        }

        private float ComputeNearestCrossGap()
        {
            float smallest = float.PositiveInfinity;

            foreach (NPCVehicleController npc in crossTraffic)
            {
                if (npc == null) continue;

                float d = Vector3.Distance(npc.transform.position, conflictPoint.position);
                if (d > relevanceRadius) continue;

                // Only traffic still approaching the point, not already past it.
                Vector3 toConflict = conflictPoint.position - npc.transform.position;
                if (toConflict.sqrMagnitude < 0.01f) continue;
                if (Vector3.Dot(toConflict.normalized, npc.transform.forward) < 0.3f) continue;

                float speedMs = Mathf.Max(0.5f, npc.CurrentSpeedKph / 3.6f);
                float timeToConflict = d / speedMs;

                if (timeToConflict < smallest) smallest = timeToConflict;
            }

            return smallest;
        }

        public float MeanAcceptedGap()
        {
            if (AcceptedGaps.Count == 0) return -1f;
            float sum = 0f;
            foreach (float g in AcceptedGaps) sum += g;
            return sum / AcceptedGaps.Count;
        }

        /// <summary>The tightest gap taken — the one that actually indicates risk.</summary>
        public float MinimumAcceptedGap()
        {
            if (AcceptedGaps.Count == 0) return -1f;
            float min = float.MaxValue;
            foreach (float g in AcceptedGaps) if (g < min) min = g;
            return min;
        }

        public void ResetSession()
        {
            AcceptedGaps.Clear();
            _recordedThisApproach = false;
            _egoInApproach = false;
        }

        public void Configure(Transform point, IEnumerable<NPCVehicleController> traffic)
        {
            conflictPoint = point;
            crossTraffic.Clear();
            crossTraffic.AddRange(traffic);
        }

        private void OnDrawGizmosSelected()
        {
            Transform point = conflictPoint != null ? conflictPoint : transform;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(point.position, commitRadius);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.2f);
            Gizmos.DrawWireSphere(point.position, relevanceRadius);
        }
    }
}
