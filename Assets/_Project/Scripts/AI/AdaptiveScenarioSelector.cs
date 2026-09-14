using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IDS.Core;

namespace IDS.AI
{
    /// <summary>
    /// Chooses the next hazard from the driver profile. This is the closing of
    /// the loop — the thing that makes the post-drive report an input rather than
    /// an ending.
    ///
    /// Mapping (weakest skill → hazard that probes it):
    ///   MirrorAwareness    → cyclist in the blind spot
    ///   HazardResponse     → sudden lead-vehicle braking
    ///   GapAcceptance      → intersection approach (uses the pedestrian hazard
    ///                        at the junction until a dedicated one exists)
    ///   LaneKeeping        → low-traffic lane-keeping drive, pedestrian hazard
    ///   SteeringSmoothness → same as lane keeping
    ///
    /// Controlled randomisation: the weakest skill's hazard is weighted heavily
    /// but not guaranteed, because a driver who knows exactly what is coming
    /// braces for it, and a braced response is not the reflex we are trying to
    /// measure.
    ///
    /// REPRODUCIBILITY: set a fixed seed during formal user testing so a session
    /// can be replayed. Leave it at 0 for a random seed in normal use.
    ///
    /// OWNER: Anushka. Consumer: Omkar's ScenarioManager.
    /// </summary>
    public class AdaptiveScenarioSelector : MonoBehaviour
    {
        [Header("Randomisation")]
        [Tooltip("Probability the targeted hazard is chosen. The remainder is " +
                 "spread over the other eligible hazards.")]
        [Range(0.4f, 1f)]
        [SerializeField] private float targetedWeight = 0.7f;

        [Tooltip("0 = random each run. Set a fixed non-zero value during formal " +
                 "user testing so sessions are reproducible. Record the seed.")]
        [SerializeField] private int randomSeed;

        [Header("Scenario ids available in the build")]
        [SerializeField] private List<string> availableScenarioIds = new()
        {
            "pedestrian_step_out", "cyclist_blind_spot", "lead_vehicle_brake"
        };

        private System.Random _rng;

        private void Awake()
        {
            _rng = randomSeed == 0
                ? new System.Random()
                : new System.Random(randomSeed);

            if (randomSeed != 0)
                Debug.Log($"[Selector] fixed seed {randomSeed} — session is reproducible.");

            ServiceRegistry.Register(this);
        }

        /// <summary>
        /// The hazard id to run next. Pass null on a first-ever session and it
        /// returns the pedestrian hazard, which is the gentlest introduction.
        /// </summary>
        public string SelectNext(DriverProfile profile)
        {
            if (availableScenarioIds.Count == 0)
            {
                Debug.LogError("[Selector] no scenario ids configured.");
                return "";
            }

            if (profile == null || profile.sessionsCompleted == 0)
                return availableScenarioIds.Contains("pedestrian_step_out")
                    ? "pedestrian_step_out"
                    : availableScenarioIds[0];

            string targeted = HazardFor(profile.WeakestSkillEnum);
            bool targetedAvailable = availableScenarioIds.Contains(targeted);

            if (targetedAvailable && _rng.NextDouble() < targetedWeight)
            {
                Debug.Log($"[Selector] targeting {profile.weakestSkill} → {targeted}");
                return targeted;
            }

            var others = availableScenarioIds
                .Where(id => id != targeted)
                .ToList();

            if (others.Count == 0) return targeted;

            string pick = others[_rng.Next(others.Count)];
            Debug.Log($"[Selector] randomised away from {targeted} → {pick}");
            return pick;
        }

        /// <summary>
        /// A whole session's hazard order. The scenario manager arms these in
        /// sequence. Deliberately not the same hazard three times.
        /// </summary>
        public List<string> SelectSessionPlan(DriverProfile profile, int count = 2)
        {
            var plan = new List<string>();
            var pool = new List<string>(availableScenarioIds);

            string first = SelectNext(profile);
            plan.Add(first);
            pool.Remove(first);

            while (plan.Count < count && pool.Count > 0)
            {
                string pick = pool[_rng.Next(pool.Count)];
                plan.Add(pick);
                pool.Remove(pick);
            }

            Debug.Log($"[Selector] session plan: {string.Join(" → ", plan)}");
            return plan;
        }

        public static string HazardFor(SkillArea skill) => skill switch
        {
            SkillArea.MirrorAwareness    => "cyclist_blind_spot",
            SkillArea.HazardResponse     => "lead_vehicle_brake",
            SkillArea.GapAcceptance      => "pedestrian_step_out",
            SkillArea.LaneKeeping        => "pedestrian_step_out",
            SkillArea.SteeringSmoothness => "pedestrian_step_out",
            _                            => "pedestrian_step_out"
        };

        /// <summary>Human-readable recommendation for the post-drive report.</summary>
        public string DescribeRecommendation(DriverProfile profile)
        {
            if (profile == null) return "Introductory drive";

            SkillArea weakest = profile.WeakestSkillEnum;
            float score = profile.ScoreFor(weakest);

            // A driver who is good at everything gets harder scenarios, not a
            // remedial one.
            if (score > 80f)
                return "All skills strong — next session increases hazard difficulty";

            return weakest switch
            {
                SkillArea.MirrorAwareness =>
                    "Blind-spot practice: cyclist approaching on the kerb side",
                SkillArea.HazardResponse =>
                    "Following-distance practice: sudden braking ahead",
                SkillArea.GapAcceptance =>
                    "Intersection refresher: judging gaps in cross traffic",
                SkillArea.LaneKeeping =>
                    "Lane-keeping refresher, low traffic",
                SkillArea.SteeringSmoothness =>
                    "Smooth-steering practice on a gentle route",
                _ => "General adaptation drive"
            };
        }

        public void SetAvailableScenarios(IEnumerable<string> ids)
        {
            availableScenarioIds = ids.ToList();
        }
    }
}
