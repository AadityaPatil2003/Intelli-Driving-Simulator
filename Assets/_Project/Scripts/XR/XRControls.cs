using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace IDS.XR
{
    public enum Hand { Left, Right }

    /// <summary>
    /// One place that knows how to read a Quest controller. Everything that needs
    /// controller input — the steering wheel, the menu pointer, the recentre
    /// button, the fallback pedals — goes through here.
    ///
    /// WHY THIS EXISTS, rather than four scripts each reading input their own way:
    /// on-device controller input is the part of this project that failed most
    /// often, and it failed differently in each script. One resolution path means
    /// one thing to debug and one log line that tells you what the headset
    /// actually reported.
    ///
    /// HOW IT RESOLVES DEVICES: it scans InputSystem.devices for a device
    /// carrying the LeftHand or RightHand usage, then looks up each control by
    /// NAME on that device. It deliberately does not use control-path strings
    /// ("&lt;XRController&gt;{RightHand}/trigger") or a specific device layout
    /// such as OculusTouchController, because both bind to a layout that may not
    /// be the one OpenXR registers for a given runtime version, and when they
    /// miss they return null silently — which reads, downstream, as a driver who
    /// simply never touched the controls.
    ///
    /// Everything returns a neutral value when a control or device is absent, so
    /// a caller can read unconditionally. Check <see cref="Available"/> before
    /// treating a zero as the driver's intent.
    /// </summary>
    public static class XRControls
    {
        private static InputDevice _left, _right;
        private static float _lastScan = -99f;
        private const float RescanInterval = 1f;

        private static bool _loggedOnce;
        private static string _lastLoggedState = "";

        // ---- device resolution -----------------------------------------------

        /// <summary>True when that hand's controller is present and tracking.</summary>
        public static bool Available(Hand hand)
        {
            InputDevice d = Device(hand);
            if (d == null) return false;
            if (d.TryGetChildControl<ButtonControl>("isTracked") is ButtonControl tracked)
                return tracked.isPressed;
            return true;   // no isTracked control: presence is the best signal there is
        }

        public static bool AnyAvailable => Available(Hand.Left) || Available(Hand.Right);

        /// <summary>
        /// True once the runtime has delivered a tracked head pose. Anything that
        /// rebases the rig on where the head is must wait for this: before it,
        /// the camera reports the rig origin and a "correction" computed from it
        /// is wrong by the driver's whole seated head position.
        /// </summary>
        public static bool HeadPoseReady
        {
            get
            {
                // Matched by the control it exposes, not by a concrete layout
                // type. XRHMD has no static 'current', and which layout an
                // OpenXR runtime registers for the headset varies — but
                // anything that is an HMD has a centre eye. This is the same
                // rule XRHeadAnchor uses to find the head, so the two cannot
                // disagree about whether a head pose exists.
                foreach (InputDevice d in InputSystem.devices)
                {
                    if (!d.added) continue;

                    var pos = d.TryGetChildControl<Vector3Control>("centerEyePosition");
                    if (pos == null) continue;

                    var tracked = d.TryGetChildControl<ButtonControl>("isTracked");
                    if (tracked != null && !tracked.isPressed) return false;

                    return pos.ReadValue() != Vector3.zero;
                }

                return false;
            }
        }

        private static InputDevice Device(Hand hand)
        {
            InputDevice cached = hand == Hand.Left ? _left : _right;
            if (cached != null && cached.added) return cached;

            if (Time.unscaledTime - _lastScan < RescanInterval)
                return null;

            Rescan();
            return hand == Hand.Left ? _left : _right;
        }

        private static void Rescan()
        {
            _lastScan = Time.unscaledTime;

            // PER HAND. Discarding both cached devices and re-resolving meant
            // that with one controller asleep the WORKING hand was thrown away
            // and rebuilt every rescan — and because the missing hand never
            // resolved, that happened on every interval, forever.
            InputDevice left = null;
            InputDevice right = null;

            foreach (InputDevice d in InputSystem.devices)
            {
                if (!d.added) continue;
                if (HasUsage(d, CommonUsages.LeftHand) && left == null) left = d;
                else if (HasUsage(d, CommonUsages.RightHand) && right == null) right = d;
            }

            // Only replace a hand we actually found. A hand that has gone to
            // sleep keeps its last device until a real one turns up.
            if (left != null) _left = left;
            if (right != null) _right = right;

            if (_left != null && !_left.added) _left = null;
            if (_right != null && !_right.added) _right = null;

            // Log the full device dump once, and thereafter only when the set of
            // resolved hands actually changes. It used to log every scan while
            // nothing was paired: hundreds of lines a second, which on the
            // headset is a frame-rate problem as well as a noise problem.
            string state = $"{(_left != null ? _left.deviceId : -1)}/" +
                           $"{(_right != null ? _right.deviceId : -1)}";

            if (_loggedOnce && state == _lastLoggedState) return;
            _loggedOnce = true;
            _lastLoggedState = state;

            var sb = new System.Text.StringBuilder("[XRControls] input devices:\n");
            foreach (InputDevice d in InputSystem.devices)
                sb.AppendLine($"   {d.displayName}  layout={d.layout}  usages={UsageList(d)}");
            sb.AppendLine($"   → left={(_left != null ? _left.displayName : "NONE")}" +
                          $"  right={(_right != null ? _right.displayName : "NONE")}");
            if (_left == null && _right == null)
                sb.AppendLine("   No controller carries a LeftHand/RightHand usage. " +
                              "Wake both controllers and check they are paired; the " +
                              "driver will be on gaze-and-dwell until one appears.");
            Debug.Log(sb.ToString());
        }

        private static bool HasUsage(InputDevice d, InternedString usage)
        {
            ReadOnlyArray<InternedString> usages = d.usages;
            for (int i = 0; i < usages.Count; i++)
                if (usages[i] == usage) return true;
            return false;
        }

        private static string UsageList(InputDevice d)
        {
            ReadOnlyArray<InternedString> usages = d.usages;
            if (usages.Count == 0) return "-";
            var parts = new List<string>(usages.Count);
            for (int i = 0; i < usages.Count; i++) parts.Add(usages[i].ToString());
            return string.Join("|", parts);
        }

        /// <summary>Forces a device rescan. Called when a controller is expected
        /// but none was found, e.g. at the start of a session.</summary>
        public static void ForceRescan()
        {
            _lastScan = -99f;
            Rescan();
        }

        // ---- analogue --------------------------------------------------------

        /// <summary>Trigger pull, 0..1. Falls back to the digital press when the
        /// runtime exposes no analogue axis.</summary>
        public static float Trigger(Hand hand) => Axis(hand, "trigger", "triggerPressed");

        /// <summary>Grip squeeze, 0..1.</summary>
        public static float Grip(Hand hand) => Axis(hand, "grip", "gripPressed");

        /// <summary>
        /// Reads an analogue control, trying the exact names first and then
        /// falling back to ANY axis on the device whose name contains the keyword.
        ///
        /// The exact-name lookup is correct for the OpenXR Oculus Touch profile,
        /// but it is one string away from silently returning zero forever, and a
        /// zero here is indistinguishable from a driver not pressing anything.
        /// That failure mode cost most of a day: the car would not move, and
        /// nothing anywhere said why. The keyword sweep costs one pass over the
        /// device's controls the first time and makes a layout rename a
        /// non-event.
        /// </summary>
        private static float Axis(Hand hand, string axisName, string buttonName)
        {
            InputDevice d = Device(hand);
            if (d == null) return 0f;

            if (d.TryGetChildControl<AxisControl>(axisName) is AxisControl a)
                return Mathf.Clamp01(a.ReadValue());

            if (d.TryGetChildControl<ButtonControl>(buttonName) is ButtonControl b)
                return b.isPressed ? 1f : 0f;

            float swept = SweepForAxis(d, axisName);
            if (swept > 0f) return swept;

            return 0f;
        }

        /// <summary>
        /// Last resort: walk the device's own control list for an axis whose name
        /// contains the keyword — "indexTrigger", "Trigger", "squeeze" and so on,
        /// depending on which interaction profile the runtime bound.
        /// </summary>
        private static float SweepForAxis(InputDevice device, string keyword)
        {
            float best = 0f;

            foreach (InputControl control in device.allControls)
            {
                if (control is not AxisControl axis) continue;
                if (control is Vector2Control) continue;
                if (control.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                float v = Mathf.Clamp01(axis.ReadValue());
                if (v > best) best = v;
            }

            return best;
        }

        public static Vector2 Thumbstick(Hand hand)
        {
            InputDevice d = Device(hand);
            if (d == null) return Vector2.zero;
            if (d.TryGetChildControl<Vector2Control>("thumbstick") is Vector2Control s)
                return s.ReadValue();
            if (d.TryGetChildControl<Vector2Control>("joystick") is Vector2Control j)
                return j.ReadValue();
            return Vector2.zero;
        }

        // ---- buttons ---------------------------------------------------------

        public static bool Held(Hand hand, string control)
        {
            InputDevice d = Device(hand);
            return d?.TryGetChildControl<ButtonControl>(control) is ButtonControl b && b.isPressed;
        }

        public static bool Pressed(Hand hand, string control)
        {
            InputDevice d = Device(hand);
            return d?.TryGetChildControl<ButtonControl>(control) is ButtonControl b
                   && b.wasPressedThisFrame;
        }

        /// <summary>A/X on that hand.</summary>
        public static bool PrimaryPressed(Hand hand) => Pressed(hand, "primaryButton");

        /// <summary>B/Y on that hand.</summary>
        public static bool SecondaryPressed(Hand hand) => Pressed(hand, "secondaryButton");

        /// <summary>
        /// Any button this project treats as "recentre me": A, X, Y, or the menu
        /// button on either controller. Deliberately generous — a driver who
        /// cannot see straight ahead should not have to find one specific button.
        /// </summary>
        private static readonly Hand[] BothHands = { Hand.Left, Hand.Right };

        public static bool RecenterPressed()
        {
            // Allocation-free: this runs every frame on the device.
            for (int i = 0; i < BothHands.Length; i++)
            {
                Hand h = BothHands[i];
                // primaryButton (A / X) ONLY.
                //
                // NOT secondaryButton (B / Y): that is VRSessionShortcuts —
                // ending a drive early and going back to the menu — and a button
                // that both recentres you and ends your run is unusable.
                //
                // NOT thumbstickClicked either. The left thumbstick is the
                // steering fallback, and clicking it is one firm push away from
                // steering with it. Drivers were recentring mid-corner, which
                // rebases the seat to a head pose taken while looking into the
                // turn and leaves them sitting permanently at an angle.
                if (Pressed(h, "primaryButton"))
                    return true;
            }
            return false;
        }

        // "Select" is deliberately NOT offered here. Edge detection belongs to
        // the one component that owns it (VRUIPointer), which thresholds the
        // analogue trigger itself. A shared static edge flag would be consumed by
        // whichever caller read it first, which is the kind of bug that presents
        // as "the button works sometimes".
        //
        // Select is also never bound to primaryButton, because that is the
        // recentre button — one physical button doing two things makes both feel
        // broken.

        // ---- pose ------------------------------------------------------------

        /// <summary>
        /// Controller pose in TRACKING space — the space of the XR Origin's
        /// camera-offset object, not world space. Convert with
        /// cameraOffset.TransformPoint / TransformRotation before use.
        /// </summary>
        public static bool TryGetPose(Hand hand, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            InputDevice d = Device(hand);
            if (d == null) return false;

            bool got = false;

            if (d.TryGetChildControl<Vector3Control>("devicePosition") is Vector3Control p)
            { position = p.ReadValue(); got = true; }

            if (d.TryGetChildControl<QuaternionControl>("deviceRotation") is QuaternionControl r)
            { rotation = r.ReadValue(); got = true; }

            return got;
        }
    }
}
