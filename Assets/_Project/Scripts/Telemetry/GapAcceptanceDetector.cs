using System.Collections.Generic;
using UnityEngine;
using IDS.Core;
using IDS.Traffic;

namespace IDS.Telemetry
{
    /// <summary>
    /// Measures the time gap the driver accepts when crossing a conflict point at
    /// the intersection. This is the metric the master plan warns not to fabricate
    /// — if there is no crossing traffic in the scene, it reports "no measurement"
    /// rather than a plausible-looking number.
    ///
    ///     ego reaches conflict point at t_ego
    ///     nearest cross-traffic vehicle would reach it at t_cross
    ///     gap = t_cross - t_ego     (positive = ego went first, with margin)
    ///
    /// Smaller gap = more aggressive. Below the profile's safeGapSeconds counts
    /// as an unsafe acceptance.
    ///
    /// SCENE SETUP: put this on a GameObject at the intersection with a trigger
    /// collider covering the ego approach, and list the cross-traffic NPCs.
    ///
    /// OWNER: Anushka. Depends on Ananya's intersection + traffic.
    /// </summary>
    public class GapAcceptanceDetector : MonoBehaviour
    {
        [SerializeField] private Transform conflictPoint;
        [SerializeField] private List<NPCVehicleController> crossTraffic = new();
        [Tooltip("Cross-traffic further than this from the conflict point is " +
                 "ignored — it is not a conflict, it is scenery.")]
        [SerializeField] private float relevanceRadius = 60f;

        [Header("Sources")]
        [SerializeField] private MonoBehaviour vehicleRef;  // IVehicleState

        /// All accepted gaps this session, in seconds.
        public List<float> AcceptedGaps { get; } = new();
        public bool HasMeasurement => AcceptedGaps.Count > 0;

        private IVehicleState _vehicle;
        private bool _egoInApproach;
        private bool _recordedThisApproach;

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            if (conflictPoint == null) conflictPoint = transform;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_vehicle == null) return;
            if (!other.attachedRigidbody ||
                other.attachedRigidbody.transform != FindEgoRoot()) return;

            _egoInApproach = true;
            _recordedThisApproach = false;
        }

        private void OnTriggerExit(Collider other) => _egoInApproach = false;

        private Transform FindEgoRoot()
        {
            var mb = _vehicle as MonoBehaviour;
            return mb != null ? mb.transform : null;
        }

        private void Update()
        {
            if (!_egoInApproach || _recordedThisApproach || _vehicle == null) return;

            // Record at the moment the ego actually commits — crosses the point.
            float egoDistance = Vector3.Distance(_vehicle.Position, conflictPoint.position);
            if (egoDistance > 2.5f) return;

            float gap = ComputeNearestCrossGap();
            if (float.IsPositiveInfinity(gap))
            {
                // No relevant cross traffic. Not a measurement — do not invent one.
                _recordedThisApproach = true;
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

            foreach (var npc in crossTraffic)
            {
                if (npc == null) continue;

                float d = Vector3.Distance(npc.transform.position, conflictPoint.position);
                if (d > relevanceRadius) continue;

                float speedMs = Mathf.Max(0.5f, npc.CurrentSpeedKph / 3.6f);

                // Only count traffic still approaching, not already past.
                Vector3 toConflict = conflictPoint.position - npc.transform.position;
                if (Vector3.Dot(toConflict.normalized, npc.transform.forward) < 0.3f) continue;

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
        }
    }
}
