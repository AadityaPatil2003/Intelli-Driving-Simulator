using System;
using System.IO;
using UnityEngine;

namespace IDS.AI
{
    /// <summary>
    /// A logistic regression evaluated in C#. Trained offline in Python
    /// (Tools/train_classifier.py) and exported as JSON: means, scales,
    /// coefficients, intercept. Inference is four multiplies and a sigmoid.
    ///
    /// This avoids shipping an ML runtime (Barracuda/Sentis, ONNX) to a Quest
    /// build for a five-feature model. That is not laziness — it removes an entire
    /// class of deployment risk for zero loss of capability at this model size.
    ///
    /// OWNER: Anushka.
    /// </summary>
    [Serializable]
    public class LogisticModel
    {
        public string modelVersion = "";
        public string[] featureNames = Array.Empty<string>();
        public float[] means = Array.Empty<float>();
        public float[] scales = Array.Empty<float>();
        public float[] coefficients = Array.Empty<float>();
        public float intercept;
        public float trainingAccuracy;
        public int trainingSamples;

        public bool IsValid =>
            coefficients.Length > 0 &&
            coefficients.Length == means.Length &&
            coefficients.Length == scales.Length;

        /// <summary>Probability of the positive class (HIGH_RISK).</summary>
        public float Predict(float[] rawFeatures)
        {
            if (!IsValid || rawFeatures.Length != coefficients.Length)
            {
                Debug.LogError($"[LogisticModel] feature length mismatch: got " +
                               $"{rawFeatures.Length}, model expects {coefficients.Length}");
                return -1f;
            }

            float z = intercept;
            for (int i = 0; i < coefficients.Length; i++)
            {
                float scale = Mathf.Approximately(scales[i], 0f) ? 1f : scales[i];
                float standardised = (rawFeatures[i] - means[i]) / scale;
                z += coefficients[i] * standardised;
            }

            return 1f / (1f + Mathf.Exp(-z));
        }

        public static LogisticModel LoadFromResources(string resourceName = "driver_model")
        {
            var asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null)
            {
                Debug.Log($"[LogisticModel] no model at Resources/{resourceName}.json — " +
                          "classifier unavailable, rule-based scorer will be used.");
                return null;
            }
            return Parse(asset.text);
        }

        public static LogisticModel LoadFromPersistentPath(string fileName = "driver_model.json")
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            if (!File.Exists(path)) return null;
            try { return Parse(File.ReadAllText(path)); }
            catch (Exception e)
            {
                Debug.LogError($"[LogisticModel] read failed: {e.Message}");
                return null;
            }
        }

        private static LogisticModel Parse(string json)
        {
            try
            {
                var m = JsonUtility.FromJson<LogisticModel>(json);
                if (m == null || !m.IsValid)
                {
                    Debug.LogError("[LogisticModel] JSON parsed but the model is invalid.");
                    return null;
                }
                Debug.Log($"[LogisticModel] loaded {m.modelVersion}: " +
                          $"{m.coefficients.Length} features, " +
                          $"training accuracy {m.trainingAccuracy:P1} " +
                          $"on {m.trainingSamples} samples");
                return m;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LogisticModel] parse failed: {e.Message}");
                return null;
            }
        }
    }
}
