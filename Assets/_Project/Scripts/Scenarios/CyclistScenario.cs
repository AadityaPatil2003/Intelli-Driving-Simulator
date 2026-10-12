using UnityEngine;
using IDS.Core;
using IDS.Traffic;
using IDS.Telemetry;
using IDS.AI;

namespace IDS.Scenarios
{
    /// <summary>
    /// Hazard 2 — cyclist in the blind spot. This is the scenario that most
    /// directly tests the project's central claim: a driver arriving from the
    /// opposite drive side checks the wrong shoulder, and this hazard catches it.
    ///
    /// Measured, beyond collision:
    ///   - whether a mirror or shoulder check was made on the KERB side before
    ///     the manoeuvre, with "kerb side" coming from the country profile
    ///   - how long before the manoeuvre that check happened
    ///
    /// The check is a head-orientation proxy (see MirrorCheckDetector) and the
    /// report must describe it as one.
    ///
    /// NOTE ON A PREVIOUS DEFECT: this scenario's pass/fail rested on
    /// MirrorCheckDetector.CheckedSideRecently, which used to fall back to a
    /// session-wide counter — one check anywhere in the drive, on either side,
    /// satisfied it. Every participant passed the blind-spot test regardless of
    /// where they looked. The detector now keeps per-side timestamps with no
    /// counter fallback, so results from before that fix cannot be compared with
    /// results after it, and the report must not pool them.
    /// </summary>
    public class CyclistScenario : HazardScenarioBase
    {
        [Header("Cyclist")]
        [SerializeField] private NPCPathFollower cyclist;
        // 4.0 m/s = 14.4 km/h, a realistic casual cyclist. At the old 5.5 m/s
        // (19.8 km/h) a driver doing 25 km/h closed the gap at 1.5 m/s and took
        // 69 s to reach the trigger — past the armed timeout, so the hazard was
        // abandoned every time. Tuned together with minSpeedToTriggerKph and
        // spawnAheadMetres so the hazard fires between 20 and 50 km/h.
        [SerializeField] private float cyclistSpeedMetresPerSec = 4.0f;

        [Header("Trigger")]
        [Tooltip("The cyclist appears when the vehicle is this far from the point " +
                 "where it will need to move over.")]
        [SerializeField] private Transform manoeuvreZone;
        [SerializeField] private float triggerDistanceMetres = 30f;

        [Header("Placement")]
        [Tooltip("How far ahead of the car the cyclist is put when the scenario "
                 + "arms. The car closes at roughly 8 m/s, so this is about "
                 + "fifteen seconds of approach.")]
        [SerializeField] private float spawnAheadMetres = 90f;

        [Tooltip("How much road the cyclist is given to ride after that.")]
        [SerializeField] private float ridingRunMetres = 500f;

        [Tooltip("Placement clamp. Must stay AHEAD of where a drive can end "
                 + "(DriveEndZ = 1310), or a hazard armed late in the drive is "
                 + "clamped to a point the driver has already passed.")]
        [SerializeField] private float maxWorldZ = 1340f;

        [Tooltip("Below this the hazard will not fire. It MUST exceed the "
                 + "cyclist's own speed (4.0 m/s = 14.4 km/h) or a driver "
                 + "cruising between the two can never catch them, the hazard "
                 + "never fires, and the queue stalls behind it.")]
        [SerializeField] private float minSpeedToTriggerKph = 18f;

        [Header("Scoring")]
        [Tooltip("Steering magnitude that counts as beginning the manoeuvre.")]
        [SerializeField] private float manoeuvreSteeringThreshold = 0.25f;

        [Tooltip("A mirror check within this many seconds before the manoeuvre " +
                 "counts as having been made for it.")]
        [SerializeField] private float checkWindowSeconds = 3f;

        [SerializeField] private float safePassingDistanceMetres = 1.5f;

        [Header("Sources")]
        [SerializeField] private MirrorCheckDetector mirrorDetector;
        [SerializeField] private CountryProfileManager countryProfiles;

        protected override string DefaultScenarioId => "cyclist_blind_spot";
        protected override SkillArea DefaultProbesSkill => SkillArea.MirrorAwareness;

