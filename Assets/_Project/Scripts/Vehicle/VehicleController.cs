using UnityEngine;
using IDS.Core;
using IDS.XR;

namespace IDS.Vehicle
{
    /// <summary>
    /// WheelCollider vehicle. Deliberately simple: no gearbox, no engine curve,
    /// no tyre wear, no fuel, no damage. The goal is a car that behaves identically
    /// every session, because every measurement in this project compares sessions
    /// against each other.
    ///
    /// EXPECTED HIERARCHY
    ///     Vehicle              Rigidbody (mass 1200) + this script
    ///       Colliders
    ///         WheelFL          WheelCollider, radius 0.34, suspension 0.15
    ///         WheelFR, RL, RR
    ///       WheelMeshes
    ///         MeshFL ...       visual only, driven by this script
    ///       Cabin              interior, mirrors, seat anchor
    ///
    /// Two things here are load-bearing and were each a visible bug:
    ///
    /// 1. CENTRE OF MASS must be low, or the car rolls over on the first corner.
    ///    This is the most common WheelCollider mistake.
    ///
    /// 2. WHEEL MESHES ARE POSITIONED IN LateUpdate, NOT FixedUpdate. The
    ///    rigidbody interpolates between physics steps, which slides the whole
    ///    hierarchy — children included — every render frame. Writing child world
    ///    positions in FixedUpdate means interpolation then shifts them again on
    ///    top, so the wheels drift forward out of their arches, about 0.2 m per
    ///    frame at 35 km/h. From the driver's seat that looks like wheels flying
    ///    past overhead. LateUpdate runs after interpolation, so the pose written
    ///    is the pose displayed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour, IVehicleState
    {
        [Header("Axles")]
        [SerializeField] private WheelCollider frontLeft;
        [SerializeField] private WheelCollider frontRight;
        [SerializeField] private WheelCollider rearLeft;
        [SerializeField] private WheelCollider rearRight;

        [Header("Wheel meshes (optional, visual only)")]
        [SerializeField] private Transform meshFrontLeft;
        [SerializeField] private Transform meshFrontRight;
        [SerializeField] private Transform meshRearLeft;
        [SerializeField] private Transform meshRearRight;

        [Header("Tuning — prototype values, not a real vehicle")]
        [SerializeField] private float maxMotorTorque = 600f;
        [SerializeField] private float maxBrakeTorque = 3000f;
        [SerializeField] private float maxSteerAngle = 28f;

        [Tooltip("Hard cap. Comfort-driven, not performance-driven.")]
        [SerializeField] private float maxSpeedKph = 50f;

        [Tooltip("Steering angle is scaled down as speed rises so the car is not " +
                 "twitchy. 1 = full angle available at any speed.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float highSpeedSteerFactor = 0.55f;

        [Tooltip("Engine braking when neither pedal is pressed, so the car slows " +
                 "predictably instead of coasting forever.")]
        [SerializeField] private float coastBrakeTorque = 250f;

        [Tooltip("Brake torque applied when the session is not in a driving " +
                 "phase. Holds the car still on the menus without needing a " +
                 "kinematic rigidbody, which would break the wheel colliders.")]
        [SerializeField] private float parkedBrakeTorque = 6000f;

        [Header("Physics")]
        [Tooltip("Centre of mass offset from the transform origin. Must be low.")]
        [SerializeField] private Vector3 comOffset = new Vector3(0f, -0.4f, 0f);

        [Header("Input")]
        [Tooltip("Leave empty to auto-resolve from the registry.")]
        [SerializeField] private VehicleInputAdapter inputAdapter;

        // ---- IVehicleState ---------------------------------------------------

        public float SteeringInput { get; private set; }
        public float AcceleratorInput { get; private set; }
        public float BrakeInput { get; private set; }
        public float CurrentSpeedKph { get; private set; }
        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;
        public bool IsColliding { get; private set; }
        public int CollisionCount { get; private set; }

        public void ResetSessionCounters()
        {
            CollisionCount = 0;
            IsColliding = false;
            _collidingSteps = 0;
        }

        public float MaxSpeedKph => maxSpeedKph;

        private Rigidbody _rb;
        private int _collidingSteps;
        private bool _driving;

        // ---- lifecycle -------------------------------------------------------

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.centerOfMass += comOffset;

            // Interpolation matters in VR: without it the car visibly stutters at
            // the render rate even at a stable 72 FPS. It is also precisely why
            // the wheel meshes are written in LateUpdate — see the class comment.
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            ServiceRegistry.Register<IVehicleState>(this);
            ServiceRegistry.Register(this);
        }

        private void Start()
        {
            inputAdapter ??= ServiceRegistry.Resolve<VehicleInputAdapter>();
            if (inputAdapter == null)
                Debug.LogWarning("[Vehicle] no VehicleInputAdapter — the car will " +
                                 "not respond to input.");
        }

        private void OnEnable()
        {
            SessionEvents.PhaseChanged += OnPhaseChanged;

            // Catch up on the phase we may have missed. PhaseChanged is an event,
            // not a state: a component enabled after the drive started would sit
            // with _driving false — parking brake on — until the NEXT phase
            // change, which during a drive is the end of it.
            SessionManager session = ServiceRegistry.Resolve<SessionManager>();
            if (session != null) OnPhaseChanged(session.Phase);
        }
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            _driving = phase == SessionPhase.Driving || phase == SessionPhase.HazardActive;
        }

        private void FixedUpdate()
        {
            ReadInput();
            UpdateSpeed();
            ApplySteering();
            ApplyDrive();
            DecayCollisionFlag();
        }

        /// <summary>
        /// Visual wheel poses, after interpolation. See the class comment — this
        /// being in FixedUpdate was a real, visible bug.
        /// </summary>
        private void LateUpdate()
        {
            SyncMesh(frontLeft, meshFrontLeft);
            SyncMesh(frontRight, meshFrontRight);
            SyncMesh(rearLeft, meshRearLeft);
            SyncMesh(rearRight, meshRearRight);
        }

        // ---- control ---------------------------------------------------------

        private void ReadInput()
        {
            if (inputAdapter == null || !_driving)
            {
                SteeringInput = 0f;
                AcceleratorInput = 0f;
                BrakeInput = 0f;
                return;
            }

            SteeringInput = Mathf.Clamp(inputAdapter.Steering, -1f, 1f);
            AcceleratorInput = Mathf.Clamp01(inputAdapter.Accelerator);
            BrakeInput = Mathf.Clamp01(inputAdapter.Brake);
        }

        private void ApplySteering()
        {
            float speedRatio = Mathf.Clamp01(Mathf.Abs(CurrentSpeedKph) / maxSpeedKph);
            float effectiveAngle = maxSteerAngle *
                                   Mathf.Lerp(1f, highSpeedSteerFactor, speedRatio);

            float angle = SteeringInput * effectiveAngle;
            if (frontLeft != null) frontLeft.steerAngle = angle;
            if (frontRight != null) frontRight.steerAngle = angle;
        }

        private void ApplyDrive()
        {
            if (!_driving)
            {
                // Held on the brakes outside the drive. The car must not creep
                // across the road while the driver reads the report.
                SetWheel(frontLeft, 0f, parkedBrakeTorque);
                SetWheel(frontRight, 0f, parkedBrakeTorque);
                SetWheel(rearLeft, 0f, parkedBrakeTorque);
                SetWheel(rearRight, 0f, parkedBrakeTorque);
                return;
            }

            bool atSpeedLimit = CurrentSpeedKph >= maxSpeedKph;

            // Brake wins. A real car cannot be driven and braked at once, and
            // without this a simultaneous throttle-and-brake input leaves the
            // motor fighting a brake with ten times its authority — the car sits
            // still, at full throttle, with nothing on screen to explain it.
            bool braking = BrakeInput >= 0.05f;

            float motor = (atSpeedLimit || braking)
                ? 0f
                : AcceleratorInput * maxMotorTorque;

            float brakeTorque = BrakeInput * maxBrakeTorque;
            if (AcceleratorInput < 0.01f && BrakeInput < 0.01f)
                brakeTorque = coastBrakeTorque;

            // Rear-wheel drive, four-wheel braking.
            SetWheel(rearLeft, motor, brakeTorque);
            SetWheel(rearRight, motor, brakeTorque);
            SetWheel(frontLeft, 0f, brakeTorque);
            SetWheel(frontRight, 0f, brakeTorque);
        }

        private static void SetWheel(WheelCollider w, float motor, float brake)
        {
            if (w == null) return;
            w.motorTorque = motor;
            w.brakeTorque = brake;
        }

        private void UpdateSpeed()
        {
            // Signed: forward positive, reverse negative.
            CurrentSpeedKph = Vector3.Dot(_rb.linearVelocity, transform.forward) * 3.6f;
        }

        private static void SyncMesh(WheelCollider w, Transform mesh)
        {
            if (w == null || mesh == null) return;
            w.GetWorldPose(out Vector3 pos, out Quaternion rot);
            mesh.SetPositionAndRotation(pos, rot);
        }

        // ---- collisions ------------------------------------------------------

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.CompareTag("RoadSurface")) return;

