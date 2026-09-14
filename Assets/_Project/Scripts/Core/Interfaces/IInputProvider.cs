namespace IDS.Core
{
    /// <summary>
    /// One source of driving control input. The VehicleInputAdapter picks the
    /// highest-priority available provider each frame.
    ///
    /// OWNER: Aaditya. Implementations: WheelInputProvider (hand tracking),
    /// ControllerInputProvider (Quest controller taped to the prop),
    /// KeyboardInputProvider (desktop development).
    /// </summary>
    public interface IInputProvider
    {
        /// Higher wins when several providers are available at once.
        int Priority { get; }

        /// False when the provider cannot currently produce trustworthy input
        /// (e.g. hand tracking lost). The adapter falls through to the next one.
        bool IsAvailable { get; }

        string ProviderName { get; }

        /// -1 .. +1
        float ReadSteering();

        /// 0 .. 1
        float ReadAccelerator();

        /// 0 .. 1
        float ReadBrake();
    }
}
