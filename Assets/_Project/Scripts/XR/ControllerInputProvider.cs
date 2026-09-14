using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// FALLBACK A from the master plan: a Quest controller taped to the wheel prop
    /// supplies the steering angle, and the triggers supply the pedals. The prop
    /// still gives real haptic resistance, so the mixed-reality claim survives
    /// even if hand tracking never becomes reliable enough.
    ///
    /// Steering is read from the controller's roll about its own forward axis,
    /// zeroed on Calibrate().
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public class ControllerInputProvider : MonoBehaviour, IInputProvider
    {
        [Tooltip("Which hand holds / is taped to the wheel.")]
        [SerializeField] private bool useRightController = true;

        [SerializeField] private float maxRollDegrees = 120f;
        [SerializeField] private float deadZoneDegrees = 4f;

        public int Priority => 50;
        public string ProviderName => useRightController
            ? "Right controller (wheel-mounted)"
            : "Left controller (wheel-mounted)";

        public bool IsAvailable => _device.isValid;

        private InputDevice _device;
        private float _neutralRoll;
        private bool  _zeroed;
        private static readonly List<InputDevice> Buffer = new();

        private void OnEnable()  => SessionEvents.CalibrationCompleted += ZeroSteering;
        private void OnDisable() => SessionEvents.CalibrationCompleted -= ZeroSteering;

        private void Update()
        {
            if (!_device.isValid) AcquireDevice();
        }

        private void AcquireDevice()
        {
            var chars = InputDeviceCharacteristics.Controller |
                        (useRightController
                            ? InputDeviceCharacteristics.Right
                            : InputDeviceCharacteristics.Left);

            InputDevices.GetDevicesWithCharacteristics(chars, Buffer);
            if (Buffer.Count > 0) _device = Buffer[0];
        }

        public void ZeroSteering()
        {
            if (TryGetRoll(out float roll))
            {
                _neutralRoll = roll;
                _zeroed = true;
                Debug.Log($"[ControllerInput] steering zeroed at {roll:F1}°");
            }
        }

        private bool TryGetRoll(out float roll)
        {
            roll = 0f;
            if (!_device.isValid) return false;
            if (!_device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
                return false;

            // Roll about the controller's own forward axis.
            Vector3 up = rot * Vector3.up;
            Vector3 fwd = rot * Vector3.forward;
            Vector3 flatUp = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
            roll = Vector3.SignedAngle(flatUp, up, fwd);
            return true;
        }

        public float ReadSteering()
        {
            if (!TryGetRoll(out float roll)) return 0f;
            if (!_zeroed) { _neutralRoll = roll; _zeroed = true; }

            float delta = Mathf.DeltaAngle(_neutralRoll, roll);
            float mag = Mathf.Abs(delta);
            if (mag <= deadZoneDegrees) return 0f;

            return Mathf.Clamp(
                Mathf.Sign(delta) * (mag - deadZoneDegrees)
                / Mathf.Max(1f, maxRollDegrees - deadZoneDegrees), -1f, 1f);
        }

        public float ReadAccelerator()
        {
            _device.TryGetFeatureValue(CommonUsages.trigger, out float t);
            return Mathf.Clamp01(t);
        }

        public float ReadBrake()
        {
            _device.TryGetFeatureValue(CommonUsages.grip, out float g);
            return Mathf.Clamp01(g);
        }
    }
}