            float impactKph = collision.relativeVelocity.magnitude * 3.6f;
            if (impactKph < 3f) return;   // kerb scrapes are not collisions

            IsColliding = true;
            _collidingSteps = 3;
            CollisionCount++;

            SessionEvents.RaiseCollision(impactKph);
            Debug.Log($"[Vehicle] collision with {collision.gameObject.name} at " +
                      $"{impactKph:F1} km/h (total {CollisionCount})");
        }

        private void DecayCollisionFlag()
        {
            if (_collidingSteps > 0) _collidingSteps--;
            else IsColliding = false;
        }

        /// <summary>Teleport for resets. Zeroes velocity so the reset is clean.</summary>
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position, rotation);
            CurrentSpeedKph = 0f;

            SetWheel(frontLeft, 0f, 0f);
            SetWheel(frontRight, 0f, 0f);
            SetWheel(rearLeft, 0f, 0f);
            SetWheel(rearRight, 0f, 0f);
        }

        public void Configure(WheelCollider fl, WheelCollider fr,
                              WheelCollider rl, WheelCollider rr,
                              Transform mfl, Transform mfr, Transform mrl, Transform mrr)
        {
            frontLeft = fl; frontRight = fr; rearLeft = rl; rearRight = rr;
            meshFrontLeft = mfl; meshFrontRight = mfr;
            meshRearLeft = mrl; meshRearRight = mrr;
        }
    }
}
