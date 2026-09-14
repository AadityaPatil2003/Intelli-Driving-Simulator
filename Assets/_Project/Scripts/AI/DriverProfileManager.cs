using System.IO;
using UnityEngine;
using IDS.Core;

namespace IDS.AI
{
    /// <summary>
    /// Loads, updates and saves the driver profile. Also produces the
    /// recommendation string that the post-drive report shows and the adaptive
    /// selector acts on.
    ///
    /// PRIVACY: profiles are keyed by an anonymous participant id (P01, P02...).
    /// Never store a participant's name. Files are excluded from git.
    ///
    /// OWNER: Anushka.
    /// </summary>
    public class DriverProfileManager : MonoBehaviour
    {
        [SerializeField] private string driverId = "P00";
        [SerializeField] private CountryProfileManager countryProfiles;
        [SerializeField] private AdaptiveScenarioSelector selector;

        public DriverProfile Current { get; private set; }

        private string FilePath => Path.Combine(
            Application.persistentDataPath, "Profiles", $"{driverId}.json");

        private void Awake()
        {
            ServiceRegistry.Register(this);
            Current = Load() ?? new DriverProfile { driverId = driverId };
        }

        private void Start()
        {
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
            selector ??= ServiceRegistry.Resolve<AdaptiveScenarioSelector>();
        }

        public void SetDriverId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            driverId = id.Trim();
            Current = Load() ?? new DriverProfile { driverId = driverId };
            Debug.Log($"[DriverProfile] active driver → {driverId} " +
                      $"({Current.sessionsCompleted} prior sessions)");
        }

        public void UpdateFromSession(DriverFeatureSet f, RiskAssessment a)
        {
            string countryId = countryProfiles?.Current?.profileId ?? "unknown";
            Current.Apply(f, a, countryId);

            Current.recommendedTraining = selector != null
                ? selector.DescribeRecommendation(Current)
                : SkillAreaNames.Friendly(a.WeakestSkill) + " practice";

            Save();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
                Debug.Log($"[DriverProfile] saved → {FilePath}");
            }
            catch (IOException e)
            {
                Debug.LogError($"[DriverProfile] save failed: {e.Message}");
            }
        }

        private DriverProfile Load()
        {
            if (!File.Exists(FilePath)) return null;
            try
            {
                var p = JsonUtility.FromJson<DriverProfile>(File.ReadAllText(FilePath));
                p.compositeRiskHistory ??= new System.Collections.Generic.List<float>();
                return p;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DriverProfile] load failed, starting fresh: {e.Message}");
                return null;
            }
        }
    }
}
