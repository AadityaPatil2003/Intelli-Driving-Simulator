namespace IDS.Core
{
    public enum ScenarioState
    {
        Waiting,     // exists but not eligible yet
        Armed,       // eligible, waiting for the trigger condition
        Triggered,   // trigger fired this frame
        Active,      // hazard is playing out, driver is responding
        Resolved,    // outcome determined
        Logged       // result handed to telemetry, safe to reset
    }

    /// <summary>
    /// One hazard scenario. Implemented by Omkar (HazardScenarioBase), selected
    /// by Anushka's AdaptiveScenarioSelector, driven by the ScenarioManager.
    ///
    /// OWNER: Omkar.
    /// </summary>
    public interface IScenario
    {
        /// Stable token used by the adaptive selector and the report:
        /// "pedestrian_step_out", "cyclist_blind_spot", "lead_vehicle_brake".
        string ScenarioId { get; }

        ScenarioState State { get; }

        /// Which skill this hazard probes. The selector matches this against the
        /// driver's weakest skill.
        SkillArea ProbesSkill { get; }

        void Arm();
        void Trigger();
        void Tick(float deltaTime);
        void Resolve();
        void Reset();
        ScenarioResult GetResult();
    }
}
