using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Simulator-sickness mitigations, in one place so they can be defended in
    /// the final report rather than scattered through the codebase.
    ///
    /// The theory: discomfort comes from vection — visual motion the vestibular
    /// system does not corroborate. Passthrough already gives a stable real-world
    /// reference frame, which is the strongest mitigation we have. On top of that:
    ///
    ///  1. The camera is never moved by script. The vehicle moves under the
    ///     driver; the head pose is the headset's, untouched.
    ///  2. No artificial camera acceleration, shake, tilt or head-bob. Ever.
    ///  3. Speed is capped low (40-60 km/h) and acceleration is gentle.
    ///  4. A static reference cage — a thin frame at the edge of vision, locked
    ///     to the driver's head — can be toggled on for sensitive participants.
    ///  5. Optional vignette that narrows the field of view during turns.
    ///
    /// If a participant reports discomfort during user testing, stop. Do not ask
    /// them to push through it.
    ///
    /// OWNER: Aaditya. Report section: risk mitigation.
    /// </summary>
    public class ComfortSettings : MonoBehaviour
    {
        [Header("Static reference frame")]
        [Tooltip("Head-locked frame giving a fixed visual anchor. Off by default; " +
                 "offer it to participants who report discomfort.")]
        [SerializeField] private GameObject referenceCage;
        [SerializeField] private bool referenceCageEnabled;

        [Header("Turn vignette")]
        [SerializeField] private GameObject vignette;
        [SerializeField] private bool vignetteEnabled = true;
        [Tooltip("Steering magnitude above which the vignette starts to close in.")]
        [SerializeField] private float vignetteSteeringThreshold = 0.4f;

        [Header("Motion limits (advisory - enforced in VehicleController)")]
        [SerializeField] private float recommendedMaxSpeedKph = 50f;
        [SerializeField] private float recommendedMaxAccelG = 0.25f;

        public float RecommendedMaxSpeedKph => recommendedMaxSpeedKph;
        public float RecommendedMaxAccelG => recommendedMaxAccelG;

        private VehicleInputAdapter _input;

        private void Start()
        {
            _input = ServiceRegistry.Resolve<VehicleInputAdapter>();
            ApplySettings();
        }

        private void Update()
        {
            if (!vignetteEnabled || vignette == null || _input == null) return;
            bool turning = Mathf.Abs(_input.Steering) > vignetteSteeringThreshold;
            if (vignette.activeSelf != turning) vignette.SetActive(turning);
        }

        public void SetReferenceCage(bool on)
        {
            referenceCageEnabled = on;
            ApplySettings();
        }

        public void SetVignette(bool on)
        {
            vignetteEnabled = on;
            if (!on && vignette != null) vignette.SetActive(false);
        }

        private void ApplySettings()
        {
            if (referenceCage != null) referenceCage.SetActive(referenceCageEnabled);
            if (vignette != null && !vignetteEnabled) vignette.SetActive(false);
        }
    }
}
