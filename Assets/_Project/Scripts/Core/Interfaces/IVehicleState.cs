using UnityEngine;

namespace IDS.Core
{
    /// <summary>
    /// Everything the rest of the project is allowed to know about the vehicle.
    /// Implemented by Ananya's VehicleController. Consumed by Anushka's telemetry
    /// and Omkar's scenarios and HUD.
    ///
    /// OWNER: Ananya (implementation) — do not change this interface without
    /// telling Anushka and Omkar, their code compiles against it.
    /// </summary>
    public interface IVehicleState
    {
        /// -1 (full left) .. +1 (full right)
        float SteeringInput { get; }

        /// 0 .. 1
        float AcceleratorInput { get; }

        /// 0 .. 1
        float BrakeInput { get; }

        /// Signed forward speed in km/h. Negative when reversing.
        float CurrentSpeedKph { get; }

        Vector3 Position { get; }

        /// World forward of the vehicle body.
        Vector3 Forward { get; }

        /// True for the frame(s) a collision is in contact.
        bool IsColliding { get; }

        /// Total collisions this session. Reset by ResetSessionCounters().
        int CollisionCount { get; }

        void ResetSessionCounters();
    }
}
