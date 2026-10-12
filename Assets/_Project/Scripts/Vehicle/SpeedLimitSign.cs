using UnityEngine;
using TMPro;
using IDS.Core;
using IDS.AI;

namespace IDS.Vehicle
{
    /// <summary>
    /// Puts the current country's speed limit on a roadside sign.
    ///
    /// The limit is part of the country profile, so a hard-coded "50" would be
    /// wrong the moment the driver picks a different country — and the scorer
    /// marks speed compliance against the profile, so the sign and the scoring
    /// would disagree. That is the one thing a driving simulator must never do.
    /// </summary>
    public class SpeedLimitSign : MonoBehaviour
    {
        [SerializeField] private TextMeshPro label;

        private void OnEnable() => SessionEvents.CountryProfileChanged += OnProfileChanged;
        private void OnDisable() => SessionEvents.CountryProfileChanged -= OnProfileChanged;

        private void Start() => Refresh();

        private void OnProfileChanged(string profileId) => Refresh();

        private void Refresh()
        {
            if (label == null) return;

            CountryProfile profile =
                ServiceRegistry.Resolve<CountryProfileManager>()?.Current;

            label.text = profile != null
                ? Mathf.RoundToInt(profile.defaultSpeedLimitKph).ToString()
                : "50";
        }

        public void Configure(TextMeshPro text) => label = text;
    }
}
