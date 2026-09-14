using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// PRIMARY input: steering estimated from the angle of the two tracked hands
    /// about the calibrated wheel centre.
    ///
    ///     left hand + right hand
    ///        → hand axis angle in the wheel plane
    ///        → minus the neutral angle
    ///        → divided by the max rotation
    ///        → steeringInput  -1 .. +1
    ///
    /// Accelerator and brake are NOT derived from the hands. There is no reliable
    /// hand signal for a pedal, and inventing one would corrupt the reaction-time
    /// measurement, which is the metric the whole hazard evaluation rests on.
    /// Pedals come from the controller trigger or the keyboard until the team has
    /// real pedal hardware. This is a deliberate decision, documented here so it
    /// does not get quietly "fixed" later.
    ///
    /// OWNER: Aaditya. Spike gate: prove this before Week 7, or fall back.
    /// </summary>
    public class WheelInputProvider : MonoBehaviour, IInputProvider
    {
        [SerializeField] private CalibrationManager calibration;
        [SerializeField] private MonoBehaviour handProviderRef;

        [Header("Steering response")]
        [Tooltip("Physical wheel rotation, in degrees each way, that maps to full lock.")]
        [SerializeField] private float maxWheelRotation = 120f;

        [Tooltip("Degrees of hand movement ignored around neutral. Stops the car " +
                 "wandering from hand-tracking noise while the driver holds still.")]
        [SerializeField] private float deadZoneDegrees = 3f;

        [Tooltip("Low-pass smoothing. Higher = smoother but laggier. Keep low, " +
                 "steering lag is a comfort problem in XR.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float smoothing = 0.12f;

        [Header("Sanity limits")]
        [Tooltip("Reject a frame where the hands are further apart than this " +
                 "multiple of the calibrated wheel diameter — the driver has let go.")]
        [SerializeField] private float maxHandSeparationFactor = 1.8f;

        public int Priority => 100;
        public string ProviderName => "Hand-tracked wheel";

        public bool IsAvailable =>
            calibration != null && calibration.IsCalibrated &&
            _hands != null && _hands.IsLeftHandTracked && _hands.IsRightHandTracked &&
            _handsPlausible;

        private IHandTrackingProvider _hands;
        private float _smoothedSteering;
        private bool  _handsPlausible;

        /// Exposed for the HUD and for debugging the spike.
        public float RawWheelAngleDegrees { get; private set; }

        private void Awake() => _hands = handProviderRef as IHandTrackingProvider;

        private void Start()
        {
            _hands ??= ServiceRegistry.Resolve<IHandTrackingProvider>();
            calibration ??= ServiceRegistry.Resolve<CalibrationManager>();
        }

        private void Update()
        {
            if (_hands == null || calibration == null || !calibration.IsCalibrated)
            {
                _handsPlausible = false;
                return;
            }

            if (!_hands.IsLeftHandTracked || !_hands.IsRightHandTracked)
            {
                _handsPlausible = false;
                return;
            }

            Vector3 l = calibration.ToLocal(_hands.LeftHandPose.position);
            Vector3 r = calibration.ToLocal(_hands.RightHandPose.position);

            float separation = Vector3.Distance(l, r);
            float expected = calibration.WheelRadius * 2f;
            _handsPlausible = separation <= expected * maxHandSeparationFactor;
            if (!_handsPlausible) return;

            float angle = calibration.ComputeHandAngle(l, r);
            RawWheelAngleDegrees = Mathf.DeltaAngle(calibration.WheelNeutralAngle, angle);

            float magnitude = Mathf.Abs(RawWheelAngleDegrees);
            float target = magnitude <= deadZoneDegrees
                ? 0f
                : Mathf.Sign(RawWheelAngleDegrees)
                  * Mathf.Clamp01((magnitude - deadZoneDegrees)
                                  / Mathf.Max(1f, maxWheelRotation - deadZoneDegrees));

            _smoothedSteering = smoothing <= 0f
                ? target
                : Mathf.Lerp(_smoothedSteering, target, 1f - Mathf.Exp(-Time.deltaTime / smoothing));
        }

        public float ReadSteering() => Mathf.Clamp(_smoothedSteering, -1f, 1f);

        // Deliberately zero — see the class comment.
        public float ReadAccelerator() => 0f;
        public float ReadBrake() => 0f;
    }
}