        /// <summary>Seconds between the kerb-side check and the manoeuvre, or -1
        /// if there was no check. Exposed for the report.</summary>
        public float CheckLeadSeconds { get; private set; } = -1f;

        private bool _manoeuvreStarted;
        private float _manoeuvreTime = -1f;

        // ---- coaching --------------------------------------------------------

        public override string ProcedureTitle => "Passing a cyclist";

        public override string[] ProcedureSteps => new[]
        {
            "Check the mirror on the cyclist's side BEFORE you move out — "
            + "within about three seconds of steering, not earlier.",
            "Signal, then move out smoothly. Do not swerve.",
            "Leave at least 1.5 m between your car and the bike as you pass.",
            "Return to your lane only once the cyclist is clear in your mirror."
        };

        protected override string Briefing =>
            "CYCLIST AHEAD — MIRROR FIRST, THEN MOVE OUT";

        protected override string Cue => KerbMirrorCue();

        protected override string Verdict(bool avoided)
        {
            // A driver who never moved out must not be told they moved out
            // without checking. The scoring already knew the difference —
            // _manoeuvreStarted — but the feedback ignored it and accused people
            // of a mistake they had not made, which is worse than silence in a
            // training tool.
            if (!_manoeuvreStarted)
                return Result.HasMinimumDistance
                       && Result.MinimumDistance < safePassingDistanceMetres
                    ? $"YOU STAYED IN LANE — ONLY {Result.MinimumDistance:F1} m TO THE CYCLIST"
                    : "YOU HELD YOUR LANE AND GAVE THEM ROOM";

            if (!Result.MirrorCheckPerformed)
                return "YOU MOVED OUT WITHOUT A MIRROR CHECK";

            if (Result.HasMinimumDistance
                && Result.MinimumDistance < safePassingDistanceMetres)
                return $"TOO CLOSE — {Result.MinimumDistance:F1} m, AIM FOR 1.5 m";

            return CheckLeadSeconds >= 0f
                ? $"GOOD — CHECKED {CheckLeadSeconds:F1}s BEFORE MOVING OUT"
                : "GOOD — MIRROR, THEN MOVED OUT";
        }

        private string KerbMirrorCue()
        {
            TrafficSide kerb = countryProfiles?.Current != null
                ? countryProfiles.Current.priorityMirror
                : TrafficSide.Left;

            return $"CHECK YOUR {kerb.ToString().ToUpper()} MIRROR NOW";
        }

        protected override void Start()
        {
            base.Start();
            mirrorDetector ??= ServiceRegistry.Resolve<MirrorCheckDetector>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();

            if (mirrorDetector == null)
                Debug.LogError("[cyclist_blind_spot] no MirrorCheckDetector — the " +
                               "blind-spot check cannot be measured, and this " +
                               "scenario's result would be meaningless. Fix the " +
                               "scene rather than reporting the number.");
        }

        protected override void OnArmed()
        {
            if (cyclist != null)
            {
                cyclist.SnapToStart();
                cyclist.SpeedMetresPerSec = cyclistSpeedMetresPerSec;

                // Begin riding on ARM, not on trigger. Arming happens as the
                // driver approaches, so by the time the cyclist is in view they
                // are already moving. Starting them at trigger left a cyclist
                // standing motionless in the kerb lane for the whole approach,
                // which reads as a bug and, worse, as scenery — the driver stops
                // treating them as traffic, and the blind-spot check this
                // scenario exists to measure never gets a fair test.
                PlaceCyclistAheadOfVehicle();
                cyclist.Begin();
            }

            _manoeuvreStarted = false;
            _manoeuvreTime = -1f;
            CheckLeadSeconds = -1f;
        }

