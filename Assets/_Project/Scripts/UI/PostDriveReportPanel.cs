using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using IDS.Core;
using IDS.AI;
using IDS.Scenarios;

namespace IDS.UI
{
    /// <summary>
    /// The post-drive report. The design decision this panel embodies: the report
    /// is the INPUT to the next session, not the end of this one, so the last
    /// thing on it is not a score — it is what happens next time.
    ///
    /// Every number comes from the scoring system. Nothing is hard-coded. If a
    /// value looks wrong the bug is upstream, and putting a plausible-looking
    /// number here would hide it.
    ///
    /// THIS COMPONENT MUST LIVE ON AN ALWAYS-ACTIVE OBJECT, with panelRoot
    /// pointing at the panel it fills. OnEnable never runs on a component inside
    /// a deactivated hierarchy, so a report panel that starts switched off and
    /// holds its own subscription never receives SessionEnded and shows last
    /// session's numbers, or none at all. The scene builder puts this on the UI
    /// root for that reason.
    ///
    /// It does not control its own visibility either — UIManager owns that, keyed
    /// on session phase. Two components deciding when the report is up is how it
    /// ended up both blank and visible at once.
    /// </summary>
    public class PostDriveReportPanel : MonoBehaviour
    {
        [Header("Root (filled, not shown — UIManager shows it)")]
        [SerializeField] private GameObject panelRoot;

        [Header("Headline")]
        [SerializeField] private TMP_Text compositeRiskText;
        [SerializeField] private TMP_Text riskBandText;
        [SerializeField] private Image compositeRiskBar;

        [Header("Skill bars")]
        [SerializeField] private Image laneKeepingBar;
        [SerializeField] private Image steeringBar;
        [SerializeField] private Image mirrorBar;
        [SerializeField] private Image gapBar;
        [SerializeField] private Image hazardBar;

        [SerializeField] private TMP_Text laneKeepingValue;
        [SerializeField] private TMP_Text steeringValue;
        [SerializeField] private TMP_Text mirrorValue;
        [SerializeField] private TMP_Text gapValue;
        [SerializeField] private TMP_Text hazardValue;

        [Header("Narrative")]
        [SerializeField] private TMP_Text weakestSkillText;
        [SerializeField] private TMP_Text recommendationText;
        [SerializeField] private TMP_Text trendText;
        [SerializeField] private TMP_Text scenarioSummaryText;

        [Header("Bar colours")]
        [SerializeField] private Color goodColour = new Color(0.30f, 0.80f, 0.50f);
        [SerializeField] private Color okColour = new Color(0.95f, 0.75f, 0.30f);
        [SerializeField] private Color poorColour = new Color(0.90f, 0.40f, 0.35f);

        [Header("Sources")]
        [SerializeField] private DriverProfileManager profileManager;
        [SerializeField] private ScenarioManager scenarioManager;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            profileManager ??= ServiceRegistry.Resolve<DriverProfileManager>();
            scenarioManager ??= ServiceRegistry.Resolve<ScenarioManager>();
        }

        private void OnEnable() => SessionEvents.SessionEnded += OnSessionEnded;
        private void OnDisable() => SessionEvents.SessionEnded -= OnSessionEnded;

        private void OnSessionEnded(RiskAssessment a) => Display(a);

        /// <summary>
        /// Fills the panel. Does not show it — the phase change that follows
        /// SessionEnded is what makes it visible.
        /// </summary>
        public void Display(RiskAssessment a)
        {
            if (compositeRiskText != null)
                compositeRiskText.text = $"{Mathf.RoundToInt(a.CompositeRisk)} / 100";

            if (riskBandText != null)
                riskBandText.text = RiskBands.Label(a.Band);

            // Composite is RISK, so the bar fills as things get worse and the
            // colour scale is inverted relative to the skill bars.
            SetBar(compositeRiskBar, a.CompositeRisk / 100f, invertColour: true);

            SetSkill(laneKeepingBar, laneKeepingValue, a.LaneKeepingScore);
            SetSkill(steeringBar, steeringValue, a.SteeringSmoothnessScore);
            SetSkill(mirrorBar, mirrorValue, a.MirrorAwarenessScore);
            SetSkill(gapBar, gapValue, a.GapAcceptanceScore);
            SetSkill(hazardBar, hazardValue, a.HazardResponseScore);

            if (weakestSkillText != null)
                weakestSkillText.text = SkillAreaNames.Friendly(a.WeakestSkill);

            DriverProfile profile = profileManager?.Current;

            if (recommendationText != null)
                recommendationText.text = !string.IsNullOrEmpty(profile?.recommendedTraining)
                    ? profile.recommendedTraining
                    : "General adaptation drive";

            if (trendText != null) trendText.text = BuildTrendLine(profile);
            if (scenarioSummaryText != null) scenarioSummaryText.text = BuildScenarioSummary();

            Debug.Log($"[Report] composite={a.CompositeRisk:F1} ({RiskBands.Label(a.Band)}) " +
                      $"weakest={a.WeakestSkill} scorer={a.ScorerUsed}");
        }

