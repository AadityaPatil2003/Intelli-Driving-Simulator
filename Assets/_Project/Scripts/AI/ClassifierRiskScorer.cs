using UnityEngine;
using IDS.Core;

namespace IDS.AI
{
    /// <summary>
    /// EXPERIMENTAL. Wraps the offline-trained logistic model. Reports IsUsable
    /// false when no model file is present, which means the scoring system falls
    /// straight through to the rule-based scorer and the project still works.
    ///
    /// HONESTY NOTE for the final report: with 4-6 participants plus the team's
    /// own sessions, this cannot be presented as a validated driving-risk
    /// classifier. It is a proof of concept trained on labelled session windows.
    /// Say that in the report; do not let the demo imply otherwise.
    ///
    /// OWNER: Anushka.
    /// </summary>
    public class ClassifierRiskScorer : MonoBehaviour, IRiskScorer
    {
        [Tooltip("Name of the TextAsset in a Resources folder, without .json")]
        [SerializeField] private string resourceName = "driver_model";
        [Tooltip("Also look in persistentDataPath, so a model can be pushed to " +
                 "the headset with adb without rebuilding the APK.")]
        [SerializeField] private bool allowPersistentPathOverride = true;

        [SerializeField] private RuleBasedRiskScorer ruleScorerForSubScores;

        public string ScorerName => _model != null
            ? $"classifier_{_model.modelVersion}"
            : "classifier_unavailable";

        public bool IsUsable => _model != null && _model.IsValid;

        private LogisticModel _model;

        private void Start()
        {
            if (allowPersistentPathOverride)
                _model = LogisticModel.LoadFromPersistentPath();

            _model ??= LogisticModel.LoadFromResources(resourceName);
            ruleScorerForSubScores ??= ServiceRegistry.Resolve<IRiskScorer>() as RuleBasedRiskScorer;
        }

        public RiskAssessment Score(DriverFeatureSet f)
        {
            // Sub-scores and the weakest-skill call still come from the rule
            // scorer. The classifier only produces a composite probability — it
            // has no notion of which individual skill was weak, and pretending
            // otherwise would put a made-up number in the driver's report.
            RiskAssessment baseline = ruleScorerForSubScores != null
                ? ruleScorerForSubScores.Score(f)
                : default;

            if (!IsUsable) return baseline;

            float[] vector =
            {
                f.MirrorCheckFreq,
                f.LaneOffsetRms,
                f.SteeringJerk,
                f.GapAcceptance < 0f ? 0f : f.GapAcceptance,
                f.ReactionTime < 0f ? 0f : f.ReactionTime
            };

            float probability = _model.Predict(vector);
            if (probability < 0f) return baseline;   // predict failed, fall back

            baseline.CompositeRisk = Mathf.Clamp(probability * 100f, 0f, 100f);
            baseline.ScorerUsed = ScorerName;

            Debug.Log($"[Classifier] p(high_risk)={probability:F3} → " +
                      $"composite {baseline.CompositeRisk:F1} " +
                      $"(rule scorer said {ruleScorerForSubScores?.Score(f).CompositeRisk:F1})");

            return baseline;
        }
    }
}
