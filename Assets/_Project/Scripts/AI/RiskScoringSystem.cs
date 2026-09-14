using UnityEngine;
using IDS.Core;
using IDS.Telemetry;

namespace IDS.AI
{
    /// <summary>
    /// Picks a scorer and runs the analysis at session end. Implements
    /// IAnalysisHost so SessionManager can call it without knowing which scorer
    /// is active.
    ///
    /// Selection rule: use the classifier only when it loads AND the team has
    /// explicitly enabled it. Default is the rule scorer, so a demo can never be
    /// broken by a bad model file.
    ///
    /// OWNER: Anushka.
    /// </summary>
    public class RiskScoringSystem : MonoBehaviour, IAnalysisHost
    {
        [SerializeField] private RuleBasedRiskScorer ruleScorer;
        [SerializeField] private ClassifierRiskScorer classifierScorer;
        [SerializeField] private FeatureExtractor featureExtractor;
        [SerializeField] private DriverProfileManager profileManager;
        [SerializeField] private CountryProfileManager countryProfiles;

        [Tooltip("Off by default. Turn on only after the classifier has been " +
                 "compared against the rule scorer and documented.")]
        [SerializeField] private bool preferClassifier;

        public DriverFeatureSet LastFeatures { get; private set; }
        public RiskAssessment LastAssessment { get; private set; }

        private void Awake() => ServiceRegistry.Register<IAnalysisHost>(this);

        private void Start()
        {
            ruleScorer ??= GetComponent<RuleBasedRiskScorer>() ?? FindObjectOfType<RuleBasedRiskScorer>();
            classifierScorer ??= GetComponent<ClassifierRiskScorer>();
            featureExtractor ??= ServiceRegistry.Resolve<FeatureExtractor>();
            profileManager ??= ServiceRegistry.Resolve<DriverProfileManager>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();

            if (ruleScorer == null)
                Debug.LogError("[Scoring] no RuleBasedRiskScorer. The project has no " +
                               "guaranteed intelligence layer. Add one.");
        }

        public RiskAssessment AnalyseCurrentSession()
        {
            if (featureExtractor == null)
            {
                Debug.LogError("[Scoring] no FeatureExtractor — cannot analyse.");
                return default;
            }

            DriverFeatureSet features = featureExtractor.Extract();

            float limit = countryProfiles?.Current != null
                ? countryProfiles.Current.defaultSpeedLimitKph
                : 50f;
            features.SpeedOverLimitRatio = featureExtractor.SpeedOverLimitRatio(limit);

            IRiskScorer scorer = ChooseScorer();
            RiskAssessment assessment = scorer.Score(features);

            LastFeatures = features;
            LastAssessment = assessment;

            profileManager?.UpdateFromSession(features, assessment);

            return assessment;
        }

        private IRiskScorer ChooseScorer()
        {
            if (preferClassifier && classifierScorer != null && classifierScorer.IsUsable)
                return classifierScorer;

            if (preferClassifier)
                Debug.LogWarning("[Scoring] classifier preferred but unusable — " +
                                 "falling back to the rule-based scorer.");

            return ruleScorer;
        }
    }
}
