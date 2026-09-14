using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// FALLBACK B: desktop development. This is what lets Ananya, Anushka and
    /// Omkar work every day without the shared lab headsets, so it is not a
    /// throwaway — treat it as a first-class input path and keep it working.
    ///
    /// A / D  or  ← →   steer
    /// W  or  ↑         accelerate
    /// S / Space or ↓   brake
    ///
    /// Uses the legacy Input axes so it works whether or not the new Input System
    /// package is wired up. Project Settings → Player → Active Input Handling
    /// must be "Both".
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public class KeyboardInputProvider : MonoBehaviour, IInputProvider
    {
        [SerializeField] private float steeringReturnRate = 4f;
        [SerializeField] private float steeringRate = 2.5f;

        public int Priority => 1;
        public bool IsAvailable => Application.isEditor || !Application.isMobilePlatform;
        public string ProviderName => "Keyboard (desktop)";

        private float _steering;

        private void Update()
        {
            float raw = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  raw -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) raw += 1f;

            if (Mathf.Approximately(raw, 0f))
                _steering = Mathf.MoveTowards(_steering, 0f, steeringReturnRate * Time.deltaTime);
            else
                _steering = Mathf.MoveTowards(_steering, raw, steeringRate * Time.deltaTime);
        }

        public float ReadSteering() => Mathf.Clamp(_steering, -1f, 1f);

        public float ReadAccelerator()
            => (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ? 1f : 0f;

        public float ReadBrake()
            => (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)
                || Input.GetKey(KeyCode.Space)) ? 1f : 0f;
    }
}
