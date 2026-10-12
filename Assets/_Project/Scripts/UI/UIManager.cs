using System.Collections.Generic;
using UnityEngine;
using TMPro;
using IDS.Core;
using IDS.AI;
using IDS.XR;

namespace IDS.UI
{
    /// <summary>
    /// Switches panels on session phase. ONE place decides what is visible, which
    /// avoids the classic bug where two panels are open at once because two
    /// scripts each thought they owned visibility.
    ///
    ///     Welcome → CountrySelection → Recenter → Ready
    ///             → (HUD only, while driving)
    ///             → Analysing → Report → Complete
    ///
    /// The report and the closing panel are listed in the same switch as every
    /// other panel. Previously the driving and report phases both called
    /// ShowOnly(null) and the report panel managed its own visibility from the
    /// SessionEnded event — so two components decided when the report was up, and
    /// the Complete phase showed nothing at all.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject welcomePanel;
        [SerializeField] private GameObject countryPanel;
        [SerializeField] private GameObject recenterPanel;
        [SerializeField] private GameObject readyPanel;
        [SerializeField] private GameObject analysingPanel;
        [SerializeField] private GameObject reportPanel;
        [SerializeField] private GameObject completePanel;

        [Header("Country selection")]
        [SerializeField] private Transform countryCardContainer;
        [SerializeField] private GameObject countryCardPrefab;

        [Header("Text")]
        [SerializeField] private TMP_Text recenterInstructions;
        [SerializeField] private TMP_Text recenterStatus;
        [SerializeField] private TMP_Text readyInstructions;
        [SerializeField] private TMP_Text completeRecommendation;
        [SerializeField] private TMP_Text completeSessionCount;

        [Header("Refs")]
        [SerializeField] private CountryProfileManager countryProfiles;
        [SerializeField] private VRSeatRecenter seatRecenter;
        [SerializeField] private DriverProfileManager driverProfiles;
        [SerializeField] private PostDriveReportPanel report;

        private SessionManager _session;
        private readonly List<GameObject> _spawnedCards = new();
        private GameObject[] _allPanels;

        private void Awake()
        {
            _allPanels = new[]
            {
                welcomePanel, countryPanel, recenterPanel, readyPanel,
                analysingPanel, reportPanel, completePanel
            };

            ServiceRegistry.Register(this);
        }

        private void Start()
        {
            _session = ServiceRegistry.Resolve<SessionManager>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
            seatRecenter ??= ServiceRegistry.Resolve<VRSeatRecenter>();
            driverProfiles ??= ServiceRegistry.Resolve<DriverProfileManager>();
            report ??= ServiceRegistry.Resolve<PostDriveReportPanel>();

            if (recenterInstructions != null)
                recenterInstructions.text =
                    "Sit upright, facing forward.\n" +
                    "Hold a controller in each hand.\n" +
                    "Press <b>Recenter</b>, or <b>A / X</b> on either controller,\n" +
                    "to put yourself in the driving seat. You can press it\n" +
                    "again at any time, including while driving.";

            if (readyInstructions != null)
                readyInstructions.text =
                    "<b>Right trigger</b> — accelerate\n" +
                    "<b>Left trigger</b> — brake\n" +
                    "<b>Grip button</b> near the wheel, then turn your hand, to steer\n" +
                    "<b>Left thumbstick</b> steers if you let go of the wheel\n" +
                    "<b>A / X</b> — recentre    <b>Hold B / Y</b> — end the drive early\n\n" +
                    "Drive normally. Check your mirrors as you would at home.\n\n" +
                    BuildProcedureSummary();

            BuildCountryCards();
            ShowOnly(welcomePanel);
        }

        private void OnEnable()
        {
            SessionEvents.PhaseChanged += OnPhaseChanged;
            SessionEvents.SeatRecentred += OnSeatRecentred;
        }

        private void OnDisable()
        {
            SessionEvents.PhaseChanged -= OnPhaseChanged;
            SessionEvents.SeatRecentred -= OnSeatRecentred;
        }

