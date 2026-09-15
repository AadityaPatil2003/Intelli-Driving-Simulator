using UnityEngine;
using IDS.Core;
using IDS.Traffic;
using IDS.Telemetry;
using IDS.AI;

namespace IDS.Scenarios
{
    /// <summary>
    /// Scenario 2 — cyclist in the blind spot. This is the scenario that most
    /// directly tests the project's core claim: a driver arriving from the
    /// opposite drive side checks the wrong shoulder, and this hazard catches it.
    ///
    /// What is measured, beyond collision:
    ///   - was a mirror/shoulder check performed on the KERB side before the
    ///     manoeuvre, where "kerb side" comes from the country profile
    ///   - the timing of that check relative to the steering input
    ///
    /// The check itself is a head-orientation proxy (see MirrorCheckDetector) and
    /// the report must describe it as one.
    ///
    /// SCENE SETUP:
    ///   CyclistScenario
    ///     ├── Cyclist (capsule + NPCPathFollower, path runs alongside the lane)
    ///     └── ManoeuvreZone (empty at the point where the driver must move over)
    ///
    /// OWNER: Omkar. Mirror detection: Anushka. Movement: Ananya.
    /// </summary>
    public class CyclistScenario : HazardScenarioBase
    {
        [Header("Cyclist")]
        [SerializeField] private NPCPathFollower cyclist;
        [SerializeField] private float cyclistSpeedMetresPerSec = 5.5f;

        [Header("Trigger")]
        [Tooltip("The cyclist appears when the vehicle is this far from the point " +
                 "where it will need to move over.")]
        [SerializeField] private Transform manoeuvreZone;
        [SerializeField] private float triggerDistanceMetres = 30f;
        [SerializeField] private float minSpeedToTriggerKph = 15f;

        [Header("Scoring")]
        [Tooltip("Steering magnitude that counts as beginning the manoeuvre.")]
        [SerializeField] private float manoeuvreSteeringThreshold = 0.25f;
        [Tooltip("Seconds before the manoeuvre within which a mirror check counts " +
                 "as having been made 'for' this manoeuvre.")]
        [SerializeField] private float checkWindowSeconds = 3f;
        [SerializeField] private float safePassingDistanceMetres = 1.5f;

        [Header("Sources")]
        [SerializeField] private MirrorCheckDetector mirrorDetector;
        [SerializeField] private CountryProfileManager countryProfiles;

        private bool _manoeuvreStarted;
        private float _manoeuvreTime;

        protected override void Start()
        {
            base.Start();
            scenarioId = "cyclist_blind_spot";
            probesSkill = SkillArea.MirrorAwareness;

            mirrorDetector ??= FindFirstObjectByType<MirrorCheckDetector>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
        }

        protected override void OnArmed()
        {
            cyclist?.SnapToStart();
            if (cyclist != null) cyclist.SpeedMetresPerSec = cyclistSpeedMetresPerSec;
            _manoeuvreStarted = false;
            _manoeuvreTime = -1f;
        }

        protected override bool ShouldTrigger()
        {
            if (Vehicle == null || manoeuvreZone == null) return false;
            if (Vehicle.CurrentSpeedKph < minSpeedToTriggerKph) return false;

            float forward = ForwardDistanceTo(manoeuvreZone.position);
            return forward > 0f && forward <= triggerDistanceMetres;
        }

        protected override void OnTriggered()
        {
            cyclist?.Begin();
            // Deliberately NO alert here. Alerting the driver to the cyclist would
            // destroy the measurement — the point is whether they check without
            // being told. The alert comes afterwards, in the report.
        }

        protected override void OnTick(float deltaTime)
        {
            if (cyclist == null) return;
            TrackMinimumDistance(cyclist.transform.position);

            if (_manoeuvreStarted) return;
            if (Vehicle == null) return;

            // Kerb side = the side the cyclist is on = opposite the centre line.
            TrafficSide kerbSide = countryProfiles?.Current != null
                ? countryProfiles.Current.priorityMirror
                : TrafficSide.Left;

            bool steeringAway =
                Mathf.Abs(Vehicle.SteeringInput) > manoeuvreSteeringThreshold;

            if (!steeringAway) return;

            _manoeuvreStarted = true;
            _manoeuvreTime = TimeSinceTrigger;

            Result.MirrorCheckPerformed = mirrorDetector != null &&
                mirrorDetector.CheckedSideRecently(kerbSide, checkWindowSeconds);

            Telemetry?.RecordEvent("cyclist_manoeuvre",
                $"check={Result.MirrorCheckPerformed}|side={kerbSide}|t={_manoeuvreTime:F2}");

            if (!Result.MirrorCheckPerformed)
                SessionEvents.RaiseAlert($"CHECK {kerbSide.ToString().ToUpper()} MIRROR",
                                         AlertPriority.Critical);
        }

        protected override void OnResolved()
        {
            cyclist?.Halt();
            Telemetry?.RecordEvent("cyclist_result",
                $"min_dist={Result.MinimumDistance:F2}|check={Result.MirrorCheckPerformed}");
        }

        protected override void OnReset() => cyclist?.SnapToStart();

        protected override bool DidAvoid()
        {
            // Success here is not merely "no collision". A driver who moved over
            // without checking got lucky, and this scenario exists to catch
            // exactly that.
            return Result.MinimumDistance > safePassingDistanceMetres
                   && (Result.MirrorCheckPerformed || !_manoeuvreStarted);
        }
    }
}
