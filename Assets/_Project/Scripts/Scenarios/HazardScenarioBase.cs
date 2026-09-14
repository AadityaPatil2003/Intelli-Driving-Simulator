using UnityEngine;
using IDS.Core;

namespace IDS.Scenarios
{
    /// <summary>
    /// Shared lifecycle for every hazard. Three scenarios that each invent their
    /// own state handling is three sets of bugs; this base class means the state
    /// machine is debugged once.
    ///
    ///   Waiting → Armed → Triggered → Active → Resolved → Logged
    ///
    /// Subclasses implement four things:
    ///   ShouldTrigger()   the condition that fires the hazard
    ///   OnTriggered()     start the hazard moving
    ///   OnTick()          measure the driver's response
    ///   OnResolved()      decide the outcome, clean up
    ///
    /// Reaction time is measured here rather than in each subclass, because it is
    /// the same definition every time — time from trigger to first meaningful
    /// brake input — and it is the number the whole evaluation leans on.
    ///
    /// OWNER: Omkar.
    /// </summary>
    public abstract class HazardScenarioBase : MonoBehaviour, IScenario
    {
        [Header("Identity")]
        [SerializeField] protected string scenarioId = "unnamed_scenario";
        [SerializeField] protected SkillArea probesSkill = SkillArea.HazardResponse;

        [Header("Timing")]
        [Tooltip("Seconds the hazard stays Active before auto-resolving.")]
        [SerializeField] protected float activeDuration = 6f;

        [Header("Response thresholds")]
        [Tooltip("Brake input above this counts as a response.")]
        [SerializeField] protected float brakeResponseThreshold = 0.15f;

        [Header("Sources (auto-resolved)")]
        [SerializeField] protected MonoBehaviour vehicleRef;   // IVehicleState

        public string ScenarioId => scenarioId;
        public ScenarioState State { get; private set; } = ScenarioState.Waiting;
        public SkillArea ProbesSkill => probesSkill;

        protected IVehicleState Vehicle;
        protected ITelemetrySink Telemetry;
        protected ScenarioResult Result;

        private float _triggerTime;
        private float _activeElapsed;

        protected virtual void Start()
        {
            Vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Require<IVehicleState>("Ananya");
            Telemetry = ServiceRegistry.Resolve<ITelemetrySink>();
            Result = ScenarioResult.Empty(scenarioId);
        }

        // ---- IScenario -------------------------------------------------------

        public void Arm()
        {
            if (State != ScenarioState.Waiting) return;
            State = ScenarioState.Armed;
            Result = ScenarioResult.Empty(scenarioId);
            OnArmed();
            SessionEvents.RaiseScenarioArmed(scenarioId);
        }

        public void Trigger()
        {
            if (State != ScenarioState.Armed) return;

            State = ScenarioState.Triggered;
            _triggerTime = Time.time;
            _activeElapsed = 0f;

            Result.TriggerTime = _triggerTime;
            Result.VehicleSpeedAtTrigger = Vehicle?.CurrentSpeedKph ?? 0f;
            Result.MinimumSpeed = Result.VehicleSpeedAtTrigger;

            OnTriggered();

            State = ScenarioState.Active;
            SessionEvents.RaiseScenarioTriggered(scenarioId);
            ServiceRegistry.Resolve<SessionManager>()?.NotifyHazardActive(true);

            Debug.Log($"[{scenarioId}] triggered at {Result.VehicleSpeedAtTrigger:F1} km/h");
        }

        public void Tick(float deltaTime)
        {
            switch (State)
            {
                case ScenarioState.Armed:
                    if (ShouldTrigger()) Trigger();
                    break;

                case ScenarioState.Active:
                    _activeElapsed += deltaTime;
                    TrackResponse();
                    OnTick(deltaTime);
                    if (_activeElapsed >= activeDuration) Resolve();
                    break;
            }
        }

        private void TrackResponse()
        {
            if (Vehicle == null) return;

            // First brake, once, ever.
            if (Result.FirstBrakeTime < 0f && Vehicle.BrakeInput >= brakeResponseThreshold)
            {
                Result.FirstBrakeTime = Time.time - _triggerTime;
                Result.ReactionTime = Result.FirstBrakeTime;
                Debug.Log($"[{scenarioId}] reaction {Result.ReactionTime:F2}s");
            }

            if (Vehicle.CurrentSpeedKph < Result.MinimumSpeed)
                Result.MinimumSpeed = Vehicle.CurrentSpeedKph;

            if (Vehicle.IsColliding) Result.Collision = true;
        }

        public void Resolve()
        {
            if (State != ScenarioState.Active) return;

            State = ScenarioState.Resolved;
            Result.Completed = true;
            Result.SuccessfulAvoidance = !Result.Collision && DidAvoid();

            OnResolved();

            SessionEvents.RaiseScenarioResolved(Result);
            ServiceRegistry.Resolve<SessionManager>()?.NotifyHazardActive(false);
            State = ScenarioState.Logged;

            Debug.Log($"[{scenarioId}] resolved — collision={Result.Collision} " +
                      $"avoided={Result.SuccessfulAvoidance} " +
                      $"react={Result.ReactionTime:F2}s min_dist={Result.MinimumDistance:F1}m");
        }

        public void Reset()
        {
            State = ScenarioState.Waiting;
            _activeElapsed = 0f;
            Result = ScenarioResult.Empty(scenarioId);
            OnReset();
        }

        public ScenarioResult GetResult() => Result;

        // ---- subclass hooks --------------------------------------------------

        /// <summary>Condition that fires the hazard. Called every frame while Armed.</summary>
        protected abstract bool ShouldTrigger();

        /// <summary>Start the hazard. Move the pedestrian, brake the lead car.</summary>
        protected abstract void OnTriggered();

        /// <summary>Per-frame measurement while the hazard plays out.</summary>
        protected virtual void OnTick(float deltaTime) { }

        /// <summary>Outcome decided, hazard finished.</summary>
        protected virtual void OnResolved() { }

        protected virtual void OnArmed() { }
        protected virtual void OnReset() { }

        /// <summary>Scenario-specific definition of success beyond "no collision".</summary>
        protected virtual bool DidAvoid() => true;

        // ---- helpers for subclasses ------------------------------------------

        protected float TimeSinceTrigger => State == ScenarioState.Active
            ? Time.time - _triggerTime : 0f;

        protected void TrackMinimumDistance(Vector3 hazardPosition)
        {
            if (Vehicle == null) return;
            float d = Vector3.Distance(Vehicle.Position, hazardPosition);
            if (d < Result.MinimumDistance) Result.MinimumDistance = d;
        }

        /// <summary>Distance along the vehicle's forward axis. Negative = behind.</summary>
        protected float ForwardDistanceTo(Vector3 point)
        {
            if (Vehicle == null) return float.MaxValue;
            return Vector3.Dot(point - Vehicle.Position, Vehicle.Forward);
        }
    }
}
