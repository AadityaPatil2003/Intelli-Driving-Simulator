using UnityEngine;

namespace IDS.Dev
{
    /// <summary>
    /// Desktop-only chase camera. Exists so the project is playable and
    /// demonstrable without a headset. In the XR build this object is disabled
    /// and the XR Origin camera takes over.
    ///
    /// It also stands in as the "head transform" for the telemetry recorder and
    /// the mirror detector, so head yaw is recorded rather than crashing on a
    /// null reference. Mirror-check numbers from a desktop session are NOT
    /// meaningful and must not be reported as if they were.
    /// </summary>
    public class DesktopChaseCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 1.25f, -0.15f);
        [SerializeField] private bool recentreWhenIdle = true;

        [Header("Free look — hold right mouse button")]
        [SerializeField] private float lookSensitivity = 2.5f;
        [SerializeField] private float maxYaw = 110f;
        [SerializeField] private float maxPitch = 45f;
        [SerializeField] private float recentreSpeed = 90f;

        private float _yaw, _pitch;

        public void SetTarget(Transform t) => target = t;

        private void LateUpdate()
        {
            if (target == null) return;

            transform.position = target.TransformPoint(localOffset);

            if (Input.GetMouseButton(1))
            {
                _yaw = Mathf.Clamp(_yaw + Input.GetAxis("Mouse X") * lookSensitivity,
                                   -maxYaw, maxYaw);
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * lookSensitivity,
                                     -maxPitch, maxPitch);
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
