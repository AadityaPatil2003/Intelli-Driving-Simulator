using System;
using UnityEngine;

namespace IDS.Core
{
    /// <summary>
    /// Plain data types passed between streams. No behaviour, no dependencies.
    /// If you need a new field here, say so at stand-up — four people compile
    /// against this file.
    /// </summary>

    public enum TrafficSide { Left, Right }

    public enum SkillArea
    {
        LaneKeeping,
        SteeringSmoothness,
        MirrorAwareness,
        GapAcceptance,
        HazardResponse
    }

    /// <summary>Session-level features produced by Anushka's FeatureExtractor.</summary>
    [Serializable]
    public struct DriverFeatureSet
    {
        // The four core channels committed to in the pitch.
        public float MirrorCheckFreq;   // checks per minute
        public float LaneOffsetRms;     // metres
        public float SteeringJerk;      // normalised, unitless
        public float GapAcceptance;     // seconds (smaller = more aggressive)

        // Supporting channels.
        public float MeanSpeedKph;
        public float SpeedOverLimitRatio; // 0 = never over, 1 = always over
        public float ReactionTime;        // seconds, mean across hazards
        public int   CollisionCount;
        public float SessionDurationSec;

        public override string ToString() =>
            $"mirror={MirrorCheckFreq:F2}/min lane_rms={LaneOffsetRms:F2}m " +
            $"jerk={SteeringJerk:F2} gap={GapAcceptance:F2}s " +
            $"react={ReactionTime:F2}s collisions={CollisionCount}";
    }

    /// <summary>Output of any IRiskScorer.</summary>
    [Serializable]
    public struct RiskAssessment
    {
        public float CompositeRisk;            // 0 (low) .. 100 (very high)
        public float LaneKeepingScore;         // 0 .. 100, HIGHER IS BETTER
        public float SteeringSmoothnessScore;  // 0 .. 100, higher is better
        public float MirrorAwarenessScore;     // 0 .. 100, higher is better
        public float GapAcceptanceScore;       // 0 .. 100, higher is better
        public float HazardResponseScore;      // 0 .. 100, higher is better
        public SkillArea WeakestSkill;
        public string ScorerUsed;

        public RiskBand Band => RiskBands.Of(CompositeRisk);
    }

    public enum RiskBand { Low, Moderate, High, VeryHigh }

    public static class RiskBands
    {
        // Project feedback categories, NOT road-safety certification.
        public static RiskBand Of(float composite)
        {
            if (composite < 30f) return RiskBand.Low;
            if (composite < 60f) return RiskBand.Moderate;
            if (composite < 80f) return RiskBand.High;
            return RiskBand.VeryHigh;
        }

        public static string Label(RiskBand b) => b switch
        {
            RiskBand.Low      => "Low",
            RiskBand.Moderate => "Moderate",
            RiskBand.High     => "High",
            _                 => "Very High"
        };
    }

    /// <summary>Outcome of a single hazard scenario.</summary>
    [Serializable]
    public struct ScenarioResult
    {
        public string ScenarioId;
        public float  TriggerTime;          // seconds since session start
        public float  VehicleSpeedAtTrigger;
        public float  FirstBrakeTime;       // -1 if the driver never braked
        public float  ReactionTime;         // -1 if no response
        public float  MinimumSpeed;
        public float  MinimumDistance;      // to the hazard, metres
        public bool   MirrorCheckPerformed;
        public bool   Collision;
        public bool   SuccessfulAvoidance;
        public bool   Completed;

        public static ScenarioResult Empty(string id) => new ScenarioResult
        {
            ScenarioId = id,
            FirstBrakeTime = -1f,
            ReactionTime = -1f,
            MinimumSpeed = float.MaxValue,
            MinimumDistance = float.MaxValue
        };
    }

    public static class SkillAreaNames
    {
        public static string Friendly(SkillArea s) => s switch
        {
            SkillArea.LaneKeeping         => "Lane Keeping",
            SkillArea.SteeringSmoothness  => "Steering Smoothness",
            SkillArea.MirrorAwareness     => "Mirror Awareness",
            SkillArea.GapAcceptance       => "Gap Selection",
            _                             => "Hazard Response"
        };
    }
}
