using UnityEngine;
using UnityEngine.InputSystem;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Desktop development input. This is what lets the team work without the
    /// shared lab headsets, so it is a first-class input path, not a throwaway —
    /// keep it working.
    ///
    ///     A / D  or  ← →      steer
    ///     W      or  ↑        accelerate
    ///     S / Space  or  ↓    brake
    ///
    /// Reads the Input System directly rather than the legacy Input class. That
    /// is what lets Active Input Handling be "Input System Package (New)" instead
    /// of "Both": the old Input Manager is deprecated, Unity warns about it on
    /// every project load, and keeping it on ships a second input backend in the
    /// player for the sake of three development-only scripts.
    ///
    /// It still needs no action asset — `Keyboard.current` is a direct device
    /// read — so desktop driving cannot be broken by input configuration.
    ///
    /// NOT AVAILABLE ON DEVICE. IsAvailable is false on Android, so a headset
    /// session can never quietly fall back to a keyboard path nobody is touching
    /// and record the result as driver behaviour.
    /// </summary>
    public class KeyboardInputProvider : MonoBehaviour, IInputProvider
    {
        [SerializeField] private float steeringRate = 2.5f;
        [SerializeField] private float steeringReturnRate = 4f;

        public int Priority => 1;
        public bool SuppliesPedals => true;
        public string ProviderName => "Keyboard (desktop)";

        public bool IsAvailable => Application.platform != RuntimePlatform.Android
                                   && Keyboard.current != null;

        private float _steering;

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            float raw = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) raw -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) raw += 1f;

            _steering = Mathf.Approximately(raw, 0f)
                ? Mathf.MoveTowards(_steering, 0f, steeringReturnRate * Time.deltaTime)
                : Mathf.MoveTowards(_steering, raw, steeringRate * Time.deltaTime);
        }

        public float ReadSteering() => Mathf.Clamp(_steering, -1f, 1f);

        public float ReadAccelerator()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return 0f;
            return kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f;
        }

        public float ReadBrake()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return 0f;
            return kb.sKey.isPressed || kb.downArrowKey.isPressed
                   || kb.spaceKey.isPressed ? 1f : 0f;
        }
    }
}
