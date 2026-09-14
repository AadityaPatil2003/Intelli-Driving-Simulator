using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using IDS.AI;

namespace IDS.UI
{
    /// <summary>
    /// One country-selection card. Shows the two facts that actually change how
    /// the driver has to behave — which side traffic keeps and which side the
    /// driver sits — plus the notable rule differences.
    ///
    /// OWNER: Omkar. Data: Anushka's CountryProfile.
    /// </summary>
    public class CountryCardView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text trafficSideText;
        [SerializeField] private TMP_Text cabinSideText;
        [SerializeField] private TMP_Text differencesText;
        [SerializeField] private Button selectButton;

        private CountryProfile _profile;
        private Action<CountryProfile> _onSelected;

        public void Bind(CountryProfile profile, Action<CountryProfile> onSelected)
        {
            _profile = profile;
            _onSelected = onSelected;

            if (nameText != null) nameText.text = profile.displayName;
            if (trafficSideText != null)
                trafficSideText.text = $"Traffic keeps {profile.trafficSide.ToString().ToLower()}";
            if (cabinSideText != null)
                cabinSideText.text = $"Driver sits {profile.driverSeatSide.ToString().ToLower()}";
            if (differencesText != null)
                differencesText.text = profile.keyDifferencesBlurb;

            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => _onSelected?.Invoke(_profile));
            }
        }
    }
}
