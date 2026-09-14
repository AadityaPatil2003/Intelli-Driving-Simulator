using UnityEngine;
using TMPro;
using IDS.Core;
using IDS.AI;

namespace IDS.UI
{
    /// <summary>
    /// The in-drive display. Carries almost nothing on purpose: speed, the mirror
    /// the system wants checked, and one alert slot. A dashboard showing every
    /// telemetry channel is useless in motion — the detail belongs in the
    /// post-drive report, where the driver is stationary and can read.
    ///
    /// PLACEMENT: world-space canvas parented to the vehicle, positioned low in
    /// the driver's forward view. NOT head-locked — a head-locked HUD in a
    /// passthrough application feels like a helmet visor and adds to discomfort.
    ///
    /// OWNER: Omkar.
    /// </summary>
    public class DrivingHUD : MonoBehaviour
    {
        [Header("Elements")]
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private TMP_Text speedText;
        [SerializeField] private TMP_Text speedUnitText;
        [SerializeField] private TMP_Text trainingModeText;
        [SerializeField] private TMP_Text priorityMirrorText;

        [Header("Speed colouring")]
        [SerializeField] private Color normalColour = Color.white;
        [SerializeField] private Color overLimitColour = new Color(1f, 0.5f, 0.4f);
        [SerializeField] private float overLimitTolerance = 5f;

        [Header("Sources")]
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState
        [SerializeField] private CountryProfileManager countryProfiles;

        private IVehicleState _vehicle;
        private float _limitKph = 50f;

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Resolve<IVehicleState>();
            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();

            if (speedUnitText != null) speedUnitText.text = "km/h";
            RefreshProfileLabels();
            SetVisible(false);
        }

        private void OnEnable()
        {
            SessionEvents.PhaseChanged += OnPhaseChanged;
            SessionEvents.CountryProfileChanged += _ => RefreshProfileLabels();
        }

        private void OnDisable()
        {
            SessionEvents.PhaseChanged -= OnPhaseChanged;
        }

        private void OnPhaseChanged(SessionPhase phase)
        {
            bool driving = phase == SessionPhase.Driving || phase == SessionPhase.HazardActive;
            SetVisible(driving);

            if (trainingModeText != null)
                trainingModeText.text = phase == SessionPhase.HazardActive
                    ? "HAZARD" : "TRAINING DRIVE";
        }

        private void Update()
        {
            if (_vehicle == null || speedText == null) return;

            float kph = Mathf.Abs(_vehicle.CurrentSpeedKph);
            speedText.text = Mathf.RoundToInt(kph).ToString();
            speedText.color = kph > _limitKph + overLimitTolerance
                ? overLimitColour : normalColour;
        }

        private void RefreshProfileLabels()
        {
            var profile = countryProfiles?.Current;
            if (profile == null) return;

            _limitKph = profile.defaultSpeedLimitKph;

            if (priorityMirrorText != null)
                priorityMirrorText.text = $"WATCH: {profile.priorityMirror} MIRROR";
        }

        public void SetVisible(bool visible)
        {
            if (hudRoot != null) hudRoot.SetActive(visible);
        }
    }
}
