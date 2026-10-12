using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Simulator-sickness mitigations, in one place so they can be defended in
    /// the report rather than scattered through the codebase.
    ///
    /// Discomfort comes from vection: visual motion the vestibular system does
    /// not corroborate. A passthrough build got one mitigation for free — the real
    /// room was always visible as a stable reference frame. Removing passthrough
    /// removes that, so the remaining mitigations matter more in this build than
    /// they did in the last one. This is the honest cost of the move to full VR
    /// and the report should say so.
    ///
    /// What is done instead:
    ///
    ///  1. The camera is never moved by script. The car moves under the driver;
    ///     the head pose is the headset's, untouched. VRSeatRecenter is the single
    ///     exception and it only moves the rig when the driver asks it to.
    ///  2. No artificial camera acceleration, shake, tilt or head-bob. Ever.
    ///  3. Speed is capped low and acceleration is gentle (VehicleController).
    ///  4. The cabin interior is always in view — a large, rigid, near-field
    ///     object that moves exactly with the driver is the strongest static
    ///     reference frame available in VR, and it is why a cockpit view is more
    ///     comfortable than a first-person walk.
    ///  5. An optional vignette narrows the field of view during turns, where
    ///     peripheral optical flow is strongest.
    ///  6. An optional head-locked reference cage for sensitive participants.
    ///
    /// If a participant reports discomfort during user testing, stop. Do not ask
    /// them to push through it.
    /// </summary>
    public class ComfortSettings : MonoBehaviour
    {
        [Header("Turn vignette")]
        [SerializeField] private GameObject vignette;
        [SerializeField] private bool vignetteEnabled = true;

        [Tooltip("Steering magnitude above which the vignette closes in.")]
        [SerializeField] private float vignetteSteeringThreshold = 0.35f;

        [Tooltip("Seconds the vignette takes to fade in and out. Snapping it on " +
                 "is itself a visual jolt.")]
        [SerializeField] private float vignetteFadeSeconds = 0.25f;

        [Header("Static reference frame")]
        [Tooltip("Head-locked frame giving a fixed visual anchor. Off by default; " +
                 "offer it to participants who report discomfort.")]
        [SerializeField] private GameObject referenceCage;
        [SerializeField] private bool referenceCageEnabled;

        [Header("Motion limits (advisory — enforced in VehicleController)")]
        [SerializeField] private float recommendedMaxSpeedKph = 50f;
        [SerializeField] private float recommendedMaxAccelG = 0.25f;

        public float RecommendedMaxSpeedKph => recommendedMaxSpeedKph;
        public float RecommendedMaxAccelG => recommendedMaxAccelG;
        public bool VignetteEnabled => vignetteEnabled;
        public bool ReferenceCageEnabled => referenceCageEnabled;

        private VehicleInputAdapter _input;
        private CanvasGroup _vignetteFade;
        private float _vignetteTarget;

        private void Awake() => ServiceRegistry.Register(this);

        private void Start()
        {
            _input = ServiceRegistry.Resolve<VehicleInputAdapter>();
            if (vignette != null) _vignetteFade = vignette.GetComponent<CanvasGroup>();
            Apply();
        }

        private void Update()
        {
            if (!vignetteEnabled || vignette == null || _input == null) return;

            bool turning = Mathf.Abs(_input.Steering) > vignetteSteeringThreshold;
            _vignetteTarget = turning ? 1f : 0f;

            if (_vignetteFade == null)
            {
                // No CanvasGroup to fade: fall back to on/off, which is still
                // better than no vignette at all.
                if (vignette.activeSelf != turning) vignette.SetActive(turning);
                return;
            }

            if (!vignette.activeSelf) vignette.SetActive(true);

            float step = Time.deltaTime / Mathf.Max(0.01f, vignetteFadeSeconds);
            _vignetteFade.alpha = Mathf.MoveTowards(_vignetteFade.alpha, _vignetteTarget, step);
        }

        public void SetReferenceCage(bool on)
        {
            referenceCageEnabled = on;
            Apply();
        }

        public void SetVignette(bool on)
        {
            vignetteEnabled = on;
            Apply();
        }

        private void Apply()
        {
            if (referenceCage != null) referenceCage.SetActive(referenceCageEnabled);

            if (vignette == null) return;

            if (!vignetteEnabled)
            {
                if (_vignetteFade != null) _vignetteFade.alpha = 0f;
                vignette.SetActive(false);
            }
            else if (_vignetteFade != null)
            {
                vignette.SetActive(true);
                _vignetteFade.alpha = 0f;
            }
        }

        public void Configure(GameObject vignetteRoot, GameObject cage)
        {
            vignette = vignetteRoot;
            referenceCage = cage;
        }
    }
}
