using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// The seam between XR input and vehicle physics. Collects every
    /// IInputProvider in the scene, and each frame uses the highest-priority
    /// available one. If hand tracking drops mid-corner, steering falls through
    /// to the controller (or keyboard) without the car snapping straight.
    ///
    /// This is why Ananya's VehicleController must never read Input directly:
    /// it reads Steering/Accelerator/Brake off this component.
    ///
    /// SPECIAL CASE — pedals. WheelInputProvider deliberately returns 0 for
    /// accelerator and brake. So pedals are resolved separately from steering:
    /// steering comes from the best steering-capable provider, pedals from the
    /// best pedal-capable provider. Without this split, hand-tracked steering
    /// would leave the car unable to brake.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public class VehicleInputAdapter : MonoBehaviour
    {
        [Tooltip("Leave empty to auto-collect every IInputProvider on this " +
                 "GameObject and its children.")]
        [SerializeField] private List<MonoBehaviour> providerRefs = new();

        [Header("Output (read-only, for inspection)")]
        [SerializeField] private float steering;
        [SerializeField] private float accelerator;
        [SerializeField] private float brake;

        public float Steering    => steering;
        public float Accelerator => accelerator;
        public float Brake       => brake;

        public string ActiveSteeringProvider { get; private set; } = "none";
        public string ActivePedalProvider    { get; private set; } = "none";

        private readonly List<IInputProvider> _providers = new();
        private string _lastAnnounced = "";

        private void Awake()
        {
            _providers.Clear();

            if (providerRefs.Count > 0)
                _providers.AddRange(providerRefs.OfType<IInputProvider>());
            else
                _providers.AddRange(GetComponentsInChildren<IInputProvider>(true));

            _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            if (_providers.Count == 0)
                Debug.LogError("[InputAdapter] no IInputProvider found. The vehicle " +
                               "will not move. Add KeyboardInputProvider at minimum.");

            ServiceRegistry.Register(this);
        }

        private void Update()
        {
            var steer = _providers.FirstOrDefault(p => p.IsAvailable);

            // A provider is pedal-capable if it reports non-zero for either pedal
            // OR is not the wheel provider. Cheap and works: the wheel provider is
            // the only one that hard-returns zero.
            var pedals = _providers.FirstOrDefault(
                p => p.IsAvailable && !(p is WheelInputProvider));

            steering = steer?.ReadSteering() ?? 0f;
            accelerator = pedals?.ReadAccelerator() ?? 0f;
            brake = pedals?.ReadBrake() ?? 0f;

            ActiveSteeringProvider = steer?.ProviderName ?? "none";
            ActivePedalProvider = pedals?.ProviderName ?? "none";

            string combined = $"{ActiveSteeringProvider} / {ActivePedalProvider}";
            if (combined != _lastAnnounced)
            {
                _lastAnnounced = combined;
                Debug.Log($"[InputAdapter] steering: {ActiveSteeringProvider} · " +
                          $"pedals: {ActivePedalProvider}");
                SessionEvents.RaiseInputProviderChanged(combined);
            }
        }

        /// <summary>Test hook so Anushka can replay recorded input without hardware.</summary>
        public void Override(float steer, float accel, float brk)
        {
            steering = Mathf.Clamp(steer, -1f, 1f);
            accelerator = Mathf.Clamp01(accel);
            brake = Mathf.Clamp01(brk);
        }
    }
}
