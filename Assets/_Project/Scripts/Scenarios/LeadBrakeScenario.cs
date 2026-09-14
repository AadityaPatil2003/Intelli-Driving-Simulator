using UnityEngine;
using IDS.Core;
using IDS.Traffic;

namespace IDS.Scenarios
{
    /// <summary>
    /// Scenario 3 — the vehicle ahead brakes suddenly. Tests following distance,
    /// which differs substantially between the traffic cultures our users arrive
    /// from, and braking reaction.
    ///
    /// Division of responsibility: Ananya's NPCVehicleController knows HOW to
    /// brake; this scenario decides WHEN. It never touches the NPC's physics.
    ///
    /// Headway is recorded in SECONDS as well as metres. Metres alone is
    /// misleading — 12 m is fine at 20 km/h and dangerous at 50.
    ///
    /// OWNER: Omkar. NPC vehicle: Ananya.
    /// </summary>
    public class LeadBrakeScenario : HazardScenarioBase
    {
        [Header("Lead vehicle")]
        [SerializeField] private NPCVehicleController leadVehicle;
        [SerializeField] private float leadCruiseSpeedKph = 40f;

        [Header("Trigger")]
        [Tooltip("Brake when the driver has been following within this many " +
                 "metres for holdSeconds. Following closely is the precondition — " +
                 "braking in front of a driver 60 m back measures nothing.")]
        [SerializeField] private float followDistanceMetres = 18f;
        [SerializeField] private float holdSeconds = 2f;
        [SerializeField] private float minSpeedToTriggerKph = 20f;

        [Header("Scoring")]
        [Tooltip("Time headway in seconds below which following was too close.")]
        [SerializeField] private float safeHeadwaySeconds = 2f;
        [SerializeField] private float safeStoppingMarginMetres = 3f;

        private float _followingFor;
        private float _headwayAtTriggerSeconds;

        protected override void Start()
        {
            base.Start();
            scenarioId = "lead_vehicle_brake";
            probesSkill = SkillArea.HazardResponse;
        }

        protected override void OnArmed()
        {
            _followingFor = 0f;
            if (leadVehicle != null)
            {
                leadVehicle.ResetToStart();
                leadVehicle.SetTargetSpeed(leadCruiseSpeedKph);
            }
        }

        protected override bool ShouldTrigger()
        {
            if (Vehicle == null || leadVehicle == null) return false;
            if (Vehicle.CurrentSpeedKph < minSpeedToTriggerKph) return false;

            float gap = ForwardDistanceTo(leadVehicle.transform.position);

            if (gap > 0f && gap <= followDistanceMetres)
                _followingFor += Time.deltaTime;
            else
                _followingFor = 0f;

            return _followingFor >= holdSeconds;
        }

        protected override void OnTriggered()
        {
            float gap = ForwardDistanceTo(leadVehicle.transform.position);
            float speedMs = Mathf.Max(0.5f, Vehicle.CurrentSpeedKph / 3.6f);
            _headwayAtTriggerSeconds = gap / speedMs;

            leadVehicle.EmergencyBrake();

            Telemetry?.RecordEvent("lead_brake_triggered",
                $"gap={gap:F1}m|headway={_headwayAtTriggerSeconds:F2}s|" +
                $"speed={Vehicle.CurrentSpeedKph:F1}");
        }

        protected override void OnTick(float deltaTime)
        {
            if (leadVehicle == null) return;
            TrackMinimumDistance(leadVehicle.transform.position);

            // Resolve early once both vehicles have effectively stopped.
            if (Vehicle.CurrentSpeedKph < 2f && leadVehicle.CurrentSpeedKph < 2f
                && TimeSinceTrigger > 1.5f)
                Resolve();
        }

        protected override void OnResolved()
        {
            leadVehicle?.ResumeDriving();
            Telemetry?.RecordEvent("lead_brake_result",
                $"headway={_headwayAtTriggerSeconds:F2}s|" +
                $"min_dist={Result.MinimumDistance:F2}m|" +
                $"react={Result.ReactionTime:F2}s|collision={Result.Collision}");
        }

        protected override void OnReset()
        {
            _followingFor = 0f;
            leadVehicle?.ResetToStart();
        }

        protected override bool DidAvoid()
            => !Result.Collision && Result.MinimumDistance > safeStoppingMarginMetres;

        /// <summary>Exposed for the post-drive report.</summary>
        public float HeadwayAtTriggerSeconds => _headwayAtTriggerSeconds;
        public bool WasFollowingTooClosely => _headwayAtTriggerSeconds > 0f
                                              && _headwayAtTriggerSeconds < safeHeadwaySeconds;
    }
}
