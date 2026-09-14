using System;
using System.Collections.Generic;
using IDS.Core;

namespace IDS.AI
{
    /// <summary>
    /// The persistent record of a driver. This is the object that makes the
    /// project a trainer rather than a test: the report is written here, and the
    /// next session's hazard is chosen from it.
    ///
    /// Serialised to JSON in persistentDataPath/Profiles/.
    ///
    /// OWNER: Anushka.
    /// </summary>
    [Serializable]
    public class DriverProfile
    {
        public string driverId = "P00";
        public string countryProfileId = "";
        public int sessionsCompleted;

        // Latest session, 0..100, higher is better.
        public float laneKeepingScore;
        public float steeringSmoothnessScore;
        public float mirrorAwarenessScore;
        public float gapAcceptanceScore;
        public float hazardResponseScore;

        public float compositeRisk;
        public string weakestSkill = "";
        public string recommendedTraining = "";
        public string lastScorerUsed = "";

        // Rolling history, so the report can say "improving" rather than only
        // giving a snapshot. Capped so the file cannot grow without bound.
        public List<float> compositeRiskHistory = new();
        public const int MaxHistory = 20;

        public void Apply(DriverFeatureSet f, RiskAssessment a, string countryId)
        {
            laneKeepingScore = a.LaneKeepingScore;
            steeringSmoothnessScore = a.SteeringSmoothnessScore;
            mirrorAwarenessScore = a.MirrorAwarenessScore;
            gapAcceptanceScore = a.GapAcceptanceScore;
            hazardResponseScore = a.HazardResponseScore;

            compositeRisk = a.CompositeRisk;
            weakestSkill = a.WeakestSkill.ToString();
            lastScorerUsed = a.ScorerUsed;
            countryProfileId = countryId;

            sessionsCompleted++;

            compositeRiskHistory.Add(a.CompositeRisk);
            while (compositeRiskHistory.Count > MaxHistory)
                compositeRiskHistory.RemoveAt(0);
        }

        public SkillArea WeakestSkillEnum =>
            Enum.TryParse(weakestSkill, out SkillArea s) ? s : SkillArea.LaneKeeping;

        /// <summary>
        /// Change in composite risk versus the previous session. Negative is an
        /// improvement (risk went down). Returns 0 with fewer than 2 sessions.
        /// </summary>
        public float RiskTrend()
        {
            int n = compositeRiskHistory.Count;
            if (n < 2) return 0f;
            return compositeRiskHistory[n - 1] - compositeRiskHistory[n - 2];
        }

        public float ScoreFor(SkillArea skill) => skill switch
        {
            SkillArea.LaneKeeping        => laneKeepingScore,
            SkillArea.SteeringSmoothness => steeringSmoothnessScore,
            SkillArea.MirrorAwareness    => mirrorAwarenessScore,
            SkillArea.GapAcceptance      => gapAcceptanceScore,
            _                            => hazardResponseScore
        };
    }
}
