using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IDS.Core;
using IDS.AI;

namespace IDS.Scenarios
{
    /// <summary>
    /// Arms scenarios in the order the adaptive selector asks for, ticks the armed
    /// one, and never lets two hazards run at once. That last part is not
    /// tidiness: with two overlapping hazards the reaction-time measurement is
    /// meaningless, because there is no way to tell which one the driver braked
    /// for.
    ///
    /// Implements IScenarioHost so SessionManager drives it.
    /// </summary>
    public class ScenarioManager : MonoBehaviour, IScenarioHost
    {
        [Tooltip("All hazard scenarios in the scene. Auto-collected if empty.")]
        [SerializeField] private List<HazardScenarioBase> scenarios = new();

        [Tooltip("How many hazards to run in one session.")]
        [SerializeField] private int hazardsPerSession = 2;

        [Tooltip("Minimum seconds of ordinary driving between hazards. Without " +
                 "this, back-to-back hazards feel like a shooting gallery rather " +
                 "than a drive, and a braced driver is not the reflex being " +
                 "measured.")]
        [SerializeField] private float minSecondsBetweenHazards = 20f;

        [SerializeField] private AdaptiveScenarioSelector selector;
        [SerializeField] private DriverProfileManager profileManager;

        public IReadOnlyList<ScenarioResult> SessionResults => _results;

        /// <summary>The hazard currently armed or running, for the HUD and logs.</summary>
        public string ArmedScenarioId => _armed != null ? _armed.ScenarioId : "";

        private readonly List<ScenarioResult> _results = new();
        private Queue<string> _plan = new();
        private HazardScenarioBase _armed;

        // Selected but NOT yet armed. Arming places a hazard relative to where
        // the car is at that instant, so it must not happen until the hazard is
        // allowed to start looking for its trigger.
        private HazardScenarioBase _pending;
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
            {
                Debug.LogWarning("[Scenarios] none found in the scene — no hazard " +
                                 "will ever fire and hazard_response will be " +
                                 "unmeasured in every session.");
                return;
            }

            // Safe here: HazardScenarioBase resolves its id in Awake, so every id
            // is final before any Start runs.
            var ids = scenarios.Select(s => s.ScenarioId).ToList();
            selector?.SetAvailableScenarios(ids);
            Debug.Log($"[Scenarios] {scenarios.Count} in the scene: {string.Join(", ", ids)}");

            foreach (HazardScenarioBase s in scenarios)
            {
                if (!string.IsNullOrWhiteSpace(s.ScenarioId)) continue;
                Debug.LogError($"[Scenarios] '{s.name}' has an empty ScenarioId. It " +
                               "can never be planned or reported.");
            }
        }

        private void OnEnable() => SessionEvents.ScenarioResolved += OnResolved;
        private void OnDisable() => SessionEvents.ScenarioResolved -= OnResolved;

        public void ArmScenariosForSession()
        {
            _results.Clear();
            ResetAll();

            List<string> plan = selector != null
                ? selector.SelectSessionPlan(profileManager?.Current, hazardsPerSession)
                : scenarios.Take(hazardsPerSession).Select(s => s.ScenarioId).ToList();

            _plan = new Queue<string>(plan);

            // The first hazard may fire as soon as its own trigger condition is
            // met, without waiting out the inter-hazard spacing.
            _lastResolvedAt = Time.time - minSecondsBetweenHazards;
            SelectNext();
        }

