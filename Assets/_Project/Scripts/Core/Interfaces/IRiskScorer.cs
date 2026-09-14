namespace IDS.Core
{
    /// <summary>
    /// Turns extracted features into a 0-100 composite risk score plus per-skill
    /// sub-scores. Two implementations: RuleBasedRiskScorer (always present,
    /// deterministic) and ClassifierRiskScorer (experimental, may be absent).
    ///
    /// OWNER: Anushka.
    /// </summary>
    public interface IRiskScorer
    {
        string ScorerName { get; }

        /// True if this scorer has everything it needs (e.g. a trained model file).
        /// The scoring system falls back to the rule-based scorer when false.
        bool IsUsable { get; }

        RiskAssessment Score(DriverFeatureSet features);
    }
}
