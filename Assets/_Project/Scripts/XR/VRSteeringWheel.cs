using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// The primary driving control: a virtual wheel the driver grabs with the
    /// controllers and physically turns, with the triggers as pedals.
    ///
    ///     grab        hold the trigger or grip near the wheel rim
    ///     steer       rotate your hand around the wheel while holding
    ///     accelerate  right trigger
    ///     brake       left trigger
    ///
    /// When the wheel is not held, the left thumbstick's X axis steers, so a
    /// driver who cannot reach or hold the wheel is never stuck. That fallback is
    /// recorded in telemetry through the active-provider name, so a session driven
    /// on the thumbstick is not later mistaken for one driven on the wheel.
    ///
    /// GRAB DETECTION IS BY PROXIMITY, NOT A COLLIDER. A trigger collider on a
    /// wheel rim that lives inside a moving rigidbody generates contacts against
    /// the car's own interior, and the resulting grab state flickers. Distance
    /// from the rim is one Vector3.Distance and cannot flicker.
    ///
    /// STEERING IS ACCUMULATED, NOT ABSOLUTE. The angle is integrated from the
    /// hand's motion around the wheel axis while held, so re-gripping does not
    /// snap the wheel, and letting go leaves the wheel where it was before it
    /// self-centres.
    ///
    /// SCENE REQUIREMENT: the wheel visual's PARENT must carry the steering
    /// column's tilt and must not itself rotate with the steering. The hand angle
    /// is measured in the parent's local space, where the wheel then lies in the
    /// XY plane. Parent the wheel directly to the cabin instead and a tilted
    /// column measures a projected angle; parent it to something that rotates
    /// with the steering and the wheel's own rotation feeds back into the
    /// measurement and it runs away on its own.
    ///
    ///     SteeringColumn   (tilted, static)
    ///       └── Wheel      (this component's wheelVisual, rotates about local Z)
    /// </summary>
    public class VRSteeringWheel : MonoBehaviour, IInputProvider
    {
        [Header("Wheel")]
        [Tooltip("The wheel's visual transform. Rotated about its local Z axis to " +
                 "match the steering angle.")]
        [SerializeField] private Transform wheelVisual;

        [Tooltip("Rim radius in metres. Used for the grab test and to convert " +
                 "hand motion into rotation.")]
        [SerializeField] private float rimRadius = 0.17f;

        [Tooltip("How far from the rim a hand may be and still grab it.")]
        [SerializeField] private float grabTolerance = 0.12f;

        [Header("Steering")]
        [Tooltip("Wheel rotation in degrees that corresponds to full lock.")]
        [SerializeField] private float degreesForFullLock = 140f;

        [Tooltip("Degrees per second the wheel returns to centre when released.")]
        [SerializeField] private float selfCentringDegPerSec = 90f;

        [Tooltip("Thumbstick steering rate, in units of full lock per second, " +
                 "used only while the wheel is not held.")]
        [SerializeField] private float thumbstickRate = 1.6f;

        [Tooltip("Thumbstick deflection below this is ignored.")]
        [SerializeField] private float thumbstickDeadZone = 0.15f;

        [Header("Pedals")]
        [Tooltip("Trigger pull below this reads as a closed pedal.")]
        [SerializeField] private float pedalDeadZone = 0.06f;

        [Header("Grab")]
        [Tooltip("Trigger or grip value at which a hand near the rim takes hold.")]
        [SerializeField] private float grabThreshold = 0.55f;

        [Tooltip("Once held, the hand keeps hold until the squeeze falls below " +
                 "this. A single threshold makes the grip drop out when the " +
                 "driver's finger relaxes slightly mid-corner.")]
        [SerializeField] private float releaseThreshold = 0.3f;

        [Header("Hands")]
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform rightHandAnchor;

        // ---- IInputProvider --------------------------------------------------

        public int Priority => 90;
        public bool SuppliesPedals => true;

        /// <summary>
        /// Available whenever at least one controller is present — the wheel need
        /// not be held, because the thumbstick fallback lives in this same
        /// provider and the triggers are the pedals either way.
        /// </summary>
        public bool IsAvailable => XRControls.AnyAvailable;

        public string ProviderName => IsHeld
            ? "VR steering wheel (held)"
            : "VR steering wheel (thumbstick)";

        public float ReadSteering() => Mathf.Clamp(_angleDegrees / degreesForFullLock, -1f, 1f);

        public float ReadAccelerator() => Pedal(Hand.Right);

        public float ReadBrake() => Pedal(Hand.Left);

        // ---- state -----------------------------------------------------------

        /// <summary>True while at least one hand has hold of the rim.</summary>
        public bool IsHeld => _holding[0] || _holding[1];

        /// <summary>Current wheel angle in degrees. Positive is clockwise.</summary>
        public float AngleDegrees => _angleDegrees;

        private readonly bool[] _holding = new bool[2];
        private readonly float[] _lastHandAngle = new float[2];
        private float _angleDegrees;
        private Quaternion _restRotation = Quaternion.identity;
        private bool _restCaptured;

        private void Awake() => ServiceRegistry.Register(this);

        private void Update()
        {
            UpdateHand(Hand.Left, 0, leftHandAnchor);
            UpdateHand(Hand.Right, 1, rightHandAnchor);

            if (!IsHeld) ApplyReleasedBehaviour();

            _angleDegrees = Mathf.Clamp(_angleDegrees, -degreesForFullLock, degreesForFullLock);

            if (wheelVisual != null)
            {
                // Capture the authored rest rotation ONCE. Reading
                // localEulerAngles back out of a quaternion this line wrote on
                // the previous frame is a lossy round-trip: Euler decomposition
                // is not unique, so x and y can flip to an equivalent-but-
                // different triple and the wheel develops a permanent tilt.
                if (!_restCaptured)
                {
                    _restRotation = wheelVisual.localRotation;
                    _restCaptured = true;
                }

                // POSITIVE, not negative. The column's basis is mirrored in X
                // (its LookRotation gives right = (-1,0,0)), and a mirrored basis
                // reverses the apparent handedness of a rotation about its Z
                // axis. With the sign reasoned from an unmirrored frame the car
                // steered correctly while the wheel in the driver's hands turned
                // the other way.
                wheelVisual.localRotation =
                    _restRotation * Quaternion.Euler(0f, 0f, _angleDegrees);
            }
        }

        // ---- grab and turn ---------------------------------------------------

        private void UpdateHand(Hand hand, int slot, Transform anchor)
        {
            if (anchor == null || !XRControls.Available(hand))
            {
                _holding[slot] = false;
                return;
            }

            // GRIP ONLY. The triggers are the pedals, and taking the grab on
            // max(trigger, grip) meant that holding the rim the way the
            // instructions describe asserted full throttle with the right hand
            // and full brake with the left at the same time. Brake authority is
            // 4 x 3000 Nm against 2 x 600 Nm of drive — ten to one — so the
            // wheels could not turn while the steering worked perfectly.
            //
            // One control, one job. Grip holds the wheel; triggers drive it.
            float squeeze = XRControls.Grip(hand);

            // Hysteresis: a higher bar to take hold than to keep it.
            bool wantsHold = _holding[slot]
                ? squeeze > releaseThreshold
                : squeeze > grabThreshold && NearRim(anchor.position);

            if (!wantsHold)
            {
                _holding[slot] = false;
                return;
            }

            float handAngle = AngleOnWheel(anchor.position);

            if (!_holding[slot])
            {
                // First frame of the grab: anchor the reference angle rather than
                // snapping the wheel to wherever the hand happens to be.
                _holding[slot] = true;
                _lastHandAngle[slot] = handAngle;
                return;
            }

            float delta = Mathf.DeltaAngle(_lastHandAngle[slot], handAngle);
            _lastHandAngle[slot] = handAngle;

            // Two hands on the wheel each contribute half, so holding with both
            // hands turns the wheel at the same rate as holding with one.
            float share = (_holding[0] && _holding[1]) ? 0.5f : 1f;
            _angleDegrees += delta * share;
        }

        private bool NearRim(Vector3 handWorldPosition)
        {
            if (wheelVisual == null) return false;

            Vector3 local = wheelVisual.InverseTransformPoint(handWorldPosition);

            // Distance from the rim circle, which lies in the wheel's local XY
            // plane at radius rimRadius.
            float radial = Mathf.Abs(new Vector2(local.x, local.y).magnitude - rimRadius);
            float axial = Mathf.Abs(local.z);

            return radial <= grabTolerance && axial <= grabTolerance * 1.5f;
        }

        /// <summary>
        /// The hand's angular position around the wheel axis, in the wheel's own
        /// local space, so the car's motion and the wheel's current rotation do
        /// not leak into the measurement.
        /// </summary>
        private float AngleOnWheel(Vector3 handWorldPosition)
        {
            if (wheelVisual == null) return 0f;

            // Measured against the wheel's PARENT, not the wheel itself. Against
            // the wheel, the rotation this script applies feeds straight back
            // into the measurement and the wheel runs away on its own.
            Transform frame = wheelVisual.parent != null ? wheelVisual.parent : wheelVisual;
            Vector3 local = frame.InverseTransformPoint(handWorldPosition)
                            - frame.InverseTransformPoint(wheelVisual.position);

            // NEGATED, and this is not arbitrary. The steering column is built
            // with Quaternion.LookRotation(forward = (0, 0.342, -0.940)) so its
            // face points back at the driver. Unity derives right as
            // cross(up, forward), which for that forward is (-1, 0, 0): the
            // column's local +X points at the car's LEFT. Measuring the hand
            // with a raw Atan2(local.x, local.y) in that frame therefore makes a
            // clockwise turn of the hand read as a negative angle, and the car
            // steered left when the driver turned right.
            //
            // Negating here makes positive mean clockwise-from-the-driver, which
            // is what ReadSteering() promises, what WheelCollider.steerAngle
            // expects for a right turn, and what the thumbstick path already
            // produced. The wheel mesh is rotated by -_angleDegrees about the
            // column's Z, which from the driver's side of the wheel reads as
            // clockwise, so the mesh now follows the hand instead of opposing it.
            return -Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;
        }

        private void ApplyReleasedBehaviour()
        {
            float stickX = XRControls.Thumbstick(Hand.Left).x;
            if (Mathf.Abs(stickX) < thumbstickDeadZone)
                stickX = XRControls.Thumbstick(Hand.Right).x;

            if (Mathf.Abs(stickX) >= thumbstickDeadZone)
            {
                float sign = Mathf.Sign(stickX);
                float magnitude = (Mathf.Abs(stickX) - thumbstickDeadZone)
                                  / (1f - thumbstickDeadZone);

                _angleDegrees += sign * magnitude * thumbstickRate
                                 * degreesForFullLock * Time.deltaTime;
                return;
            }

            _angleDegrees = Mathf.MoveTowards(_angleDegrees, 0f,
                                              selfCentringDegPerSec * Time.deltaTime);
        }

        /// <summary>
        /// Pedal position for one hand, from the trigger OR that hand's
        /// thumbstick pushed forward — whichever is further down.
        ///
        /// Two independent routes on purpose. A single binding that silently
        /// returns zero leaves the car immobile with nothing on screen to say
        /// why, and that is exactly the failure that cost a day of debugging.
        /// The thumbstick is also a genuine accessibility option: holding a
        /// trigger at a constant partial pull for three minutes is tiring, and
        /// on a long steady drive most people would rather push a stick.
        /// </summary>
        private float Pedal(Hand hand)
        {
            float trigger = XRControls.Trigger(hand);

            // Forward on the stick only, AND only while the stick is near-centred
            // in X. The same stick is the steering fallback, so a diagonal push
            // to turn — which is how anyone steers with a stick — was pressing
            // the brake at the same time.
            Vector2 stickAxis = XRControls.Thumbstick(hand);
            float stick = Mathf.Abs(stickAxis.x) <= 0.4f
                ? Mathf.Max(0f, stickAxis.y)
                : 0f;

            float t = Mathf.Max(trigger, stick);
            if (t < pedalDeadZone) return 0f;
            return Mathf.Clamp01((t - pedalDeadZone) / (1f - pedalDeadZone));
        }

        // ---- wiring, called by the scene builder -----------------------------

        public void Configure(Transform wheel, Transform leftHand, Transform rightHand)
        {
            wheelVisual = wheel;
            leftHandAnchor = leftHand;
            rightHandAnchor = rightHand;
        }
    }
}