        /// <summary>
        /// Puts the cyclist on the road ahead of the car and gives them a long
        /// run to ride, rather than leaving them parked at a fixed world
        /// position. Fixed placement meant that if this scenario armed after the
        /// car had already passed that spot, the cyclist sat behind the driver
        /// and the hazard never happened.
        /// </summary>
        private void PlaceCyclistAheadOfVehicle()
        {
            if (Vehicle == null || cyclist == null) return;

            Transform start = cyclist.StartPoint;
            Transform end = cyclist.EndPoint;
            if (start == null) return;

            float targetZ = Mathf.Min(Vehicle.Position.z + spawnAheadMetres, maxWorldZ);

            if (targetZ <= Vehicle.Position.z + 20f)
            {
                Debug.LogWarning($"[{ScenarioId}] no room left to place the "
                                 + $"cyclist (car at z={Vehicle.Position.z:F0}, "
                                 + $"clamp {maxWorldZ:F0}).");
                return;
            }

            Vector3 s = start.position;
            s.z = targetZ;
            start.position = s;

            if (end != null)
            {
                Vector3 e = end.position;
                e.z = targetZ + ridingRunMetres;
                end.position = e;
            }

            cyclist.SnapToStart();
        }

        /// <summary>
        /// Triggers on closing on the CYCLIST, not on reaching a fixed point on
        /// the map. The hazard is "you are catching a cyclist and will have to go
        /// around them", and that is a fact about the gap between two moving
        /// things.
        /// </summary>
        protected override bool ShouldTrigger()
        {
            if (Vehicle == null || cyclist == null) return false;
            if (Vehicle.CurrentSpeedKph < minSpeedToTriggerKph) return false;

            float forward = ForwardDistanceTo(cyclist.transform.position);
            return forward > 0f && forward <= triggerDistanceMetres;
        }

        protected override void OnTriggered()
        {
            // Already riding since OnArmed; this only matters if they somehow
            // reached the end of their run.
            if (cyclist != null && !cyclist.IsMoving) cyclist.Begin();

            // Deliberately NO alert. Telling the driver about the cyclist destroys
            // the measurement — the point is whether they check without being
            // told. The feedback comes afterwards, in the report.
        }

        protected override void OnTick(float deltaTime)
        {
            if (cyclist == null) return;
            TrackMinimumDistance(cyclist.transform.position);

            if (_manoeuvreStarted || Vehicle == null) return;

            if (Mathf.Abs(Vehicle.SteeringInput) <= manoeuvreSteeringThreshold) return;

            // Kerb side — the side the cyclist is on — comes from the profile.
            TrafficSide kerbSide = countryProfiles?.Current != null
                ? countryProfiles.Current.priorityMirror
                : TrafficSide.Left;

            _manoeuvreStarted = true;
            _manoeuvreTime = TimeSinceTrigger;

            float sinceCheck = mirrorDetector != null
                ? mirrorDetector.SecondsSinceCheck(kerbSide)
                : float.MaxValue;

            Result.MirrorCheckPerformed = sinceCheck <= checkWindowSeconds;
            CheckLeadSeconds = Result.MirrorCheckPerformed ? sinceCheck : -1f;

            Telemetry?.RecordEvent("cyclist_manoeuvre",
                $"check={Result.MirrorCheckPerformed}|side={kerbSide}" +
                $"|lead={CheckLeadSeconds:F2}|t={_manoeuvreTime:F2}");

            if (!Result.MirrorCheckPerformed)
                SessionEvents.RaiseAlert($"CHECK {kerbSide.ToString().ToUpper()} MIRROR",
                                         AlertPriority.Critical);
        }

        protected override void OnResolved()
        {
            // Not halted. The scenario resolving means the measurement is over,
            // not that the cyclist has stopped existing — freezing them in the
            // kerb lane behind the driver is both wrong and, if the driver looks
            // in a mirror, obviously wrong.
            Telemetry?.RecordEvent("cyclist_result",
                $"min_dist={Result.MinimumDistance:F2}|check={Result.MirrorCheckPerformed}" +
                $"|lead={CheckLeadSeconds:F2}|manoeuvred={_manoeuvreStarted}");
        }

        protected override void OnReset() => cyclist?.SnapToStart();

        protected override bool DidAvoid()
        {
            // Success is not merely "no collision". A driver who moved over
            // without checking got lucky, and this scenario exists to catch
            // exactly that. A driver who never moved over is not penalised for a
            // manoeuvre they did not make.
            return Result.MinimumDistance > safePassingDistanceMetres
                   && (Result.MirrorCheckPerformed || !_manoeuvreStarted);
        }

        public void Configure(NPCPathFollower rider, Transform zone)
        {
            cyclist = rider;
            manoeuvreZone = zone;
        }
    }
}
