using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IDS.Core;

namespace IDS.AI
{
    /// <summary>
    /// Holds the active profile and tells everyone when it changes. Ananya's
    /// environment and cabin, Anushka's scorer and Omkar's UI all react to
    /// SessionEvents.CountryProfileChanged rather than polling.
    ///
    /// OWNER: Anushka (data) / Aaditya (lifecycle).
    /// </summary>
    public class CountryProfileManager : MonoBehaviour
    {
        [SerializeField] private List<CountryProfile> availableProfiles = new();
        [SerializeField] private CountryProfile defaultProfile;

        public CountryProfile Current { get; private set; }
        public IReadOnlyList<CountryProfile> Available => availableProfiles;

        private void Awake()
        {
            ServiceRegistry.Register(this);

            if (availableProfiles.Count == 0)
                Debug.LogWarning("[CountryProfile] no profiles assigned. Run " +
                                 "Tools → Intelli-Driving → Create Country Profiles " +
                                 "and drag the assets in.");

            Current = defaultProfile ?? availableProfiles.FirstOrDefault();
        }

        private void Start()
        {
            if (Current != null) Select(Current);
        }

        public void Select(CountryProfile profile)
        {
            if (profile == null) return;
            Current = profile;
            Debug.Log($"[CountryProfile] → {profile.displayName} " +
                      $"(traffic {profile.trafficSide}, seat {profile.driverSeatSide})");
            SessionEvents.RaiseCountryProfileChanged(profile.profileId);
        }

        /// <summary>Called by Omkar's profile-selection buttons.</summary>
        public void SelectById(string profileId)
        {
            var p = availableProfiles.FirstOrDefault(
                x => x != null && x.profileId == profileId);
            if (p == null)
            {
                Debug.LogError($"[CountryProfile] unknown profile '{profileId}'");
                return;
            }
            Select(p);
        }
    }
}
