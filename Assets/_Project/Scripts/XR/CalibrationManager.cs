using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Binds the virtual wheel to the real prop. The driver sits down, puts both
    /// hands on the physical wheel at the 9-and-3 position, looks forward, and
    /// confirms. We store:
    ///
    ///   wheelCenter          midpoint between the two palms
    ///   wheelRadius          half the distance between them
    ///   wheelPlaneNormal     the axis the wheel rotates about (driver-facing)
    ///   wheelNeutralAngle    the hand angle that means "straight ahead"
    ///   seatPosition         head position at calibration
    ///   userForward          head forward, flattened to horizontal
    ///
    /// Everything is stored in XR Origin space, not world space, so a recentre
    /// does not invalidate the calibration.
    ///
    /// OWNER: Aaditya. Consumer: WheelInputProvider.
    /// </summary>
    public class CalibrationManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private Transform headTransform;   // the XR camera
        [SerializeField] private MonoBehaviour handProviderRef; // IHandTrackingProvider

        [Header("Defaults used when hands are unavailable")]
        [Tooltip("Offset from the head, in metres, where we assume the wheel is.")]
        [SerializeField] private Vector3 assumedWheelOffset = new Vector3(0f, -0.45f, 0.45f);
        [SerializeField] private float assumedWheelRadius = 0.17f;

        public bool IsCalibrated { get; private set; }

        public Vector3 WheelCenterLocal { get; private set; }
        public float   WheelRadius      { get; private set; } = 0.17f;
        public Vector3 WheelPlaneNormalLocal { get; private set; } = Vector3.forward;
        public float   WheelNeutralAngle { get; private set; }
        public Vector3 SeatPositionLocal { get; private set; }
        public Vector3 UserForwardLocal  { get; private set; } = Vector3.forward;

        private IHandTrackingProvider _hands;

        private void Awake()
        {
            _hands = handProviderRef as IHandTrackingProvider;
            ServiceRegistry.Register(this);
        }

        private void Start()
        {
            _hands ??= ServiceRegistry.Resolve<IHandTrackingProvider>();
            if (xrOrigin == null) xrOrigin = transform;
            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;
        }

        /// <summary>Called by Omkar's calibration panel Confirm button.</summary>
        public bool Calibrate()
        {
            if (headTransform == null)
            {
                Debug.LogError("[Calibration] no head transform — cannot calibrate.");
                return false;
            }

            SeatPositionLocal = ToLocal(headTransform.position);

            Vector3 fwd = headTransform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            UserForwardLocal = ToLocalDirection(fwd.normalized);

            bool bothHands = _hands != null
                             && _hands.IsLeftHandTracked && _hands.IsRightHandTracked;

            if (bothHands)
            {
                Vector3 l = ToLocal(_hands.LeftHandPose.position);
                Vector3 r = ToLocal(_hands.RightHandPose.position);

                WheelCenterLocal = (l + r) * 0.5f;
                WheelRadius = Mathf.Max(0.08f, Vector3.Distance(l, r) * 0.5f);

                // The wheel plane contains the hand-to-hand axis and is tilted
                // toward the driver. Normal = the rotation axis.
                Vector3 handAxis = (r - l).normalized;
                Vector3 up = Vector3.up;
                WheelPlaneNormalLocal = Vector3.Cross(handAxis, up).normalized;
                if (Vector3.Dot(WheelPlaneNormalLocal, UserForwardLocal) < 0f)
                    WheelPlaneNormalLocal = -WheelPlaneNormalLocal;

                WheelNeutralAngle = ComputeHandAngle(l, r);

                Debug.Log($"[Calibration] hands: centre={WheelCenterLocal} " +
                          $"radius={WheelRadius:F3}m neutral={WheelNeutralAngle:F1}°");
            }
            else
            {
                // Assumed geometry: still gives the driver something to steer,
                // and lets the controller/keyboard fallback take over cleanly.
                WheelCenterLocal = SeatPositionLocal
                                   + ToLocalDirection(headTransform.right) * assumedWheelOffset.x
                                   + Vector3.up * assumedWheelOffset.y
                                   + UserForwardLocal * assumedWheelOffset.z;
                WheelRadius = assumedWheelRadius;
                WheelPlaneNormalLocal = UserForwardLocal;
                WheelNeutralAngle = 0f;

                Debug.LogWarning("[Calibration] hands not tracked — assumed wheel " +
                                 "geometry used. Steering will need a fallback provider.");
            }

            IsCalibrated = true;
            SessionEvents.RaiseCalibrationCompleted();
            return true;
        }

        public void ClearCalibration() => IsCalibrated = false;

        /// <summary>
        /// Signed angle of the hand axis about the wheel plane normal, in degrees.
        /// Positive = clockwise from the driver's point of view = steering right.
        /// </summary>
        public float ComputeHandAngle(Vector3 leftLocal, Vector3 rightLocal)
        {
            Vector3 axis = (rightLocal - leftLocal);
            Vector3 normal = WheelPlaneNormalLocal;

            // Project the hand axis into the wheel plane.
            Vector3 inPlane = axis - Vector3.Dot(axis, normal) * normal;
            if (inPlane.sqrMagnitude < 0.0001f) return WheelNeutralAngle;

            // Reference direction in the plane: horizontal-right.
            Vector3 reference = Vector3.Cross(normal, Vector3.up);
            if (reference.sqrMagnitude < 0.0001f) reference = Vector3.right;
            reference.Normalize();

            return Vector3.SignedAngle(reference, inPlane.normalized, normal);
        }

        public Vector3 WheelCenterWorld => xrOrigin.TransformPoint(WheelCenterLocal);
        public Vector3 WheelNormalWorld => xrOrigin.TransformDirection(WheelPlaneNormalLocal);

        public Vector3 ToLocal(Vector3 world) => xrOrigin.InverseTransformPoint(world);
        public Vector3 ToLocalDirection(Vector3 world) => xrOrigin.InverseTransformDirection(world);

        private void OnDrawGizmos()
        {
            if (!IsCalibrated || xrOrigin == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(WheelCenterWorld, WheelRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(WheelCenterWorld, WheelNormalWorld * 0.2f);
        }
    }
}
