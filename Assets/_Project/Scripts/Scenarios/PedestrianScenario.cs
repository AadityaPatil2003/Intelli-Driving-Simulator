using UnityEngine;
using IDS.Core;
using IDS.Traffic;

namespace IDS.Scenarios
{
    /// <summary>
    /// Scenario 1 — pedestrian step-out. The gentlest of the three and the first
    /// one to build, because it needs no traffic system: one capsule, two
    /// waypoints, a trigger distance.
    ///
    /// What it probes: reaction time and where attention was allocated. A driver
    /// scanning the road ahead sees it; a driver checking the wrong mirror does not.
    ///
    /// SCENE SETUP (Scenario_Test.unity):
    ///   PedestrianScenario (this script)
    ///     ├── Pedestrian (capsule, NPCPathFollower, trigger collider)
    ///     ├── StartPoint (empty, on the kerb)
    ///     └── EndPoint   (empty, across the lane)
    ///
    /// Trigger is by forward distance, not a trigger volume. A volume fires on the
    /// vehicle's collider bounds, which means the trigger distance changes if the
    /// car model changes size — and the whole measurement depends on the hazard
    /// appearing at a consistent distance.
    ///
    /// OWNER: Omkar. Movement infrastructure: Ananya.
    /// </summary>
    public class PedestrianScenario : HazardScenarioBase
    {
        [Header("Pedestrian")]
        [SerializeField] private NPCPathFollower pedestrian;
        [SerializeField] private Transform stepOutPoint;

        [Header("Trigger")]
        [Tooltip("Forward distance from the vehicle to the step-out point at " +
                 "which the pedestrian starts moving.")]
        [SerializeField] private float triggerDistanceMetres = 22f;
        [Tooltip("Do not trigger below this speed — a stationary car makes the " +
                 "reaction measurement meaningless.")]
        [SerializeField] private float minSpeedToTriggerKph = 15f;

        [Header("Difficulty")]
        [SerializeField] private float walkSpeedMetresPerSec = 1.4f;
        [Tooltip("Distance at which we consider the driver to have failed to " +
                 "leave a safe margin, even without a collision.")]
        [SerializeField] private float safeMarginMetres = 2.5f;

        protected override void Start()
        {
            base.Start();
            scenarioId = string.IsNullOrEmpty(scenarioId) || scenarioId == "unnamed_scenario"
                ? "pedestrian_step_out" : scenarioId;

            if (stepOutPoint == null && pedestrian != null)
                stepOutPoint = pedestrian.transform;
        }

        protected override void OnArmed()
        {
            pedestrian?.SnapToStart();
            if (pedestrian != null) pedestrian.SpeedMetresPerSec = walkSpeedMetresPerSec;
        }

        protected override bool ShouldTrigger()
        {
            if (Vehicle == null || stepOutPoint == null) return false;
            if (Vehicle.CurrentSpeedKph < minSpeedToTriggerKph) return false;

            float forward = ForwardDistanceTo(stepOutPoint.position);
            return forward > 0f && forward <= triggerDistanceMetres;
        }

        protected override void OnTriggered()
        {
            pedestrian?.Begin();
            SessionEvents.RaiseAlert("PEDESTRIAN", AlertPriority.Critical);
        }

        protected override void OnTick(float deltaTime)
        {
            if (pedestrian == null) return;
            TrackMinimumDistance(pedestrian.transform.position);

            // Once the pedestrian is across and behind us, the hazard is over —
            // no point holding the driver in a hazard state for the full duration.
            if (pedestrian.HasArrived && ForwardDistanceTo(pedestrian.transform.position) < -3f)
                Resolve();
        }

        protected override void OnResolved()
        {
            pedestrian?.Halt();
        }

        protected override void OnReset()
        {
            pedestrian?.SnapToStart();
        }

        protected override bool DidAvoid()
        {
            // Avoided if we never got closer than the safe margin, OR we slowed
            // meaningfully. A driver who stopped well short passes even if the
            // pedestrian then walked right up to the bumper.
            bool keptMargin = Result.MinimumDistance > safeMarginMetres;
            bool slowedDown = Result.MinimumSpeed < Result.VehicleSpeedAtTrigger * 0.5f;
            return keptMargin || slowedDown;
        }
    }
}