        /// <summary>
        /// Blanks the panel when the driver leaves it, so a later drive that fails
        /// to produce a score cannot show the previous drive's numbers as if they
        /// were new.
        /// </summary>
        public void Clear()
        {
            if (compositeRiskText != null) compositeRiskText.text = "—";
            if (riskBandText != null) riskBandText.text = "";
            if (weakestSkillText != null) weakestSkillText.text = "";
            if (recommendationText != null) recommendationText.text = "";
            if (trendText != null) trendText.text = "";
            if (scenarioSummaryText != null) scenarioSummaryText.text = "";

            SetBar(compositeRiskBar, 0f, true);
            SetSkill(laneKeepingBar, laneKeepingValue, 0f, blank: true);
            SetSkill(steeringBar, steeringValue, 0f, blank: true);
            SetSkill(mirrorBar, mirrorValue, 0f, blank: true);
            SetSkill(gapBar, gapValue, 0f, blank: true);
            SetSkill(hazardBar, hazardValue, 0f, blank: true);
        }

        private string BuildTrendLine(DriverProfile profile)
        {
            if (profile == null || profile.sessionsCompleted < 2)
                return "First recorded session — no comparison yet.";

            float delta = profile.RiskTrend();

            if (Mathf.Abs(delta) < 2f)
                return $"About the same as last session (session {profile.sessionsCompleted}).";

            return delta < 0f
                ? $"Improved by {Mathf.Abs(delta):F0} points since last session."
                : $"Up {delta:F0} points since last session.";
        }

        private string BuildScenarioSummary()
        {
            var results = scenarioManager?.SessionResults;

            if (results == null || results.Count == 0)
                return "No hazards occurred in this session, so hazard response " +
                       "was not measured.";

            var sb = new StringBuilder();

            int shown = 0;

            foreach (ScenarioResult r in results)
            {
                // Abandoned hazards never happened. Listing one as "close, no
                // braking response" blames the driver for a hazard the simulator
                // failed to stage.
                if (!r.Completed) continue;

                shown++;
                string label = r.ScenarioId.Replace('_', ' ');

                string outcome = r.Collision ? "collision"
                    : r.SuccessfulAvoidance ? "avoided" : "close";

                string react = r.ReactionTime >= 0f
                    ? $"{r.ReactionTime:F2}s reaction"
                    : "no braking response";

                sb.AppendLine($"• {label}: {outcome}, {react}");

                if (r.ScenarioId == "cyclist_blind_spot")
                    sb.AppendLine(r.MirrorCheckPerformed
                        ? "   mirror checked before moving over"
                        : "   moved over WITHOUT a mirror check");
            }

            if (shown == 0)
                return "No hazards occurred in this session, so hazard response " +
                       "was not measured.";

            return sb.ToString().TrimEnd();
        }

        private void SetSkill(Image bar, TMP_Text label, float score0to100, bool blank = false)
        {
            SetBar(bar, blank ? 0f : score0to100 / 100f, invertColour: false);
            if (label != null)
                label.text = blank ? "—" : $"{Mathf.RoundToInt(score0to100)} / 100";
        }

        private void SetBar(Image bar, float fill01, bool invertColour)
        {
            if (bar == null) return;

            fill01 = Mathf.Clamp01(fill01);
            bar.fillAmount = fill01;

            float quality = invertColour ? 1f - fill01 : fill01;
            bar.color = quality > 0.7f ? goodColour
                      : quality > 0.4f ? okColour
                      : poorColour;
        }

        /// <summary>The panel this component fills. UIManager shows and hides it.</summary>
        public GameObject PanelRoot => panelRoot;

        public void ConfigureRoot(GameObject root) => panelRoot = root;

        public void ConfigureHeadline(TMP_Text composite, TMP_Text band, Image bar)
        {
            compositeRiskText = composite;
            riskBandText = band;
            compositeRiskBar = bar;
        }

        public void ConfigureSkill(SkillArea skill, Image bar, TMP_Text value)
        {
            switch (skill)
            {
                case SkillArea.LaneKeeping: laneKeepingBar = bar; laneKeepingValue = value; break;
                case SkillArea.SteeringSmoothness: steeringBar = bar; steeringValue = value; break;
                case SkillArea.MirrorAwareness: mirrorBar = bar; mirrorValue = value; break;
                case SkillArea.GapAcceptance: gapBar = bar; gapValue = value; break;
                case SkillArea.HazardResponse: hazardBar = bar; hazardValue = value; break;
            }
        }

        public void ConfigureNarrative(TMP_Text weakest, TMP_Text recommendation,
                                       TMP_Text trend, TMP_Text scenarios)
        {
            weakestSkillText = weakest;
            recommendationText = recommendation;
            trendText = trend;
            scenarioSummaryText = scenarios;
        }
    }
}
