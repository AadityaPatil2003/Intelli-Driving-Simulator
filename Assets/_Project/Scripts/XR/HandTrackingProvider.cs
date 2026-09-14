using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Wraps the XR Hands subsystem behind IHandTrackingProvider. Uses the palm
    /// joint, which is the most stable joint when the hands are wrapped around a
    /// physical object — fingertips drop out constantly against a wheel rim.
    ///
    /// Also holds a short grace period: if tracking blinks for a few frames we
    /// keep reporting the last good pose rather than dropping the steering input
    /// to zero, which would feel like the wheel had been yanked out of the
    /// driver's hands.
    ///
    /// OWNER: Aaditya. Consumers: WheelInputProvider, Omkar's UI hand cursor.
    /// </summary>
    public class HandTrackingProvider : MonoBehaviour, IHandTrackingProvider
    {
        [Tooltip("Seconds of tracking loss tolerated before the hand is reported " +
                 "as untracked.")]
        [SerializeField] private float trackingGracePeriod = 0.25f;

        private XRHandSubsystem _subsystem;
        private static readonly List<XRHandSubsystem> Subsystems = new();

        private Pose _leftPose = Pose.identity, _rightPose = Pose.identity;
        private float _leftLastSeen = -99f, _rightLastSeen = -99f;

        public bool IsLeftHandTracked  => Time.time - _leftLastSeen  <= trackingGracePeriod;
        public bool IsRightHandTracked => Time.time - _rightLastSeen <= trackingGracePeriod;
        public Pose LeftHandPose  => _leftPose;
        public Pose RightHandPose => _rightPose;

        /// True when the subsystem itself is missing (desktop editor, usually).
        public bool SubsystemAvailable => _subsystem != null;

        private void Awake() => ServiceRegistry.Register<IHandTrackingProvider>(this);

        private void OnEnable()
        {
            SubsystemManager.GetSubsystems(Subsystems);
            foreach (var s in Subsystems)
            {
                if (!s.running) continue;
                _subsystem = s;
                break;
            }

            if (_subsystem == null)
            {
                Debug.LogWarning("[Hands] no running XRHandSubsystem — hand input " +
                                 "unavailable, fallbacks will be used.");
                return;
            }

            _subsystem.updatedHands += OnUpdatedHands;
        }

        private void OnDisable()
        {
            if (_subsystem != null) _subsystem.updatedHands -= OnUpdatedHands;
        }

        private void OnUpdatedHands(XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags flags,
            XRHandSubsystem.UpdateType updateType)
        {
            // Only act on the dynamic (render) update; the fixed update fires too
            // often and we do not need physics-rate hand data.
            if (updateType != XRHandSubsystem.UpdateType.Dynamic) return;

            TryRead(subsystem.leftHand,  ref _leftPose,  ref _leftLastSeen);
            TryRead(subsystem.rightHand, ref _rightPose, ref _rightLastSeen);
        }

        private void TryRead(XRHand hand, ref Pose pose, ref float lastSeen)
        {
            if (!hand.isTracked) return;

            var joint = hand.GetJoint(XRHandJointID.Palm);
            if (!joint.TryGetPose(out Pose local)) return;

            // Joint poses are in XR Origin space; convert to world.
            Transform origin = transform;
            pose = new Pose(
                origin.TransformPoint(local.position),
                origin.rotation * local.rotation);
            lastSeen = Time.time;
        }
    }
}
