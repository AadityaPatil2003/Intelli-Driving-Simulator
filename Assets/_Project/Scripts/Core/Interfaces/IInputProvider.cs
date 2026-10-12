namespace IDS.Core
{
    /// <summary>
    /// One source of driving control input. VehicleInputAdapter picks the
    /// highest-priority available provider each frame, so a provider dropping out
    /// mid-corner falls through to the next one instead of snapping the car
    /// straight.
    ///
    /// Implementations, by priority:
    ///
    ///     VRSteeringWheel        90   grab-and-rotate wheel + triggers (primary)
    ///     ControllerInputProvider 50   thumbstick + triggers, no grab needed
    ///     KeyboardInputProvider    1   desktop development only
    ///
    /// A provider must report IsAvailable false rather than returning zeros when
    /// it cannot produce trustworthy input. Returning zeros looks identical to
    /// "the driver is doing nothing", which is how an input failure gets recorded
    /// as driver behaviour.
    /// </summary>
    public interface IInputProvider
    {
        /// Higher wins when several providers are available at once.
        int Priority { get; }

        /// False when this provider cannot currently produce trustworthy input.
        bool IsAvailable { get; }

        string ProviderName { get; }

        /// True if this provider supplies accelerator and brake as well as
        /// steering. The wheel supplies both; a hypothetical steering-only
        /// provider would return false and the adapter would take pedals from
        /// the next provider down.
        bool SuppliesPedals { get; }

        /// -1 (full left) .. +1 (full right)
        float ReadSteering();

        /// 0 .. 1
        float ReadAccelerator();

        /// 0 .. 1
        float ReadBrake();
    }
}
