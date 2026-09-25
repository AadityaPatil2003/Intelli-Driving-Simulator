using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using TMPro;

namespace IDS.Dev
{
    /// <summary>
    /// Shows XR runtime state on a panel in front of the driver, in the headset.
    ///
    /// Why this exists: when passthrough does not work, the symptom is a black
    /// screen, and a black screen is indistinguishable from "the app crashed",
    /// "no XR loader started", "the camera clear colour is opaque" and "the
    /// ARSession never came up". Each has a different fix. Reading adb logcat
    /// while wearing a headset is not practical, so the diagnosis has to be
    /// visible from inside.
    ///
    /// Put this in the XR gate scene. Remove it from the final build.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public class XRDiagnosticHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text output;
        [SerializeField] private float refreshInterval = 0.5f;

        [Tooltip("Optional. Assign the PassthroughController so its state is reported.")]
        [SerializeField] private MonoBehaviour passthroughControllerRef;

        private float _nextRefresh;
        private float _fpsAccumulator;
        private int _fpsFrames;
        private float _fps;

        private static readonly List<InputDevice> DeviceBuffer = new();
        private readonly StringBuilder _sb = new();

        private void Update()
        {
            _fpsAccumulator += Time.unscaledDeltaTime;
            _fpsFrames++;

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;

            if (_fpsAccumulator > 0f) _fps = _fpsFrames / _fpsAccumulator;
            _fpsAccumulator = 0f;
            _fpsFrames = 0;

            if (output != null) output.text = BuildReport();
        }

        private string BuildReport()
        {
            _sb.Clear();
            _sb.AppendLine("<b>XR DIAGNOSTIC</b>");
            _sb.AppendLine();

            // ---- loader -------------------------------------------------------
            var manager = XRGeneralSettings.Instance != null
                ? XRGeneralSettings.Instance.Manager : null;

            if (manager == null)
            {
                _sb.AppendLine(Bad("XR Manager") + "  null");
                _sb.AppendLine("   XR Plug-in Management is not configured");
                _sb.AppendLine("   for this build target.");
            }
            else if (manager.activeLoader == null)
            {
                _sb.AppendLine(Bad("Active loader") + "  none");
                _sb.AppendLine("   No XR loader started. Check that OpenXR is");
                _sb.AppendLine("   ticked for Android in XR Plug-in Management.");
            }
            else
            {
                _sb.AppendLine(Good("Active loader") + "  " + manager.activeLoader.name);
            }

            // ---- head tracking ------------------------------------------------
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HeadMounted, DeviceBuffer);

            if (DeviceBuffer.Count > 0)
            {
                var hmd = DeviceBuffer[0];
                bool hasPose = hmd.TryGetFeatureValue(
                    CommonUsages.centerEyePosition, out Vector3 pos);

                _sb.AppendLine(hasPose
                    ? Good("Head tracking") + $"  {pos.x:F2}, {pos.y:F2}, {pos.z:F2}"
                    : Warn("Head tracking") + "  device present, no pose");
                _sb.AppendLine("   " + hmd.name);
            }
            else
            {
                _sb.AppendLine(Bad("Head tracking") + "  no HMD device");
            }

            // ---- hands --------------------------------------------------------
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HandTracking, DeviceBuffer);
            _sb.AppendLine(DeviceBuffer.Count > 0
                ? Good("Hand devices") + $"  {DeviceBuffer.Count}"
                : Warn("Hand devices") + "  none (enable hand tracking in headset settings)");

            // ---- camera -------------------------------------------------------
            var cam = Camera.main;
            if (cam == null)
            {
                _sb.AppendLine(Bad("Main camera") + "  none tagged MainCamera");
            }
            else
            {
                Color bg = cam.backgroundColor;
                bool correct = cam.clearFlags == CameraClearFlags.SolidColor && bg.a < 0.01f;

                _sb.AppendLine((correct ? Good("Camera clear") : Bad("Camera clear")) +
                               $"  {cam.clearFlags}, a={bg.a:F2}");

                if (!correct)
                {
                    _sb.AppendLine("   Must be Solid Color with alpha 0, or the");
                    _sb.AppendLine("   camera paints over the passthrough feed.");
                }
            }

            // ---- passthrough --------------------------------------------------
            if (passthroughControllerRef != null)
            {
                var prop = passthroughControllerRef.GetType().GetProperty("PassthroughActive");
                if (prop != null && prop.GetValue(passthroughControllerRef) is bool active)
                    _sb.AppendLine((active ? Good("Passthrough") : Bad("Passthrough")) +
                                   (active ? "  active" : "  inactive"));
            }

            // ---- performance --------------------------------------------------
            _sb.AppendLine();
            string fpsTag = _fps >= 70f ? Good("FPS") : _fps >= 60f ? Warn("FPS") : Bad("FPS");
            _sb.AppendLine($"{fpsTag}  {_fps:F0}   (target {Application.targetFrameRate})");

            return _sb.ToString();
        }

        private static string Good(string label) => $"<color=#4CB782>[OK]</color> {label}";
        private static string Warn(string label) => $"<color=#F0A202>[??]</color> {label}";
        private static string Bad(string label) => $"<color=#E05C5C>[!!]</color> {label}";
    }
}
