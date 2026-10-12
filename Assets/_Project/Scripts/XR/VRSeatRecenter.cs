using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Puts the driver's head in the driving position and keeps it there.
    ///
    /// This is the VR replacement for the old physical calibration step. There is
    /// no wheel prop to align to any more; the only misalignment left is between
    /// where the participant is actually sitting in the room and where the virtual
    /// driving seat is. One button fixes it, and the driver can press it again at
    /// any time without leaving the drive.
    ///
    /// THREE THINGS THIS GETS RIGHT, each of which was previously a bug:
    ///
    /// 1. SEAT-LOCAL MATHS. The car moves in FixedUpdate; this runs in Update. A
    ///    correction computed in world space is one physics step stale, which at
    ///    35 km/h is about 20 cm of error — enough that the driver ends up sitting
    ///    through the dashboard. Everything here is computed in the seat anchor's
    ///    local space instead, so the car's own motion cancels out exactly.
    ///
    /// 2. TRACKING ORIGIN. Floor mode reports head height above the real floor,
    ///    roughly 1.1 m for a seated adult, and then the rig adds it on top of the
    ///    seat height. Device mode reports head pose relative to where tracking
    ///    started, which is what a seated experience wants.
    ///
    /// 3. NO LOCOMOTION. Nothing in a driving simulator may move the rig except
    ///    the car. Any leftover locomotion component — a move provider, a turn
    ///    provider, and above all a gravity provider paired with a disabled
    ///    character controller — fights this script. A gravity provider with no
    ///    character controller to stop it is what previously dropped the camera
    ///    out of the world at terminal velocity. Awake strips the whole family by
    ///    name, so the rig cannot acquire one by accident later.
    /// </summary>
    public class VRSeatRecenter : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("The XR Origin's own transform — the object this script moves.")]
        [SerializeField] private Transform xrOrigin;

        [Tooltip("The main camera under the XR Origin.")]
        [SerializeField] private Camera headCamera;

        [Tooltip("Empty transform at the driver's eye position in the cabin, " +
                 "parented to the vehicle. The XR Origin is parented to this.")]
        [SerializeField] private Transform seatAnchor;

        [Header("Behaviour")]
        [Tooltip("Recentre automatically when the driver enters the cabin.")]
        [SerializeField] private bool recentreOnReady = true;

        [Tooltip("Also match the driver's yaw to straight ahead. Position only " +
                 "when off, which is less disorienting mid-drive.")]
        [SerializeField] private bool alignYaw = true;

        [Tooltip("How far the driver may lean from the seat before being pulled " +
                 "back. Leaning to look in a mirror has to stay possible, so this " +
                 "is generous; it exists so that standing up does not drag the " +
                 "camera out through the roof.")]
        [SerializeField] private float leashRadius = 0.6f;

        [Header("Diagnostics")]
        [Tooltip("Logs head height and seat offset every few seconds. Leave on " +
                 "until a session has been verified on the headset.")]
        [SerializeField] private bool logSeatTrace = true;
        [SerializeField] private float traceInterval = 3f;

        /// <summary>Locomotion components that must not exist on a driving rig.
        /// Matched by type name so this file needs no reference to the packages
        /// or samples that define them.</summary>
        private static readonly string[] LocomotionToStrip =
        {
            "GravityProvider", "XRBodyTransformer", "LocomotionMediator",
            "DynamicMoveProvider", "ContinuousMoveProvider", "ContinuousTurnProvider",
            "SnapTurnProvider", "TeleportationProvider", "JumpProvider",
            "ClimbProvider", "ClimbTeleportInteractor", "GrabMoveProvider",
            "TwoHandedGrabMoveProvider", "ClimbTeleportDestinationIndicator",
            "CharacterControllerDriver", "LocomotionSystem", "ActionBasedController"
        };

        private float _nextTrace;
        private bool _ready;

        // ---- lifecycle -------------------------------------------------------

        private void Awake()
        {
            if (xrOrigin == null) xrOrigin = transform;
            if (headCamera == null) headCamera = GetComponentInChildren<Camera>(true);

            StripLocomotion();
            StripCharacterController();

            ServiceRegistry.Register(this);
        }

        private IEnumerator Start()
        {
            // The input subsystem is not necessarily up on the first frame, so
            // the origin mode is set once it is, then re-asserted. Setting it
            // once at startup is how it silently reverts to Floor.
            yield return null;
            SetTrackingOriginToDevice();
            yield return new WaitForSeconds(0.5f);
            SetTrackingOriginToDevice();

            if (recentreOnReady) Recentre();
        }

        private void OnEnable() => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            _ready = phase == SessionPhase.Recenter || phase == SessionPhase.Ready
                     || phase == SessionPhase.Driving || phase == SessionPhase.HazardActive;

            // Entering the cabin: put them in the seat without being asked.
            if (recentreOnReady && phase == SessionPhase.Recenter) Recentre();
        }

        private void Update()
        {
            // No alert on a manual recentre: the view snapping to straight-ahead
            // is its own confirmation, and an alert banner on every button press
            // trains the driver to ignore the banner.
            if (XRControls.RecenterPressed()) Recentre();

            if (logSeatTrace && Time.time >= _nextTrace)
            {
                _nextTrace = Time.time + traceInterval;
                LogTrace();
            }
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            ApplyLeash();
        }

        // ---- the actual recentre ---------------------------------------------

        /// <summary>
        /// Moves the XR Origin so the driver's current head pose lands on the
        /// seat anchor. Safe to call at any time, including mid-drive.
        /// </summary>
        public void Recentre()
        {
            if (xrOrigin == null || headCamera == null || seatAnchor == null)
            {
                Debug.LogWarning("[Recentre] not wired: " +
                                 $"origin={(xrOrigin != null)} camera={(headCamera != null)} " +
                                 $"seat={(seatAnchor != null)}. Nothing moved.");
                return;
            }

            if (xrOrigin.parent != seatAnchor)
            {
                Debug.LogWarning("[Recentre] the XR Origin is not parented to the " +
                                 "seat anchor, so the driver will not move with the " +
                                 "car. Rebuild the scene with " +
                                 "Tools → Intelli-Driving → Build Main Scene.");
            }

            Transform head = headCamera.transform;

            // REFUSE TO RECENTRE ON A HEAD POSE THAT DOES NOT EXIST YET.
            //
            // On the recentre screen this can be called in the first frames,
            // before the runtime has delivered a pose. The camera is then sitting
            // at the rig origin, so headInSeat is the seat anchor's own offset
            // and the correction bakes in an origin shift wrong by the driver's
            // entire seated head height — about 1.2 m. The driver ends up
            // somewhere around the pedals and has to recentre again to escape it.
            if (!XRControls.HeadPoseReady)
            {
                Debug.LogWarning("[Recentre] no head pose from the runtime yet — "
                                 + "ignoring. Press it again in a moment.");
                return;
            }

            if (alignYaw)
            {
                // Yaw correction in seat-local space: how far off straight-ahead
                // the driver is looking, measured in the seat's own frame.
                Vector3 localForward = seatAnchor.InverseTransformDirection(head.forward);
                localForward.y = 0f;

                if (localForward.sqrMagnitude > 0.0001f)
                {
                    float yawError = Vector3.SignedAngle(
                        localForward.normalized, Vector3.forward, Vector3.up);

                    Vector3 e = xrOrigin.localEulerAngles;
                    xrOrigin.localRotation = Quaternion.Euler(0f, e.y + yawError, 0f);
                }
            }

            // Position correction, also seat-local. After this the head sits at
            // the seat anchor's own origin.
            Vector3 headInSeat = seatAnchor.InverseTransformPoint(head.position);
            xrOrigin.localPosition -= headInSeat;

            SessionEvents.RaiseSeatRecentred();

            Debug.Log($"[Recentre] head was {headInSeat.magnitude:F2} m from the seat " +
                      $"(local {headInSeat}); origin now at {xrOrigin.localPosition}");
        }

        /// <summary>Wired to the Recenter button on the recentre panel.</summary>
        public void OnRecenterButton() => Recentre();

        // ---- leash -----------------------------------------------------------

        /// <summary>
        /// Pulls the driver back if their head leaves a sphere around the seat.
        /// Without it, a participant who stands up takes the camera with them and
        /// ends up looking down at the car roof from outside, which reads on the
        /// recording as the scene flying away.
        /// </summary>
        private void ApplyLeash()
        {
            if (leashRadius <= 0f || xrOrigin == null || headCamera == null
                || seatAnchor == null) return;

            Vector3 headInSeat = seatAnchor.InverseTransformPoint(headCamera.transform.position);
            float distance = headInSeat.magnitude;
            if (distance <= leashRadius) return;

            Vector3 excess = headInSeat * (1f - leashRadius / distance);
            xrOrigin.localPosition -= excess;
        }

        // ---- rig hygiene -----------------------------------------------------

        private void StripLocomotion()
        {
            var components = GetComponentsInChildren<MonoBehaviour>(true);
            int removed = 0;

            foreach (MonoBehaviour c in components)
            {
                if (c == null || c == this) continue;

                string typeName = c.GetType().Name;
                for (int i = 0; i < LocomotionToStrip.Length; i++)
                {
                    if (typeName != LocomotionToStrip[i]) continue;
                    Debug.Log($"[Recentre] removing {typeName} from {c.gameObject.name} " +
                              "— nothing but the car may move this rig.");
                    Destroy(c);
                    removed++;
                    break;
                }
            }

            if (removed > 0)
                Debug.Log($"[Recentre] stripped {removed} locomotion component(s) " +
                          "from the XR rig.");
        }

        /// <summary>
        /// A CharacterController on the rig is either enabled — in which case it
        /// collides with the car's own interior and shoves the driver around — or
        /// disabled, in which case a GravityProvider has nothing to stop it and
        /// the rig falls forever. Neither is wanted, so the component goes.
        /// </summary>
        private void StripCharacterController()
        {
            var controllers = GetComponentsInChildren<CharacterController>(true);
            foreach (CharacterController cc in controllers)
            {
                Debug.Log($"[Recentre] removing CharacterController from {cc.gameObject.name}.");
                Destroy(cc);
            }
        }

        private static void SetTrackingOriginToDevice()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);

            if (subsystems.Count == 0)
            {
                Debug.Log("[Recentre] no XRInputSubsystem yet — tracking origin " +
                          "will stay at the runtime default. Harmless in the editor " +
                          "without a headset.");
                return;
            }

            foreach (XRInputSubsystem s in subsystems)
            {
                bool ok = s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
                Debug.Log($"[Recentre] tracking origin → Device: {(ok ? "ok" : "refused")} " +
                          $"(now {s.GetTrackingOriginMode()})");
                if (ok) s.TryRecenter();
            }
        }

        private void LogTrace()
        {
            if (headCamera == null || seatAnchor == null) return;

            Vector3 headInSeat = seatAnchor.InverseTransformPoint(headCamera.transform.position);
            Debug.Log($"[SeatTrace] seatY={seatAnchor.position.y:F3} " +
                      $"camY={headCamera.transform.position.y:F3} " +
                      $"offsetInSeat={headInSeat} " +
                      $"parentedCorrectly={(xrOrigin != null && xrOrigin.parent == seatAnchor)} " +
                      $"controllers L={XRControls.Available(Hand.Left)} " +
                      $"R={XRControls.Available(Hand.Right)}");
        }

        // ---- wiring, called by the scene builder -----------------------------

        public void Configure(Transform origin, Camera cam, Transform seat)
        {
            xrOrigin = origin;
            headCamera = cam;
            seatAnchor = seat;
        }

        public Transform SeatAnchor => seatAnchor;
    }
}
