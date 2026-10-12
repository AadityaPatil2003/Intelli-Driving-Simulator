using UnityEngine;
using IDS.Core;

namespace IDS.UI
{
    /// <summary>
    /// Decides, per session phase, whether the driver is sitting in a car or
    /// standing in an empty room reading a menu — and puts the menu canvas where
    /// it can actually be read in each case.
    ///
    /// WHY THIS EXISTS
    ///
    /// The menu canvas is parented to the seat anchor so it is always straight
    /// ahead and mirrors with the drive side for free. That is right, but it put
    /// the menus inside the cabin, and the cabin has a dashboard in it.
    ///
    /// The dashboard fascia's top edge sits at y 0.71 with its far face at
    /// z 0.66, and the driver's eyes are at y 0.78. From the seat that edge
    /// subtends -4.94°, so EVERY part of the view below -4.94° is behind the
    /// dashboard. That limit is an angle, not a distance: pushing the canvas
    /// further away does not help, because the canvas grows at the same rate the
    /// occlusion does. The canvas as authored spanned -18.6° to +21.9°, so the
    /// bottom 34% of it — which is where every primary button lives — was behind
    /// the dash, invisible but still clickable, because the cabin boxes have no
    /// colliders and VRUIPointer intersects canvas planes mathematically. A
    /// driver could press START without ever seeing it, then reach the country
    /// screen and find no countries.
    ///
    /// THE FIX
    ///
    /// Two placements, chosen by phase:
    ///
    ///   Hall  — the car is not drawn at all. The canvas sits 2.6 m ahead at
    ///           2.67 × 1.85 m, spanning -17.6° to +21.5°. Nothing occludes it
    ///           because there is nothing there. Used for the screens that are
    ///           pure menu: Welcome, country selection, analysing, report,
    ///           complete.
    ///
    ///   Cabin — the car is drawn and the driver is in the seat. The canvas is
    ///           smaller and lifted to sit entirely above the dashboard line,
    ///           spanning -3.3° to +27.4°. Used for the two phases where being
    ///           in the seat is the point: recentring and the ready prompt.
    ///
    /// Hiding the car means disabling RENDERERS, never GameObjects. The vehicle
    /// hierarchy contains the XR Origin, the wheel colliders, the rigidbody and
    /// every mirror camera; deactivating those objects would take the driver's
    /// head tracking with them.
    /// </summary>
    public class MenuStaging : MonoBehaviour
    {
        [Header("Scene references")]
        [Tooltip("Root of the vehicle. Every renderer under this is hidden " +
                 "during the pure-menu phases.")]
        [SerializeField] private Transform vehicleRoot;

        [Tooltip("The world-space menu canvas, parented to the seat anchor.")]
        [SerializeField] private Transform menuCanvas;

        [Tooltip("Renderers under these transforms are never touched — the UI " +
                 "itself, and anything parented to the XR rig.")]
        [SerializeField] private Transform[] exclusions;

        [Header("Hall placement — car hidden")]
        [SerializeField] private Vector3 hallPosition = new Vector3(0f, 0.10f, 2.60f);
        [SerializeField] private float hallScale = 0.00215f;

        [Header("Cabin placement — car visible, above the dashboard line")]
        [SerializeField] private Vector3 cabinPosition = new Vector3(0f, 0.369f, 1.60f);
        [SerializeField] private float cabinScale = 0.00107f;

        public Vector3 HallPosition => hallPosition;
        public float HallScale => hallScale;
        public Vector3 CabinPosition => cabinPosition;
        public float CabinScale => cabinScale;

        private Renderer[] _bodywork;
        private bool _visible = true;

        private void Awake()
        {
            CollectRenderers();

            // Boot shows nothing, but the first phase raised is Welcome, and a
            // frame of fully-drawn cabin before it would read as a glitch.
            SetCarVisible(false);
            Place(hallPosition, hallScale);
        }

        private void OnEnable() => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void CollectRenderers()
        {
            if (vehicleRoot == null)
            {
                _bodywork = new Renderer[0];
                return;
            }

            var keep = new System.Collections.Generic.List<Renderer>();

            foreach (Renderer r in vehicleRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (IsExcluded(r.transform)) continue;

                // Canvas content renders through CanvasRenderer, not Renderer, so
                // UI is already safe — but a world-space canvas may carry a
                // TextMeshPro renderer, and hiding the menu with the car would
                // leave the driver staring at nothing at all.
                if (r.GetComponentInParent<Canvas>() != null) continue;

                keep.Add(r);
            }

            _bodywork = keep.ToArray();
        }

        private bool IsExcluded(Transform t)
        {
            if (exclusions == null) return false;

            foreach (Transform e in exclusions)
            {
                if (e == null) continue;
                if (t == e || t.IsChildOf(e)) return true;
            }

            return false;
        }

        private void OnPhaseChanged(SessionPhase phase)
        {
            bool inCar = phase switch
            {
                SessionPhase.Recenter => true,
                SessionPhase.Ready => true,
                SessionPhase.Driving => true,
                SessionPhase.HazardActive => true,
                _ => false
            };

            SetCarVisible(inCar);

            if (inCar) Place(cabinPosition, cabinScale);
            else Place(hallPosition, hallScale);
        }

        private void SetCarVisible(bool visible)
        {
            if (_bodywork == null) return;
            if (_visible == visible && _bodywork.Length > 0) return;

            _visible = visible;

            for (int i = 0; i < _bodywork.Length; i++)
                if (_bodywork[i] != null) _bodywork[i].enabled = visible;
        }

        private void Place(Vector3 localPosition, float scale)
        {
            if (menuCanvas == null) return;

            menuCanvas.localPosition = localPosition;
            menuCanvas.localRotation = Quaternion.identity;
            menuCanvas.localScale = new Vector3(scale, scale, scale);
        }

        public void Configure(Transform vehicle, Transform canvas, Transform[] excluded)
        {
            vehicleRoot = vehicle;
            menuCanvas = canvas;
            exclusions = excluded;
        }
    }
}
