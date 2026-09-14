using UnityEngine;

namespace IDS.Core
{
    /// <summary>
    /// Hand poses in world space, abstracted away from XR Hands so that Omkar's
    /// UI and the wheel input can be tested without a headset.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public interface IHandTrackingProvider
    {
        bool IsLeftHandTracked { get; }
        bool IsRightHandTracked { get; }

        /// Palm pose. Identity-ish when not tracked — always check the flag first.
        Pose LeftHandPose { get; }
        Pose RightHandPose { get; }
    }
}
