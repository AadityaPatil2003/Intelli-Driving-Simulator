using System.Collections.Generic;
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
    /// is the INPUT to the next session, not the end of this one. So the last
    /// thing on it is not a score, it is what happens next time.
    ///
    /// Every number here comes from Anushka's scoring system. Nothing is
    /// hard-coded — if a value looks wrong, the bug is upstream, and hard-coding
    /// a plausible-looking number here would hide it.
    ///
    /// OWNER: Omkar. Data: Anushka.
    /// </summary>
    public class PostDriveReportPanel : MonoBehaviour
    {
        [Header("Root")]
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
        [SerializeField] private Color goodColour = new Color(0.3f, 0.8f, 0.5f);
        [SerializeField] private Color okColour = new Color(0.95f, 0.75f, 0.3f);
        [SerializeField] private Color poorColour = new Color(0.9f, 0.4f, 0.35f);

        [Header("Sources")]
        [SerializeField] private DriverProfileManager profileManager;
        [SerializeField] private ScenarioManager scenarioManager;

        private void Start()
        {
            profileManager ??= ServiceRegistry.Resolve<DriverProfileManager>();
            scenarioManager ??= ServiceRegistry.Resolve<ScenarioManager>();
            SetVisible(false);
        }

        private void OnEnable()  => SessionEvents.SessionEnded += OnSessionEnded;
        private void OnDisable() => SessionEvents.SessionEnded -= OnSessionEnded;

        private void OnSessionEnded(RiskAssessment a) => Display(a);

        public void Display(RiskAssessment a)
        {
            SetVisible(true);

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

            var profile = profileManager?.Current;

            if (recommendationText != null)
                recommendationText.text = !string.IsNullOrEmpty(profile?.recommendedTraining)
                    ? profile.recommendedTraining
                    : "General adaptation drive";

            if (trendText != null) trendText.text = BuildTrendLine(profile);
            if (scenarioSummaryText != null) scenarioSummaryText.text = BuildScenarioSummary();
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
                return "No hazards occurred in this session.";

            var sb = new StringBuilder();
            foreach (ScenarioResult r in results)
            {
                string label = r.ScenarioId.Replace('_', ' ');
                string outcome = r.Collision
                    ? "collision"
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
            return sb.ToString().TrimEnd();
        }

        private void SetSkill(Image bar, TMP_Text label, float score0to100)
        {
            SetBar(bar, score0to100 / 100f, invertColour: false);
            if (label != null) label.text = $"{Mathf.RoundToInt(score0to100)} / 100";
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

        public void SetVisible(bool visible)
        {
            if (panelRoot != null) panelRoot.SetActive(visible);
        }

        /// <summary>Wired to the "Done" button.</summary>
        public void OnDismiss()
        {
            SetVisible(false);
            ServiceRegistry.Resolve<SessionManager>()?.OnReportDismissed();
        }

        /// <summary>Wired to the "Drive again" button — the loop closing.</summary>
        public void OnDriveAgain()
        {
            SetVisible(false);
            ServiceRegistry.Resolve<SessionManager>()?.RestartSession();
        }
    }
}
