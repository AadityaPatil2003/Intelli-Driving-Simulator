using System.Collections.Generic;
using UnityEngine;
using IDS.Core;
using IDS.Traffic;

namespace IDS.Scenarios
{
    /// <summary>
    /// Hazard 1 — pedestrian step-out. The gentlest of the three: one capsule,
    /// two waypoints and a trigger distance, with no dependency on the traffic
    /// system.
    ///
    /// What it probes: reaction time, and where attention was allocated. A driver
    /// scanning the road ahead sees it; a driver checking the wrong mirror does
    /// not.
    ///
    /// TRIGGERING IS BY FORWARD DISTANCE, NOT A TRIGGER VOLUME. A volume fires
    /// against the vehicle's collider bounds, so the effective trigger distance
    /// changes whenever the car model changes size — and every measurement here
    /// depends on the hazard appearing at a consistent distance.
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
        // 1.8 m/s, not 1.4. The crossing is now the full 9.2 m width of the road,
        // and at 1.4 m/s that takes 6.6 s against an activeDuration of 6 s, so the
        // scenario timed out mid-crossing. Brisk is also the realistic pace for
        // somebody stepping out in front of a car.
        [SerializeField] private float walkSpeedMetresPerSec = 1.8f;

        [Header("Placement")]
        [Tooltip("How far ahead of the car the crossing is moved when the "
                 + "scenario arms.")]
        [SerializeField] private float spawnAheadMetres = 90f;

        [Tooltip("Placement clamp. Must stay AHEAD of DriveEndZ (1310) or a "
                 + "hazard armed late in the drive gets clamped to a point the "
                 + "driver has already passed.")]
        [SerializeField] private float maxWorldZ = 1340f;

        [Tooltip("Closest approach beyond which the driver left no safe margin, " +
                 "even without a collision.")]
        [SerializeField] private float safeMarginMetres = 2.5f;

        protected override string DefaultScenarioId => "pedestrian_step_out";
        protected override SkillArea DefaultProbesSkill => SkillArea.HazardResponse;

        // ---- coaching --------------------------------------------------------

        public override string ProcedureTitle => "Pedestrian crossing";

        public override string[] ProcedureSteps => new[]
        {
            "Slow down on the approach to a marked crossing, before you can see "
            + "whether anyone is waiting.",
            "Scan both footpaths, not just your own side.",
            "If someone steps out, brake — do not steer around them.",
            "Leave at least 2.5 m and wait until they are fully across."
        };

        protected override string Briefing =>
            "CROSSING AHEAD — EASE OFF AND SCAN BOTH SIDES";

        protected override string Cue => "PEDESTRIAN — BRAKE, DO NOT SWERVE";

        protected override string Verdict(bool avoided)
        {
            if (Result.Collision) return "COLLISION — SLOW DOWN EARLIER NEXT TIME";

            if (avoided && Result.ReactionTime >= 0f)
                return $"STOPPED IN TIME — REACTED IN {Result.ReactionTime:F2}s";

            if (Result.ReactionTime < 0f)
                return "YOU NEVER BRAKED — A CROSSING NEEDS A COVERED BRAKE";

            return Result.HasMinimumDistance
                ? $"ONLY {Result.MinimumDistance:F1} m CLEARANCE — AIM FOR 2.5 m"
                : "TOO LATE — BRAKE EARLIER FOR A CROSSING";
        }

        protected override void Start()
        {
            base.Start();
            if (stepOutPoint == null && pedestrian != null)
                stepOutPoint = pedestrian.transform;
        }

        protected override void OnArmed()
        {
            if (pedestrian == null) return;

            PlaceCrossingAheadOfVehicle();

            pedestrian.SnapToStart();
            pedestrian.SpeedMetresPerSec = walkSpeedMetresPerSec;
        }

        /// <summary>
        /// Slides the whole crossing — both ends of the walk and the step-out
        /// point — up the road to sit ahead of wherever the car is now.
        ///
        /// Scenarios arm one at a time, in an order the adaptive selector picks,
        /// so there is no guarantee the car has not already driven past a fixed
        /// world position by the time this one's turn comes. A hazard that never
        /// fires also never resolves, and the queue stops: that is how a frozen
        /// pedestrian ended up preventing the cyclist from ever appearing.
        /// </summary>
        private void PlaceCrossingAheadOfVehicle()
        {
            if (Vehicle == null) return;

            Transform start = pedestrian.StartPoint;
            if (start == null) return;

            float targetZ = Mathf.Min(Vehicle.Position.z + spawnAheadMetres, maxWorldZ);

            // If the clamp has pulled the target back level with or behind the
            // car there is no road left to stage this on. Say so; the armed
            // timeout will abandon it and the queue moves on instead of stalling.
            if (targetZ <= Vehicle.Position.z + 20f)
            {
                Debug.LogWarning($"[{ScenarioId}] no room left to place the "
                                 + $"crossing (car at z={Vehicle.Position.z:F0}, "
                                 + $"clamp {maxWorldZ:F0}).");
                return;
            }

            float delta = targetZ - start.position.z;
            if (Mathf.Abs(delta) < 0.01f) return;

            // DE-DUPLICATE FIRST. The scene builder wires the follower's
            // startPoint and this scenario's stepOutPoint to the SAME Transform
            // (Configure(traffic.Pedestrian, env.PedestrianStart)), so the naive
            // loop shifted that one object twice.
            //
            // With shipped values that put the crossing roughly 125 m BEHIND the
            // driver instead of 90 m ahead, and left the end point only one delta
            // away — turning a 9.2 m walk across the road into a 215 m diagonal
            // down it. The pedestrian is hazard #1 of a first session, so it
            // could never trigger, never resolved, and blocked the queue: no
            // hazard fired at all in a new driver's first drive.
            var moved = new List<Transform>();

            foreach (Transform t in new[] { start, pedestrian.EndPoint, stepOutPoint })
            {
                if (t == null || moved.Contains(t)) continue;
                moved.Add(t);

                Vector3 p = t.position;
                p.z += delta;
                t.position = p;
            }
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

            // Once the pedestrian is across and behind the car the hazard is over;
            // holding the driver in a hazard state for the full duration would
            // stretch the HazardActive phase past the event it describes.
            if (pedestrian.HasArrived &&
                ForwardDistanceTo(pedestrian.transform.position) < -3f)
                Resolve();
        }

        /// <summary>
        /// Deliberately does NOT halt the pedestrian.
        ///
        /// Resolve fires either when the crossing is complete or when
        /// activeDuration runs out, and halting on the timeout left somebody
        /// standing frozen in the middle of the road for the rest of the drive.
        /// Letting them finish the walk costs nothing — they are off the
        /// carriageway in a couple of seconds — and OnReset puts them back.
        /// </summary>
        protected override void OnResolved() { }

        protected override void OnReset() => pedestrian?.SnapToStart();

        protected override bool DidAvoid()
        {
            // Avoided if the car never came closer than the safe margin, OR it
            // slowed meaningfully. A driver who stopped well short passes even if
            // the pedestrian then walked up to the bumper.
            bool keptMargin = Result.MinimumDistance > safeMarginMetres;
            bool slowedDown = Result.MinimumSpeed < Result.VehicleSpeedAtTrigger * 0.5f;
            return keptMargin || slowedDown;
        }

        public void Configure(NPCPathFollower walker, Transform point)
        {
            pedestrian = walker;
            stepOutPoint = point;
        }
    }
}
