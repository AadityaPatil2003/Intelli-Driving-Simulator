using UnityEngine;
using IDS.Core;
using IDS.UI;

namespace IDS.Scenarios
{
    /// <summary>
    /// Shared lifecycle for every hazard. Three scenarios each inventing their own
    /// state handling is three sets of bugs; here the state machine is debugged
    /// once.
    ///
    ///     Waiting → Armed → Triggered → Active → Resolved → Logged
    ///
    /// Subclasses implement four things:
    ///     ShouldTrigger()   the condition that fires the hazard
    ///     OnTriggered()     start the hazard moving
    ///     OnTick()          measure the driver's response
    ///     OnResolved()      decide the outcome, clean up
    ///
    /// Reaction time is measured here, not in each subclass, because the
    /// definition is the same every time — trigger to first meaningful brake
    /// input — and it is the number the whole evaluation leans on.
    ///
    /// SCENARIO IDS ARE RESOLVED IN Awake, NOT Start. ScenarioManager.Start reads
    /// every scenario's ScenarioId to tell the adaptive selector what exists in
    /// the scene. Unity does not define the order of Start between objects, so
    /// when a scenario set its own id in Start it was still "unnamed_scenario" as
    /// often as not — the selector then planned ids that matched nothing and
    /// ScenarioManager skipped every hazard with "planned 'x' is not in the
    /// scene". Awake always completes before any Start, so resolving the id there
    /// removes the race rather than hiding it behind an execution-order setting.
    /// </summary>
    public abstract class HazardScenarioBase : MonoBehaviour, IScenario
    {
        [Header("Identity")]
        [Tooltip("Normally left empty — each scenario supplies its own id. Set " +
                 "this only to run two differently tuned copies of one hazard.")]
        [SerializeField] protected string scenarioId = "";

        [SerializeField] protected SkillArea probesSkill = SkillArea.HazardResponse;

        [Header("Timing")]
        [Tooltip("Seconds the hazard stays Active before auto-resolving.")]
        [SerializeField] protected float activeDuration = 6f;

        [Tooltip("Seconds a hazard may sit Armed waiting for its trigger before "
                 + "it gives up. Nothing bounded this state, and the queue only "
                 + "advances when something resolves — so one hazard whose "
                 + "trigger had become unsatisfiable blocked every hazard behind "
                 + "it for the rest of the drive, silently.")]
        [SerializeField] protected float armedTimeout = 40f;

        [Header("Response thresholds")]
        [Tooltip("Brake input above this counts as a response.")]
        [SerializeField] protected float brakeResponseThreshold = 0.15f;

        [Header("Sources (auto-resolved)")]
        [SerializeField] protected MonoBehaviour vehicleRef;   // IVehicleState

        /// <summary>The id this scenario uses when none is set in the inspector.
        /// Must be a compile-time constant per subclass, not computed state.</summary>
        protected abstract string DefaultScenarioId { get; }

        /// <summary>Which skill this hazard probes. Overridden per subclass.</summary>
        protected abstract SkillArea DefaultProbesSkill { get; }

        // ---- coaching --------------------------------------------------------
        // This is a training tool, so every hazard teaches its procedure rather
        // than silently marking it. Each subclass supplies three short lines and
        // the base class fires them at the right moments; DriveCoach decides
        // whether they are spoken at all, so an assessment run stays silent.

        /// <summary>What the driver should be getting ready to do. On arm.</summary>
        protected virtual string Briefing => null;

        /// <summary>The step to take right now. On trigger.</summary>
        protected virtual string Cue => null;

        /// <summary>What they actually did. On resolve, while they remember it.</summary>
        protected virtual string Verdict(bool avoided) => null;

        /// <summary>
        /// The procedure, in order, for the pre-drive briefing panel. Written as
        /// instructions to the driver, not as a description of the scoring.
        /// </summary>
        public virtual string[] ProcedureSteps => System.Array.Empty<string>();

        /// <summary>A short name for this hazard, for the briefing panel.</summary>
        public virtual string ProcedureTitle => ScenarioId;

