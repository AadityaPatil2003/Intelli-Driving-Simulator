using UnityEngine;
using IDS.Core;

namespace IDS.AI
{
    /// <summary>
    /// Deterministic scoring. This is the guaranteed intelligence layer: it works
    /// at Milestone 2, it needs no data, and it stays in the build as the fallback
    /// whether or not the classifier ever reaches usable accuracy.
    ///
    /// WEIGHTS ARE PROJECT DESIGN PARAMETERS, NOT VALIDATED SAFETY VALUES.
    /// They are exposed in the inspector precisely so they can be adjusted after
    /// internal pilot sessions, and the final report must say they were chosen by
    /// the team rather than derived from road-safety research.
    ///
    ///   Lane offset      30%
    ///   Steering         20%
    ///   Mirror           20%
    ///   Gap              15%
    ///   Hazard response  15%
    ///
    /// Direction of each contribution (this is the part that makes the score mean
    /// something rather than being an arbitrary number):
    ///   frequent mirror checks       → LOWER risk
    ///   conservative (larger) gaps   → LOWER risk
    ///   larger lane offset RMS       → HIGHER risk
    ///   jerkier steering             → HIGHER risk
    ///   slower hazard reaction       → HIGHER risk
    ///
    /// OWNER: Anushka.
    /// </summary>
    public class RuleBasedRiskScorer : MonoBehaviour, IRiskScorer
    {
        [Header("Weights (must sum to 1.0)")]
        [SerializeField] private float wLane     = 0.30f;
        [SerializeField] private float wSteering = 0.20f;
        [SerializeField] private float wMirror   = 0.20f;
        [SerializeField] private float wGap      = 0.15f;
        [SerializeField] private float wHazard   = 0.15f;

        [Header("Reference values - the point at which a feature scores 'bad'")]
        [Tooltip("Lane offset RMS in metres considered fully poor lane keeping.")]
        [SerializeField] private float laneOffsetPoorMetres = 0.9f;
        [Tooltip("Reaction time in seconds considered a fully poor response.")]
        [SerializeField] private float reactionPoorSeconds = 2.2f;
        [Tooltip("Reaction time considered excellent.")]
        [SerializeField] private float reactionGoodSeconds = 0.7f;

        [Header("Collision penalty")]
        [Tooltip("Composite risk added per collision, on top of the weighted score.")]
        [SerializeField] private float collisionPenalty = 12f;

        [SerializeField] private CountryProfileManager profileManager;

        public string ScorerName => "rule_based_v1";
        public bool IsUsable => true;   // always. that is the whole point.

        private void Awake() => ServiceRegistry.Register<IRiskScorer>(this);

        private void Start() => profileManager ??= ServiceRegistry.Resolve<CountryProfileManager>();

        public RiskAssessment Score(DriverFeatureSet f)
        {
            CountryProfile profile = profileManager?.Current;

            float expectedMirror = profile != null ? profile.expectedMirrorChecksPerMinute : 6f;
            float safeGap = profile != null ? profile.safeGapSeconds : 4f;

            // ---- per-skill SCORES: 0..100 where HIGHER IS BETTER ------------

            float laneScore = 100f * (1f - Mathf.Clamp01(
                f.LaneOffsetRms / Mathf.Max(0.01f, laneOffsetPoorMetres)));

            // SteeringJerk arrives already normalised 0..1 by the extractor.
            float steeringScore = 100f * (1f - Mathf.Clamp01(f.SteeringJerk));

            float mirrorScore = 100f * Mathf.Clamp01(
                f.MirrorCheckFreq / Mathf.Max(0.1f, expectedMirror));

            // Gap: -1 means not measured. Score it neutral rather than punishing
            // the driver for a scenario the session did not contain.
            float gapScore = f.GapAcceptance < 0f
                ? 50f
                : 100f * Mathf.Clamp01(f.GapAcceptance / Mathf.Max(0.1f, safeGap));

            // Reaction: -1 means no hazard occurred. Neutral again.
            float hazardScore;
            if (f.ReactionTime < 0f) hazardScore = 50f;
            else
            {
                float t = Mathf.InverseLerp(reactionPoorSeconds, reactionGoodSeconds,
                                            f.ReactionTime);
                hazardScore = 100f * Mathf.Clamp01(t);
            }

            // ---- composite RISK: 0..100 where HIGHER IS WORSE --------------

            float sumWeights = wLane + wSteering + wMirror + wGap + wHazard;
            if (Mathf.Abs(sumWeights - 1f) > 0.01f)
                Debug.LogWarning($"[RuleScorer] weights sum to {sumWeights:F2}, not 1.0 " +
                                 "— the composite score is still normalised, but fix this.");

            float weightedRisk =
                (100f - laneScore)     * wLane     +
                (100f - steeringScore) * wSteering +
                (100f - mirrorScore)   * wMirror   +
                (100f - gapScore)      * wGap      +
                (100f - hazardScore)   * wHazard;

            weightedRisk /= Mathf.Max(0.01f, sumWeights);
            weightedRisk += f.CollisionCount * collisionPenalty;

            var assessment = new RiskAssessment
            {
                CompositeRisk = Mathf.Clamp(weightedRisk, 0f, 100f),
                LaneKeepingScore = laneScore,
                SteeringSmoothnessScore = steeringScore,
                MirrorAwarenessScore = mirrorScore,
                GapAcceptanceScore = gapScore,
                HazardResponseScore = hazardScore,
                ScorerUsed = ScorerName
            };

            assessment.WeakestSkill = FindWeakest(assessment, f);

            Debug.Log($"[RuleScorer] composite={assessment.CompositeRisk:F1} " +
                      $"({RiskBands.Label(assessment.Band)}) " +
                      $"weakest={assessment.WeakestSkill}");

            return assessment;
        }

        /// <summary>
        /// Lowest sub-score wins. Skills that were not measured (neutral 50) are
        /// excluded — recommending "practise gap selection" after a session with no
        /// intersection would be nonsense.
        /// </summary>
        private static SkillArea FindWeakest(RiskAssessment a, DriverFeatureSet f)
        {
            SkillArea weakest = SkillArea.LaneKeeping;
            float lowest = float.MaxValue;

            void Consider(SkillArea skill, float score, bool measured)
            {
                if (!measured || score >= lowest) return;
                lowest = score;
                weakest = skill;
            }

            Consider(SkillArea.LaneKeeping, a.LaneKeepingScore, true);
            Consider(SkillArea.SteeringSmoothness, a.SteeringSmoothnessScore, true);
            Consider(SkillArea.MirrorAwareness, a.MirrorAwarenessScore, true);
            Consider(SkillArea.GapAcceptance, a.GapAcceptanceScore, f.GapAcceptance >= 0f);
            Consider(SkillArea.HazardResponse, a.HazardResponseScore, f.ReactionTime >= 0f);

            return weakest;
        }
    }
}
