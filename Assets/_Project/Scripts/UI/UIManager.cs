using System.Collections.Generic;
using UnityEngine;
using TMPro;
using IDS.Core;
using IDS.AI;
using IDS.XR;

namespace IDS.UI
{
    /// <summary>
    /// Switches panels on session phase. One place decides what is visible, which
    /// avoids the classic bug where two panels are open at once because two
    /// scripts each thought they owned visibility.
    ///
    /// PANELS: Welcome → CountrySelection → Calibration → Ready → (HUD during
    /// driving) → Report.
    ///
    /// OWNER: Omkar.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject welcomePanel;
        [SerializeField] private GameObject countryPanel;
        [SerializeField] private GameObject calibrationPanel;
        [SerializeField] private GameObject readyPanel;
        [SerializeField] private GameObject analysingPanel;
        [SerializeField] private PostDriveReportPanel reportPanel;
        [SerializeField] private DrivingHUD hud;

        [Header("Country selection")]
        [SerializeField] private Transform countryCardContainer;
        [SerializeField] private GameObject countryCardPrefab;

        [Header("Calibration guidance")]
        [SerializeField] private TMP_Text calibrationInstructions;
        [SerializeField] private TMP_Text calibrationStatus;

        [Header("Refs")]
        [SerializeField] private CountryProfileManager countryProfiles;
        [SerializeField] private CalibrationManager calibration;

        private SessionManager _session;
        private readonly List<GameObject> _spawnedCards = new();

        private void Start()
        {
            _session = ServiceRegistry.Resolve<SessionManager>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
            calibration ??= ServiceRegistry.Resolve<CalibrationManager>();

            if (calibrationInstructions != null)
                calibrationInstructions.text =
                    "Sit comfortably.\n" +
                    "Place both hands on the wheel at 9 and 3.\n" +
                    "Look straight ahead.\n" +
                    "Press Confirm when ready.";

            BuildCountryCards();
            ShowOnly(welcomePanel);
        }

        private void OnEnable()  => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.Welcome:          ShowOnly(welcomePanel); break;
                case SessionPhase.CountrySelection: ShowOnly(countryPanel); break;
                case SessionPhase.Calibration:      ShowOnly(calibrationPanel); break;
                case SessionPhase.Ready:            ShowOnly(readyPanel); break;
                case SessionPhase.Driving:
                case SessionPhase.HazardActive:     ShowOnly(null); break;
                case SessionPhase.Analysing:        ShowOnly(analysingPanel); break;
                case SessionPhase.Report:
                case SessionPhase.Complete:         ShowOnly(null); break;
            }
        }

        private void ShowOnly(GameObject panel)
        {
            foreach (var p in new[]
                     { welcomePanel, countryPanel, calibrationPanel, readyPanel, analysingPanel })
                if (p != null) p.SetActive(p == panel);
        }

        private void BuildCountryCards()
        {
            if (countryCardContainer == null || countryCardPrefab == null
                || countryProfiles == null) return;

            foreach (var c in _spawnedCards) Destroy(c);
            _spawnedCards.Clear();

            foreach (CountryProfile profile in countryProfiles.Available)
            {
                if (profile == null) continue;

                GameObject card = Instantiate(countryCardPrefab, countryCardContainer);
                _spawnedCards.Add(card);

                var view = card.GetComponent<CountryCardView>();
                if (view != null) view.Bind(profile, OnCountryChosen);
            }
        }

        private void OnCountryChosen(CountryProfile profile)
        {
            countryProfiles?.Select(profile);
            _session?.OnCountryConfirmed();
        }

        // ---- button handlers -------------------------------------------------

        public void OnStartPressed() => _session?.OnWelcomeConfirmed();

        public void OnCalibratePressed()
        {
            bool ok = calibration != null && calibration.Calibrate();

            if (calibrationStatus != null)
                calibrationStatus.text = ok
                    ? "Calibrated. You can start driving."
                    : "Could not calibrate — hands not visible. " +
                      "You can still drive using the controller.";
        }

        public void OnCalibrationConfirmPressed() => _session?.OnCalibrationConfirmed();

        public void OnBeginDrivePressed() => _session?.BeginDrive();

        public void OnEndDrivePressed() => _session?.EndDrive();

        public void OnExitPressed() => Application.Quit();
    }
}
