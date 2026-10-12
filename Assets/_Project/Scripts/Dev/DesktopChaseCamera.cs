using UnityEngine;
using UnityEngine.InputSystem;

namespace IDS.Dev
{
    /// <summary>
    /// Desktop-only chase camera, so the project is playable and demonstrable
    /// without a headset. It is NOT in the generated scene — the XR rig's camera
    /// is the only camera there. Add this by hand to a spare GameObject when you
    /// want to drive the simulator at a desk.
    ///
    /// It can also stand in as the "head transform" for the telemetry recorder
    /// and the mirror detector, so head yaw is recorded rather than crashing on a
    /// null reference. Mirror-check numbers from a desktop session are NOT
    /// meaningful and must never be reported as if they were — a mouse-look yaw
    /// is not a driver turning their head.
    ///
    /// Reads the Input System directly, so the project does not need the
    /// deprecated Input Manager backend.
    /// </summary>
    public class DesktopChaseCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 1.25f, -0.15f);
        [SerializeField] private bool recentreWhenIdle = true;

        [Header("Free look — hold right mouse button")]
        [Tooltip("Degrees per pixel of mouse movement. Mouse.delta is in pixels, " +
                 "where the legacy Mouse X axis was pre-scaled, so this is much " +
                 "smaller than the old sensitivity value.")]
        [SerializeField] private float lookSensitivity = 0.12f;

        [SerializeField] private float maxYaw = 110f;
        [SerializeField] private float maxPitch = 45f;
        [SerializeField] private float recentreSpeed = 90f;

        private float _yaw, _pitch;

        public void SetTarget(Transform t) => target = t;

        private void LateUpdate()
        {
            if (target == null) return;

            transform.position = target.TransformPoint(localOffset);

            Mouse mouse = Mouse.current;
            bool looking = mouse != null && mouse.rightButton.isPressed;

            if (looking)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw = Mathf.Clamp(_yaw + delta.x * lookSensitivity, -maxYaw, maxYaw);
                _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -maxPitch, maxPitch);
            }
            else if (recentreWhenIdle)
            {
                _yaw = Mathf.MoveTowards(_yaw, 0f, recentreSpeed * Time.deltaTime);
                _pitch = Mathf.MoveTowards(_pitch, 0f, recentreSpeed * Time.deltaTime);
            }

            transform.rotation = target.rotation * Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}