        public string ScenarioId => scenarioId;
        public ScenarioState State { get; private set; } = ScenarioState.Waiting;
        public SkillArea ProbesSkill => probesSkill;

        protected IVehicleState Vehicle;
        protected ITelemetrySink Telemetry;
        protected ScenarioResult Result;

        private float _triggerTime;
        private float _activeElapsed;
        private float _armedElapsed;

        protected virtual void Awake()
        {
            if (string.IsNullOrWhiteSpace(scenarioId) || scenarioId == "unnamed_scenario")
                scenarioId = DefaultScenarioId;

            probesSkill = DefaultProbesSkill;
            Result = ScenarioResult.Empty(scenarioId);
        }

        protected virtual void Start()
        {
            Vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Require<IVehicleState>("vehicle");
            Telemetry = ServiceRegistry.Resolve<ITelemetrySink>();
        }

        // ---- IScenario -------------------------------------------------------

        public void Arm()
        {
            if (State != ScenarioState.Waiting) return;
            State = ScenarioState.Armed;
            _armedElapsed = 0f;
            Result = ScenarioResult.Empty(scenarioId);
            OnArmed();
            DriveCoach.Tell(Briefing);
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
            DriveCoach.Tell(Cue, AlertPriority.Warning);

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
                    if (ShouldTrigger()) { Trigger(); break; }

                    _armedElapsed += deltaTime;
                    if (armedTimeout > 0f && _armedElapsed >= armedTimeout) Abandon();
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

            // Immediately, and jumping the queue. The verdict is the teachable
            // moment and it is worth nothing once the driver has stopped
            // thinking about what they just did.
            DriveCoach.TellNow(Verdict(Result.SuccessfulAvoidance),
                               Result.SuccessfulAvoidance
                                   ? AlertPriority.Info : AlertPriority.Warning);

            SessionEvents.RaiseScenarioResolved(Result);
            ServiceRegistry.Resolve<SessionManager>()?.NotifyHazardActive(false);
            State = ScenarioState.Logged;

            Debug.Log($"[{scenarioId}] resolved — collision={Result.Collision} " +
                      $"avoided={Result.SuccessfulAvoidance} " +
                      $"react={Result.ReactionTime:F2}s " +
                      $"min_dist={Result.MinimumDistance:F1}m");
        }

        /// <summary>
        /// Gives up on a hazard whose trigger never became satisfiable, and
        /// advances the queue.
        ///
        /// Result.Completed stays FALSE. That distinction matters: an abandoned
        /// hazard must not be scored as a driver who failed to react, and must
        /// not appear in the report as something that happened. It is a hazard
        /// that never occurred.
        /// </summary>
        public void Abandon()
        {
            if (State != ScenarioState.Armed) return;

            State = ScenarioState.Logged;
            Result.Completed = false;
            Result.SuccessfulAvoidance = false;

            OnReset();

            Debug.LogWarning($"[{scenarioId}] abandoned after {armedTimeout:F0}s "
                             + "armed without its trigger condition being met. The "
                             + "queue has moved on.");

            Telemetry?.RecordEvent("scenario_abandoned", scenarioId);
            SessionEvents.RaiseScenarioResolved(Result);
        }

        public void Reset()
        {
            // If a drive ends mid-hazard, the session is still flagged as being
            // in a hazard. Left set, the HUD stays stuck on "HAZARD" and the lane
            // alerts stay suppressed for the whole of the next drive.
            if (State == ScenarioState.Triggered || State == ScenarioState.Active)
                ServiceRegistry.Resolve<SessionManager>()?.NotifyHazardActive(false);

            State = ScenarioState.Waiting;
            _activeElapsed = 0f;
            _armedElapsed = 0f;
            Result = ScenarioResult.Empty(scenarioId);
            OnReset();
        }

        public ScenarioResult GetResult() => Result;

        // ---- subclass hooks --------------------------------------------------

        /// <summary>Condition that fires the hazard. Called every frame while Armed.</summary>
        protected abstract bool ShouldTrigger();

        /// <summary>Start the hazard: move the pedestrian, brake the lead car.</summary>
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