        /// <summary>
        /// Chooses the next hazard from the plan. Does NOT arm it.
        ///
        /// SELECTION AND PLACEMENT ARE SEPARATE, and this is the single most
        /// damaging bug the project had. Arm() places a hazard relative to the
        /// car's position at that instant — the pedestrian 90 m ahead, the
        /// cyclist 130 m ahead. The old code armed the next hazard the moment the
        /// previous one resolved, and then Update() refused to tick it until the
        /// 20 s inter-hazard spacing had elapsed.
        ///
        /// At 40 km/h the car covers 222 m in those 20 s. So the pedestrian was
        /// 132 m BEHIND the driver before it was ever allowed to look for its
        /// trigger, and the cyclist had been overtaken. Neither could fire.
        /// Neither resolved. The queue stopped. That is the whole explanation for
        /// "the pedestrian never appeared" and "the cyclist just stands there".
        /// </summary>
        private void SelectNext()
        {
            _armed = null;
            _pending = null;

            while (_plan.Count > 0)
            {
                string id = _plan.Dequeue();
                HazardScenarioBase scenario = scenarios.FirstOrDefault(s => s.ScenarioId == id);

                if (scenario == null)
                {
                    Debug.LogWarning($"[Scenarios] planned '{id}' is not in the scene, " +
                                     "skipping it.");
                    continue;
                }

                _pending = scenario;
                Debug.Log($"[Scenarios] next up: {id} ({_plan.Count} remaining after it)");
                return;
            }

            Debug.Log("[Scenarios] session plan complete.");
        }

        private void Update()
        {
            // Arm the pending hazard only once the spacing has elapsed. Arming is
            // what places it on the road ahead of the driver, so doing it here
            // rather than at selection time means it is placed where the driver
            // actually is when it becomes live.
            if (_armed == null && _pending != null
                && Time.time - _lastResolvedAt >= minSecondsBetweenHazards)
            {
                _armed = _pending;
                _pending = null;
                _armed.Arm();
                Debug.Log($"[Scenarios] armed {_armed.ScenarioId}");
            }

            if (_armed == null) return;

            _armed.Tick(Time.deltaTime);
        }

        private void OnResolved(ScenarioResult result)
        {
            _results.Add(result);
            _lastResolvedAt = Time.time;
            SelectNext();
        }

        /// <summary>
        /// Stops everything at the end of a drive.
        ///
        /// This previously reset only scenarios in the Armed state, which left a
        /// Triggered or Active hazard running: the pedestrian kept walking and the
        /// lead car kept braking behind the report panel, and the next drive
        /// started with a hazard already half-played. Resetting Armed, Triggered
        /// and Active covers every state a hazard can be in while a drive is
        /// under way.
        ///
        /// Resolved and Logged scenarios are left alone so their results remain
        /// inspectable; the session's results are held in _results independently,
        /// so nothing the report needs is lost either way.
        /// </summary>
        public void DisarmAll()
        {
            foreach (HazardScenarioBase s in scenarios)
            {
                if (s == null) continue;
                if (s.State == ScenarioState.Active)
                {
                    // RESOLVE an active hazard, do not reset it. Resetting threw
                    // away a completed measurement: brake hard for a pedestrian,
                    // have the drive timer expire a second later, and the report
                    // said no hazards occurred and the reaction time was lost.
                    Debug.Log($"[Scenarios] drive ended during {s.ScenarioId} — "
                              + "resolving it so the response is scored");
                    s.Resolve();
                    continue;
                }

                if (s.State == ScenarioState.Armed
                    || s.State == ScenarioState.Triggered)
                {
                    Debug.Log($"[Scenarios] disarming {s.ScenarioId} (was {s.State})");
                    s.Reset();
                }
            }

            _armed = null;
            _pending = null;
            _plan.Clear();
        }

        public void ResetAll()
        {
            foreach (HazardScenarioBase s in scenarios)
                if (s != null) s.Reset();
            _armed = null;
            _pending = null;
        }

        /// <summary>Debug hook: fire the armed hazard now. Bound to H by DevHotkeys.</summary>
        public void ForceTriggerArmed()
        {
            // Arm the pending one first, so the dev hotkey works during the
            // inter-hazard spacing instead of reporting "nothing armed".
            if (_armed == null && _pending != null)
            {
                _armed = _pending;
                _pending = null;
                _armed.Arm();
            }

            if (_armed == null) { Debug.Log("[Scenarios] nothing armed."); return; }
            _armed.Trigger();
        }

        public void Configure(IEnumerable<HazardScenarioBase> all, int perSession)
        {
            scenarios.Clear();
            scenarios.AddRange(all);
            hazardsPerSession = perSession;
        }
    }
}
