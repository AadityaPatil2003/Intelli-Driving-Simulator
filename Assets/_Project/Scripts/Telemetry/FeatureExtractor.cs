using System.Collections.Generic;
using UnityEngine;
using IDS.Core;

namespace IDS.Telemetry
{
    /// <summary>
    /// Turns a session's samples into the four core features plus supporting ones.
    /// Every formula here has to be documented in the final report, so each one is
    /// written out explicitly rather than hidden in a helper.
    ///
    ///   lane_offset_rms   = sqrt(mean(laneOffset^2))                     metres
    ///   steering_jerk     = rms of d²(steering)/dt², normalised           unitless
    ///   mirror_check_freq = checks / minutes driven                       per minute
    ///   gap_acceptance    = minimum accepted gap at the conflict point    seconds
    ///
    /// Why RMS and not mean-absolute for lane offset: RMS penalises a few large
    /// excursions more than constant small wobble, and a driver who drifts a full
    /// half-lane twice is the case we care about.
    ///
    /// OWNER: Anushka.
    /// </summary>
    public class FeatureExtractor : MonoBehaviour
    {
        [SerializeField] private TelemetryRecorder recorder;
        [SerializeField] private MirrorCheckDetector mirrorDetector;
        [SerializeField] private GapAcceptanceDetector gapDetector;

        [Header("Normalisation")]
        [Tooltip("Steering jerk value treated as the practical maximum, used to " +
                 "normalise into 0..1. Tune after internal pilot sessions.")]
        [SerializeField] private float steeringJerkReference = 40f;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            recorder ??= ServiceRegistry.Resolve<TelemetryRecorder>();
            mirrorDetector ??= FindFirstObjectByType<MirrorCheckDetector>(FindObjectsInactive.Exclude);
            gapDetector ??= FindFirstObjectByType<GapAcceptanceDetector>(FindObjectsInactive.Exclude);
        }

        public DriverFeatureSet Extract()
        {
            var features = new DriverFeatureSet();

            if (recorder == null || recorder.Samples.Count < 2)
            {
                Debug.LogWarning("[Features] fewer than 2 samples — returning empty " +
                                 "feature set. Did the session actually record?");
                return features;
            }

            List<TelemetrySample> s = recorder.Samples;
            float duration = Mathf.Max(0.001f, s[^1].t - s[0].t);
            features.SessionDurationSec = duration;

            features.LaneOffsetRms = LaneOffsetRms(s);
            features.SteeringJerk  = SteeringJerk(s);
            features.MeanSpeedKph  = MeanSpeed(s);
            features.CollisionCount = CountCollisions(s);

            features.MirrorCheckFreq = mirrorDetector != null
                ? mirrorDetector.TotalChecks / (duration / 60f)
                : 0f;

            // -1 signals "not measured". The scorer must handle that, not assume 0.
            features.GapAcceptance = gapDetector != null && gapDetector.HasMeasurement
                ? gapDetector.MinimumAcceptedGap()
                : -1f;

            features.ReactionTime = MeanReactionTime();
            features.SpeedOverLimitRatio = 0f; // set by the scorer, needs the profile

            Debug.Log($"[Features] {features}");
            return features;
        }

        // ---- individual formulas --------------------------------------------

        private static float LaneOffsetRms(List<TelemetrySample> s)
        {
            double sumSq = 0.0;
            foreach (var x in s) sumSq += (double)x.laneOffset * x.laneOffset;
            return Mathf.Sqrt((float)(sumSq / s.Count));
        }

        private float SteeringJerk(List<TelemetrySample> s)
        {
            // First derivative: steering rate. Second: rate of change of rate.
            // RMS of the second derivative, then normalised against a reference.
            if (s.Count < 3) return 0f;

            var rates = new float[s.Count - 1];
            for (int i = 1; i < s.Count; i++)
            {
                float dt = Mathf.Max(0.001f, s[i].t - s[i - 1].t);
                rates[i - 1] = (s[i].steeringInput - s[i - 1].steeringInput) / dt;
            }

            double sumSq = 0.0;
            int n = 0;
            for (int i = 1; i < rates.Length; i++)
            {
                float dt = Mathf.Max(0.001f, s[i + 1].t - s[i].t);
                float jerk = (rates[i] - rates[i - 1]) / dt;
                sumSq += (double)jerk * jerk;
                n++;
            }

            if (n == 0) return 0f;
            float rms = Mathf.Sqrt((float)(sumSq / n));
            return Mathf.Clamp01(rms / Mathf.Max(0.001f, steeringJerkReference));
        }

        private static float MeanSpeed(List<TelemetrySample> s)
        {
            double sum = 0.0;
            foreach (var x in s) sum += Mathf.Abs(x.speedKph);
            return (float)(sum / s.Count);
        }

        private static int CountCollisions(List<TelemetrySample> s)
        {
            int count = 0;
            bool wasColliding = false;
            foreach (var x in s)
            {
                if (x.collision && !wasColliding) count++;
                wasColliding = x.collision;
            }
            return count;
        }

        private float MeanReactionTime()
        {
            if (recorder == null) return -1f;

            float sum = 0f;
            int n = 0;
            foreach (var e in recorder.Events)
            {
                if (e.name != "hazard_resolved") continue;
                // detail format: "<id>|react=<value>|collision=<bool>"
                foreach (string part in e.detail.Split('|'))
                {
                    if (!part.StartsWith("react=")) continue;
                    if (float.TryParse(part.Substring(6), out float r) && r >= 0f)
                    { sum += r; n++; }
                }
            }
            return n == 0 ? -1f : sum / n;
        }

        /// <summary>
        /// Fraction of samples above the posted limit. Needs the country profile,
        /// so it is computed separately and folded in by the scoring system.
        /// </summary>
        public float SpeedOverLimitRatio(float limitKph)
        {
            if (recorder == null || recorder.Samples.Count == 0) return 0f;
            int over = 0;
            foreach (var x in recorder.Samples)
                if (x.speedKph > limitKph) over++;
            return (float)over / recorder.Samples.Count;
        }
    }
}
