using UnityEngine;
using IDS.Core;
using IDS.XR;

namespace IDS.Vehicle
{
    /// <summary>
    /// WheelCollider vehicle. Deliberately simple: no gearbox, no engine curve,
    /// no tyre wear, no fuel, no damage. The goal is a car that behaves the same
    /// way every session, because every telemetry measurement in this project is
    /// comparing sessions against each other.
    ///
    /// SCENE SETUP (Vehicle_Test.unity):
    ///   Vehicle (Rigidbody, mass 1200, this script)
    ///     ├── Body            (mesh / cube, no collider needed if using a box on root)
    ///     ├── Colliders
    ///     │     ├── WheelFL (WheelCollider)   radius 0.34, suspension 0.15
    ///     │     ├── WheelFR (WheelCollider)
    ///     │     ├── WheelRL (WheelCollider)
    ///     │     └── WheelRR (WheelCollider)
    ///     └── WheelMeshes/... (visual only, driven by this script)
    ///
    /// IMPORTANT: set the Rigidbody's centre of mass low (see comOffset below) or
    /// the car will roll over on the first corner. This is the single most common
    /// WheelCollider mistake.
    ///
    /// OWNER: Ananya.
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

        [Header("Tuning - prototype values, not a real vehicle")]
        [SerializeField] private float maxMotorTorque = 600f;
        [SerializeField] private float maxBrakeTorque = 3000f;
        [SerializeField] private float maxSteerAngle  = 28f;
        [Tooltip("Hard cap. Comfort-driven, not performance-driven.")]
        [SerializeField] private float maxSpeedKph = 50f;

        [Tooltip("Steering angle is scaled down as speed rises so the car is not " +
                 "twitchy at speed. 1 = full angle available at any speed.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float highSpeedSteerFactor = 0.55f;

        [Tooltip("Engine braking applied when neither pedal is pressed, so the " +
                 "car slows predictably instead of coasting forever.")]
        [SerializeField] private float coastBrakeTorque = 250f;

        [Header("Physics")]
        [Tooltip("Centre of mass offset from the transform origin. Must be low.")]
        [SerializeField] private Vector3 comOffset = new Vector3(0f, -0.4f, 0f);

        [Header("Input")]
        [Tooltip("Leave empty to auto-resolve Aaditya's adapter from the registry. " +
                 "In Vehicle_Test you can wire a KeyboardInputProvider directly.")]
        [SerializeField] private VehicleInputAdapter inputAdapter;

        // ---- IVehicleState ---------------------------------------------------
        public float SteeringInput    { get; private set; }
        public float AcceleratorInput { get; private set; }
        public float BrakeInput       { get; private set; }
        public float CurrentSpeedKph  { get; private set; }
        public Vector3 Position => transform.position;
        public Vector3 Forward  => transform.forward;
        public bool IsColliding { get; private set; }
        public int  CollisionCount { get; private set; }

        public void ResetSessionCounters()
        {
            CollisionCount = 0;
            IsColliding = false;
        }

        private Rigidbody _rb;
        private int _collidingFrames;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.centerOfMass += comOffset;

            // Interpolation matters in XR: without it the car visibly stutters
            // at the render rate even at a stable 72 FPS.
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            ServiceRegistry.Register<IVehicleState>(this);
            ServiceRegistry.Register(this);
        }

        private void Start()
        {
            inputAdapter ??= ServiceRegistry.Resolve<VehicleInputAdapter>();
            if (inputAdapter == null)
                Debug.LogWarning("[Vehicle] no VehicleInputAdapter (owner: Aaditya). " +
                                 "Car will not respond to input.");
        }

        private void FixedUpdate()
        {
            ReadInput();
            ApplySteering();
            ApplyDrive();
            UpdateSpeed();
            UpdateWheelMeshes();
            DecayCollisionFlag();
        }

        private void ReadInput()
        {
            if (inputAdapter == null) return;
            SteeringInput    = Mathf.Clamp(inputAdapter.Steering, -1f, 1f);
            AcceleratorInput = Mathf.Clamp01(inputAdapter.Accelerator);
            BrakeInput       = Mathf.Clamp01(inputAdapter.Brake);
        }

        private void ApplySteering()
        {
            float speedRatio = Mathf.Clamp01(Mathf.Abs(CurrentSpeedKph) / maxSpeedKph);
            float effectiveAngle = maxSteerAngle *
                                   Mathf.Lerp(1f, highSpeedSteerFactor, speedRatio);

            float angle = SteeringInput * effectiveAngle;
            if (frontLeft  != null) frontLeft.steerAngle  = angle;
            if (frontRight != null) frontRight.steerAngle = angle;
        }

        private void ApplyDrive()
        {
            bool atSpeedLimit = CurrentSpeedKph >= maxSpeedKph;
            float motor = atSpeedLimit ? 0f : AcceleratorInput * maxMotorTorque;

            float brakeTorque = BrakeInput * maxBrakeTorque;
            if (AcceleratorInput < 0.01f && BrakeInput < 0.01f)
                brakeTorque = coastBrakeTorque;

            // Rear-wheel drive, four-wheel braking.
            SetWheel(rearLeft,  motor, brakeTorque);
            SetWheel(rearRight, motor, brakeTorque);
            SetWheel(frontLeft,  0f, brakeTorque);
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
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            CurrentSpeedKph = forwardSpeed * 3.6f;
        }

        private void UpdateWheelMeshes()
        {
            SyncMesh(frontLeft, meshFrontLeft);
            SyncMesh(frontRight, meshFrontRight);
            SyncMesh(rearLeft, meshRearLeft);
            SyncMesh(rearRight, meshRearRight);
        }

        private static void SyncMesh(WheelCollider w, Transform mesh)
        {
            if (w == null || mesh == null) return;
            w.GetWorldPose(out Vector3 pos, out Quaternion rot);
            mesh.SetPositionAndRotation(pos, rot);
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Ignore the road surface itself.
            if (collision.gameObject.CompareTag("RoadSurface")) return;

            float impactKph = collision.relativeVelocity.magnitude * 3.6f;
            if (impactKph < 3f) return; // kerb scrapes are not collisions

            IsColliding = true;
            _collidingFrames = 3;
            CollisionCount++;

            SessionEvents.RaiseCollision(impactKph);
            Debug.Log($"[Vehicle] collision with {collision.gameObject.name} " +
                      $"at {impactKph:F1} km/h (total {CollisionCount})");
        }

        private void DecayCollisionFlag()
        {
            if (_collidingFrames > 0) _collidingFrames--;
            else IsColliding = false;
        }

        /// <summary>Teleport for scenario resets. Zeroes velocity so the reset is clean.</summary>
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position, rotation);
            CurrentSpeedKph = 0f;
        }
    }
}
