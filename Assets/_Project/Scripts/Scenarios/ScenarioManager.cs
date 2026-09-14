using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IDS.Core;
using IDS.AI;

namespace IDS.Scenarios
{
    /// <summary>
    /// Arms scenarios in the order the adaptive selector asks for, ticks the
    /// active one, and never lets two hazards run at once — which is a real
    /// failure mode: two overlapping hazards make the reaction-time measurement
    /// meaningless because you cannot tell which one the driver braked for.
    ///
    /// Implements IScenarioHost so SessionManager drives it.
    ///
    /// OWNER: Omkar.
    /// </summary>
    public class ScenarioManager : MonoBehaviour, IScenarioHost
    {
        [Tooltip("All hazard scenarios in the scene. Auto-collected if empty.")]
        [SerializeField] private List<HazardScenarioBase> scenarios = new();

        [Tooltip("How many hazards to run in one session.")]
        [SerializeField] private int hazardsPerSession = 2;

        [Tooltip("Minimum seconds of ordinary driving between hazards. Without " +
                 "this, back-to-back hazards feel like a shooting gallery rather " +
                 "than a drive.")]
        [SerializeField] private float minSecondsBetweenHazards = 20f;

        [SerializeField] private AdaptiveScenarioSelector selector;
        [SerializeField] private DriverProfileManager profileManager;

        public IReadOnlyList<ScenarioResult> SessionResults => _results;

        private readonly List<ScenarioResult> _results = new();
        private Queue<string> _plan = new();
        private HazardScenarioBase _armed;
        private float _lastResolvedAt = -999f;

        private void Awake()
        {
            if (scenarios.Count == 0)
                scenarios.AddRange(GetComponentsInChildren<HazardScenarioBase>(true));

            ServiceRegistry.Register<IScenarioHost>(this);
            ServiceRegistry.Register(this);
        }

        private void Start()
        {
            selector ??= ServiceRegistry.Resolve<AdaptiveScenarioSelector>();
            profileManager ??= ServiceRegistry.Resolve<DriverProfileManager>();

            if (scenarios.Count == 0)
                Debug.LogWarning("[Scenarios] none found in the scene.");
            else
                selector?.SetAvailableScenarios(scenarios.Select(s => s.ScenarioId));
        }

        private void OnEnable()  => SessionEvents.ScenarioResolved += OnResolved;
        private void OnDisable() => SessionEvents.ScenarioResolved -= OnResolved;

        public void ArmScenariosForSession()
        {
            _results.Clear();
            ResetAll();

            List<string> plan = selector != null
                ? selector.SelectSessionPlan(profileManager?.Current, hazardsPerSession)
                : scenarios.Take(hazardsPerSession).Select(s => s.ScenarioId).ToList();

            _plan = new Queue<string>(plan);
            _lastResolvedAt = Time.time - minSecondsBetweenHazards; // first one can fire immediately
            ArmNext();
        }

        private void ArmNext()
        {
            _armed = null;
            if (_plan.Count == 0)
            {
                Debug.Log("[Scenarios] session plan complete.");
                return;
            }

            string id = _plan.Dequeue();
            var scenario = scenarios.FirstOrDefault(s => s.ScenarioId == id);

            if (scenario == null)
            {
                Debug.LogWarning($"[Scenarios] planned '{id}' is not in the scene, skipping.");
                ArmNext();
                return;
            }

            scenario.Arm();
            _armed = scenario;
            Debug.Log($"[Scenarios] armed {id} ({_plan.Count} remaining)");
        }

        private void Update()
        {
            if (_armed == null) return;

            // Hold the next hazard back until the spacing has elapsed.
            if (_armed.State == ScenarioState.Armed &&
                Time.time - _lastResolvedAt < minSecondsBetweenHazards) return;

            _armed.Tick(Time.deltaTime);
        }

        private void OnResolved(ScenarioResult result)
        {
            _results.Add(result);
            _lastResolvedAt = Time.time;
            ArmNext();
        }

        public void DisarmAll()
        {
            foreach (var s in scenarios)
                if (s.State == ScenarioState.Armed) s.Reset();
            _armed = null;
            _plan.Clear();
        }

        public void ResetAll()
        {
            foreach (var s in scenarios) s.Reset();
            _armed = null;
        }

        /// <summary>Debug hook: fire the armed hazard now. Bind to a key in the test scene.</summary>
        public void ForceTriggerArmed()
        {
            if (_armed == null) { Debug.Log("[Scenarios] nothing armed."); return; }
            _armed.Trigger();
        }
    }
}
