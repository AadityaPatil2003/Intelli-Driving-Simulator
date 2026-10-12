using UnityEngine;
using IDS.Core;
using IDS.Traffic;

namespace IDS.Scenarios
{
    /// <summary>
    /// Hazard 3 — the vehicle ahead brakes suddenly. Tests following distance,
    /// which differs substantially between the traffic cultures these users
    /// arrive from, and braking reaction.
    ///
    /// Division of responsibility: NPCVehicleController knows HOW to brake; this
    /// scenario decides WHEN. It never touches the NPC's motion directly, which
    /// is what keeps hazard logic out of the traffic system.
    ///
    /// Headway is recorded in SECONDS as well as metres. Metres alone misleads —
    /// 12 m is fine at 20 km/h and dangerous at 50.
    /// </summary>
    public class LeadBrakeScenario : HazardScenarioBase
    {
        [Header("Lead vehicle")]
        [SerializeField] private NPCVehicleController leadVehicle;
        [SerializeField] private float leadCruiseSpeedKph = 40f;

        [Header("Trigger")]
        [Tooltip("Brake once the driver has been following within this many " +
                 "metres for holdSeconds. Following closely is the precondition: " +
                 "braking in front of a driver 60 m back measures nothing.")]
        [SerializeField] private float followDistanceMetres = 18f;
        [SerializeField] private float holdSeconds = 2f;
        [Tooltip("How far ahead of the driver the lead car is placed when this "
                 + "hazard arms.")]
        [SerializeField] private float spawnAheadMetres = 45f;

        [SerializeField] private float minSpeedToTriggerKph = 20f;

        [Header("Scoring")]
        [Tooltip("Time headway in seconds below which the driver was following " +
                 "too closely.")]
        [SerializeField] private float safeHeadwaySeconds = 2f;
        [SerializeField] private float safeStoppingMarginMetres = 3f;

        protected override string DefaultScenarioId => "lead_vehicle_brake";
        protected override SkillArea DefaultProbesSkill => SkillArea.HazardResponse;

        /// <summary>Exposed for the post-drive report.</summary>
        public float HeadwayAtTriggerSeconds => _headwayAtTriggerSeconds;

        public bool WasFollowingTooClosely => _headwayAtTriggerSeconds > 0f
                                              && _headwayAtTriggerSeconds < safeHeadwaySeconds;

        private float _followingFor;
        private float _headwayAtTriggerSeconds = -1f;

        // ---- coaching --------------------------------------------------------

        public override string ProcedureTitle => "Vehicle braking ahead";

        public override string[] ProcedureSteps => new[]
        {
            "Keep a two-second gap to the car in front — more if you are unsure "
            + "of the road.",
            "Watch its brake lights, not its bumper.",
            "Brake progressively and in a straight line.",
            "Rebuild the gap once it has settled, rather than closing straight "
            + "back up."
        };

        protected override string Briefing =>
            "TRAFFIC AHEAD — KEEP A TWO-SECOND GAP";

        protected override string Cue => "BRAKE LIGHTS — SLOW DOWN";

        protected override string Verdict(bool avoided)
        {
            if (Result.Collision) return "REAR-ENDED — YOUR GAP WAS TOO SMALL";

            if (Result.ReactionTime < 0f)
                return "YOU NEVER BRAKED — WATCH THE BRAKE LIGHTS";

            if (avoided)
                return $"GOOD — REACTED IN {Result.ReactionTime:F2}s";

            return Result.HasMinimumDistance
                ? $"LATE — {Result.ReactionTime:F2}s, AND ONLY "
                  + $"{Result.MinimumDistance:F1} m LEFT"
                : $"LATE — {Result.ReactionTime:F2}s";
        }

        protected override void OnArmed()
        {
            _followingFor = 0f;
            _headwayAtTriggerSeconds = -1f;

            if (leadVehicle == null) return;

            // Place it ahead of the DRIVER, not back at waypoint 0. See
            // NPCVehicleController.PlaceNear for why ResetToStart was wrong here.
            if (Vehicle != null)
                leadVehicle.PlaceNear(Vehicle.Position, spawnAheadMetres);
            else
                leadVehicle.ResetToStart();

            leadVehicle.SetTargetSpeed(leadCruiseSpeedKph);
        }

        protected override bool ShouldTrigger()
        {
            if (Vehicle == null || leadVehicle == null) return false;
            if (Vehicle.CurrentSpeedKph < minSpeedToTriggerKph)
            {
                _followingFor = 0f;
                return false;
            }

            float gap = ForwardDistanceTo(leadVehicle.transform.position);

            if (gap > 0f && gap <= followDistanceMetres)
                _followingFor += Time.deltaTime;
            else
                _followingFor = 0f;

            return _followingFor >= holdSeconds;
        }

        protected override void OnTriggered()
        {
            // The dev force-trigger path (H) can reach here with no lead vehicle
            // resolved; it used to null-dereference and kill the scenario tick.
            if (leadVehicle == null)
            {
                Debug.LogWarning($"[{ScenarioId}] triggered with no lead vehicle.");
                return;
            }

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
            _headwayAtTriggerSeconds = -1f;
            leadVehicle?.ResetToStart();
        }

        protected override bool DidAvoid()
            => !Result.Collision && Result.MinimumDistance > safeStoppingMarginMetres;

        public void Configure(NPCVehicleController lead) => leadVehicle = lead;
    }
}
