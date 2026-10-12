using System.Collections.Generic;
using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// The seam between input and vehicle physics. Collects every IInputProvider
    /// in the scene and each frame uses the highest-priority available one, so a
    /// provider dropping out mid-corner falls through instead of snapping the car
    /// straight.
    ///
    /// This is why VehicleController must never read input directly: it reads
    /// Steering / Accelerator / Brake off this component.
    ///
    /// STEERING AND PEDALS ARE RESOLVED SEPARATELY, keyed on
    /// IInputProvider.SuppliesPedals. A provider that steers but has no pedals
    /// would otherwise leave the car unable to brake. Today both providers supply
    /// both, so the two resolve to the same object — the split stays because the
    /// alternative is a silently unbrakeable car the first time that stops being
    /// true.
    ///
    /// NOTHING DRIVES THE CAR ON THE DRIVER'S BEHALF. An earlier build had an
    /// auto-drive fallback that held the throttle at a constant 0.35 whenever no
    /// real input was available. It made the car move in a demo, and it wrote that
    /// constant into the telemetry as though the participant had done it — every
    /// recorded session had an identical accelerator trace. If no provider is
    /// available the car does not move, which is visible immediately and cannot
    /// be mistaken for data.
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

        public float Steering => steering;
        public float Accelerator => accelerator;
        public float Brake => brake;

        public string ActiveSteeringProvider { get; private set; } = "none";
        public string ActivePedalProvider { get; private set; } = "none";

        /// <summary>True when no provider can currently supply input at all.</summary>
        public bool NoInputAvailable => _steerProvider == null && _pedalProvider == null;

        /// <summary>
        /// True when a provider IS available and claims to be fine, but has been
        /// returning dead-centre zero on every axis for a sustained period while
        /// the car is supposed to be driveable.
        ///
        /// This is the failure the old watchdog could not see. It only fired when
        /// there was no provider at all, so a live provider returning steady zero
        /// — a mis-bound trigger, a controller asleep but still "added" — logged
        /// nothing whatsoever. That silence is why a dead throttle took a day to
        /// find.
        /// </summary>
        public bool InputLooksDead { get; private set; }

        private readonly List<IInputProvider> _providers = new();
        private IInputProvider _steerProvider;
        private IInputProvider _pedalProvider;
        private string _lastAnnounced = "";
        [Tooltip("Seconds of a live provider returning dead-centre zero on every "
                 + "axis before the adapter says so.")]
        [SerializeField] private float deadInputSeconds = 8f;

        private float _nextNoInputWarning;
        private float _deadFor;
        private float _nextRediscover;

        private void Awake()
        {
            _providers.Clear();

            if (providerRefs.Count > 0)
            {
                foreach (MonoBehaviour mb in providerRefs)
                    if (mb is IInputProvider p) _providers.Add(p);
            }
            else
            {
                _providers.AddRange(GetComponentsInChildren<IInputProvider>(true));
            }

            // Highest priority first.
            _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            if (_providers.Count == 0)
                Debug.LogError("[InputAdapter] no IInputProvider found. The vehicle " +
                               "cannot be driven. The scene builder should have added " +
                               "VRSteeringWheel and KeyboardInputProvider here.");
            else
                Debug.Log("[InputAdapter] providers, highest priority first: " +
                          string.Join(", ", Describe()));

            ServiceRegistry.Register(this);
        }

        private IEnumerable<string> Describe()
        {
            foreach (IInputProvider p in _providers)
                yield return $"{p.ProviderName} (p{p.Priority})";
        }

        private void Update()
        {
            if (Time.time >= _nextRediscover)
            {
                _nextRediscover = Time.time + 5f;
                RediscoverProviders();
            }

            _steerProvider = null;
            _pedalProvider = null;

            foreach (IInputProvider p in _providers)
            {
                if (!p.IsAvailable) continue;
                _steerProvider ??= p;
                if (p.SuppliesPedals) _pedalProvider ??= p;
                if (_steerProvider != null && _pedalProvider != null) break;
            }

            steering = _steerProvider?.ReadSteering() ?? 0f;
            accelerator = _pedalProvider?.ReadAccelerator() ?? 0f;
            brake = _pedalProvider?.ReadBrake() ?? 0f;

            ActiveSteeringProvider = _steerProvider?.ProviderName ?? "none";
            ActivePedalProvider = _pedalProvider?.ProviderName ?? "none";

            string combined = $"{ActiveSteeringProvider} / {ActivePedalProvider}";
            if (combined != _lastAnnounced)
            {
                _lastAnnounced = combined;
                Debug.Log($"[InputAdapter] steering: {ActiveSteeringProvider} · " +
                          $"pedals: {ActivePedalProvider}");
                SessionEvents.RaiseInputProviderChanged(combined);
            }

            WarnIfNoInput();
            WatchForDeadInput();
        }

        /// <summary>
        /// Periodically re-reads the provider list. One AddComponent at runtime,
        /// or a provider that only registers itself late, used to leave this
        /// adapter holding a stale list for the whole session with no way back.
        /// </summary>
        private void RediscoverProviders()
        {
            var found = new List<IInputProvider>(
                GetComponentsInChildren<IInputProvider>(true));

            if (found.Count == _providers.Count) return;

            _providers.Clear();
            _providers.AddRange(found);
            _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            Debug.Log("[InputAdapter] provider list changed: "
                      + string.Join(", ", Describe()));
        }

        private void WatchForDeadInput()
        {
            bool anyProvider = _steerProvider != null || _pedalProvider != null;
            bool allZero = Mathf.Abs(steering) < 0.001f
                           && accelerator < 0.001f && brake < 0.001f;

            if (!anyProvider || !allZero) { _deadFor = 0f; InputLooksDead = false; return; }

            _deadFor += Time.deltaTime;
            if (_deadFor < deadInputSeconds) return;

            InputLooksDead = true;
            _deadFor = 0f;

            Debug.LogWarning($"[InputAdapter] {deadInputSeconds:F0}s with a live "
                             + $"provider ({ActivePedalProvider}) returning zero on "
                             + "every axis. Either nobody is touching the controls, "
                             + "or the bindings are not reaching the device. "
                             + $"left={XRControls.Available(Hand.Left)} "
                             + $"right={XRControls.Available(Hand.Right)}");

            RediscoverProviders();
            XRControls.ForceRescan();
        }

        /// <summary>
        /// Says so, repeatedly, when nothing can drive the car. A silent dead
        /// control is the single most expensive failure mode in this project: it
        /// looks exactly like a driver sitting still.
        /// </summary>
        private void WarnIfNoInput()
        {
            if (!NoInputAvailable || Time.time < _nextNoInputWarning) return;
            _nextNoInputWarning = Time.time + 5f;

            Debug.LogWarning("[InputAdapter] no input provider is available — the car " +
                             "will not move. On the headset this means no controller " +
                             "is paired and tracking; wake both controllers. " +
                             $"XRControls sees left={XRControls.Available(Hand.Left)} " +
                             $"right={XRControls.Available(Hand.Right)}.");
            XRControls.ForceRescan();
        }

        /// <summary>Test hook: replay recorded input without hardware.</summary>
        public void Override(float steer, float accel, float brk)
        {
            steering = Mathf.Clamp(steer, -1f, 1f);
            accelerator = Mathf.Clamp01(accel);
            brake = Mathf.Clamp01(brk);
        }

        public void SetProviders(IEnumerable<MonoBehaviour> providers)
        {
            providerRefs.Clear();
            providerRefs.AddRange(providers);
        }
    }
}