        private void OnPhaseChanged(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.Welcome: ShowOnly(welcomePanel); break;
                case SessionPhase.CountrySelection: ShowOnly(countryPanel); break;
                case SessionPhase.Recenter: ShowOnly(recenterPanel); break;
                case SessionPhase.Ready: ShowOnly(readyPanel); break;

                case SessionPhase.Driving:
                case SessionPhase.HazardActive:
                    ShowOnly(null);   // HUD only — DrivingHUD owns its own visibility
                    break;

                case SessionPhase.Analysing: ShowOnly(analysingPanel); break;

                case SessionPhase.Report:
                    ShowOnly(reportPanel);
                    break;

                case SessionPhase.Complete:
                    FillCompletePanel();
                    ShowOnly(completePanel);
                    break;
            }
        }

        private void OnSeatRecentred()
        {
            if (recenterStatus == null) return;
            recenterStatus.text = "Recentred. You are in the driving seat.";
        }

        private void ShowOnly(GameObject panel)
        {
            if (_allPanels == null) return;

            foreach (GameObject p in _allPanels)
                if (p != null && p.activeSelf != (p == panel)) p.SetActive(p == panel);
        }

        /// <summary>
        /// Lists what the driver will be coached through, before they drive.
        ///
        /// Read off the scenarios actually present in the scene rather than
        /// hard-coded, so the briefing cannot drift out of step with the hazards
        /// that will really fire.
        /// </summary>
        private static string BuildProcedureSummary()
        {
            var titles = new List<string>();

            foreach (IDS.Scenarios.HazardScenarioBase h in
                     FindObjectsByType<IDS.Scenarios.HazardScenarioBase>(
                         FindObjectsInactive.Include))
            {
                if (h == null || string.IsNullOrWhiteSpace(h.ProcedureTitle)) continue;
                if (!titles.Contains(h.ProcedureTitle)) titles.Add(h.ProcedureTitle);
            }

            if (titles.Count == 0) return "";

            return "<b>YOU WILL BE COACHED THROUGH</b>\n"
                   + string.Join("   ·   ", titles) + "\n"
                   + "<size=24>Each one tells you what to do before it happens, "
                   + "and how you did straight after.</size>";
        }

        // ---- country selection -----------------------------------------------

        private void BuildCountryCards()
        {
            if (countryCardContainer == null || countryCardPrefab == null
                || countryProfiles == null)
            {
                Debug.LogError("[UI] country selection is not wired " +
                               $"(container={countryCardContainer != null}, " +
                               $"prefab={countryCardPrefab != null}, " +
                               $"profiles={countryProfiles != null}). The driver will " +
                               "not be able to choose a country.");
                return;
            }

            foreach (GameObject c in _spawnedCards) Destroy(c);
            _spawnedCards.Clear();

            int built = 0;

            foreach (CountryProfile profile in countryProfiles.Available)
            {
                if (profile == null) continue;

                GameObject card = Instantiate(countryCardPrefab, countryCardContainer);
                card.SetActive(true);
                _spawnedCards.Add(card);

                CountryCardView view = card.GetComponent<CountryCardView>();
                if (view != null) { view.Bind(profile, OnCountryChosen); built++; }
            }

            Debug.Log($"[UI] built {built} country card(s).");

            if (built == 0)
            {
                Debug.LogError("[UI] no country profiles are assigned to " +
                               "CountryProfileManager. Run Tools → Intelli-Driving → " +
                               "Create Country Profiles, then rebuild the scene.");

                ShowCardFailure($"No country profiles loaded "
                                + $"({countryProfiles.Available?.Count ?? 0} slot(s), "
                                + "all empty).\n\nRebuild the scene from "
                                + "Tools \u2192 Intelli-Driving \u2192 Build Main Scene "
                                + "and check the validation report.");
            }
        }

        /// <summary>
        /// Puts the reason on the screen the driver is looking at.
        ///
        /// A console error is no use in a headset. When the cards failed to
        /// build, the country panel rendered its title and its hint and then
        /// simply nothing, which looks exactly like a layout problem and sent
        /// the debugging in the wrong direction for an afternoon.
        /// </summary>
        private void ShowCardFailure(string message)
        {
            if (countryCardContainer == null) return;

            var go = new GameObject("CardFailureMessage", typeof(RectTransform));
            go.layer = countryCardContainer.gameObject.layer;
            go.transform.SetParent(countryCardContainer, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1000f, 320f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = message;
            text.fontSize = 34f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.55f, 0.45f, 1f);
            text.raycastTarget = false;

            _spawnedCards.Add(go);
        }

        private void OnCountryChosen(CountryProfile profile)
        {
            countryProfiles?.Select(profile);
            _session?.OnCountryConfirmed();
        }

        // ---- closing panel ----------------------------------------------------

        private void FillCompletePanel()
        {
            DriverProfile profile = driverProfiles?.Current;

            if (completeRecommendation != null)
                completeRecommendation.text = !string.IsNullOrEmpty(profile?.recommendedTraining)
                    ? profile.recommendedTraining
                    : "General adaptation drive";

            if (completeSessionCount != null)
                completeSessionCount.text = profile != null
                    ? $"Sessions completed: {profile.sessionsCompleted}"
                    : "";
        }

        // ---- button handlers --------------------------------------------------

        public void OnStartPressed() => _session?.OnWelcomeConfirmed();

        public void OnRecenterPressed()
        {
            if (seatRecenter == null)
            {
                if (recenterStatus != null)
                    recenterStatus.text = "Recentring is unavailable — check the scene setup.";
                Debug.LogError("[UI] no VRSeatRecenter, so the Recenter button does nothing.");
                return;
            }

            seatRecenter.Recentre();
        }

        public void OnRecenterConfirmPressed() => _session?.OnRecenterConfirmed();

        public void OnBeginDrivePressed() => _session?.BeginDrive();

        public void OnEndDrivePressed() => _session?.EndDrive();

        /// <summary>"Done" on the report.</summary>
        public void OnReportDonePressed()
        {
            report?.Clear();
            _session?.OnReportDismissed();
        }

        /// <summary>"Drive again" on the report — straight back into the cabin.</summary>
        public void OnDriveAgainPressed()
        {
            report?.Clear();
            _session?.RestartSession();
        }

        /// <summary>"New session" on the closing panel.</summary>
        public void OnNewSessionPressed() => _session?.ReturnToWelcome();

        public void OnExitPressed() => Application.Quit();

        // ---- wiring, called by the scene builder ------------------------------

        public void Configure(GameObject welcome, GameObject country, GameObject recenter,
                              GameObject ready, GameObject analysing, GameObject reportRoot,
                              GameObject complete, Transform cardContainer,
                              GameObject cardPrefab)
        {
            welcomePanel = welcome;
            countryPanel = country;
            recenterPanel = recenter;
            readyPanel = ready;
            analysingPanel = analysing;
            reportPanel = reportRoot;
            completePanel = complete;
            countryCardContainer = cardContainer;
            countryCardPrefab = cardPrefab;
        }

        public void ConfigureText(TMP_Text recenterInstr, TMP_Text recenterStat,
                                  TMP_Text readyInstr, TMP_Text completeRec,
                                  TMP_Text completeCount)
        {
            recenterInstructions = recenterInstr;
            recenterStatus = recenterStat;
            readyInstructions = readyInstr;
            completeRecommendation = completeRec;
            completeSessionCount = completeCount;
        }
    }
}
