using UnityEngine;
using IDS.Core;
using IDS.AI;

namespace IDS.Vehicle
{
    /// <summary>
    /// The project's novelty claim, implemented: one vehicle, one scene, and the
    /// drive side is a data-driven transform rather than a second car and a second
    /// map.
    ///
    /// What moves when the country profile says right-hand drive:
    ///   - the seat anchor, which is where the XR Origin is parented
    ///   - the steering column, and with it the wheel
    ///   - the pedal box
    ///   - the indicator stalk (visual only)
    ///   - which mirror carries the priority highlight
    ///
    /// What does NOT move here: lane direction, traffic spawn side and
    /// intersection priority, all of which come from the country profile where
    /// the environment and the scorer read it.
    ///
    /// AUTHORING RULE: put the anchors in at their LEFT-hand-drive positions and
    /// let this script mirror them. Authoring both sides by hand is exactly the
    /// duplication this design exists to remove — and the two copies drift.
    ///
    /// Mirroring happens by negating each anchor's local X against the captured
    /// baseline, so it is idempotent: applying right-hand drive twice leaves the
    /// cabin where it was, and switching back and forth does not accumulate error.
    /// </summary>
    public class CabinConfigurator : MonoBehaviour
    {
        [Header("Anchors — author these at LEFT-hand-drive positions")]
        [Tooltip("Driver's eye position. The XR Origin is parented to this.")]
        [SerializeField] private Transform driverAnchor;
        [SerializeField] private Transform steeringColumnAnchor;
        [SerializeField] private Transform pedalBoxAnchor;
        [SerializeField] private Transform indicatorStalkAnchor;
        [SerializeField] private Transform dashboardAnchor;

        [Header("Mirrors")]
        [SerializeField] private Transform leftMirror;
        [SerializeField] private Transform rightMirror;
        [SerializeField] private Transform centreMirror;

        [Tooltip("Highlight moved onto whichever mirror the current profile says " +
                 "matters most. A visual cue for the learner, nothing more.")]
        [SerializeField] private GameObject priorityMirrorHighlight;

        [Header("State")]
        [SerializeField] private TrafficSide currentDriverSeatSide = TrafficSide.Right;

        private Transform[] _mirrored;
        private Vector3[] _baseLocalPositions;
        private bool _captured;

        public TrafficSide CurrentDriverSeatSide => currentDriverSeatSide;
        public Transform DriverAnchor => driverAnchor;
        public Transform SteeringColumnAnchor => steeringColumnAnchor;
        public Transform LeftMirror => leftMirror;
        public Transform RightMirror => rightMirror;
        public Transform CentreMirror => centreMirror;

        private void Awake()
        {
            CaptureBaseline();
            ServiceRegistry.Register(this);
        }

        private void OnEnable() => SessionEvents.CountryProfileChanged += OnProfileChanged;
        private void OnDisable() => SessionEvents.CountryProfileChanged -= OnProfileChanged;

        private void CaptureBaseline()
        {
            _mirrored = new[]
            {
                driverAnchor, steeringColumnAnchor, pedalBoxAnchor,
                indicatorStalkAnchor, dashboardAnchor
            };

            _baseLocalPositions = new Vector3[_mirrored.Length];
            for (int i = 0; i < _mirrored.Length; i++)
                _baseLocalPositions[i] = _mirrored[i] != null
                    ? _mirrored[i].localPosition
                    : Vector3.zero;

            _captured = true;
        }

        private void OnProfileChanged(string profileId)
        {
            CountryProfile profile = ServiceRegistry.Resolve<CountryProfileManager>()?.Current;
            if (profile != null) Apply(profile);
        }

        public void Apply(CountryProfile profile)
        {
            if (profile == null) return;
            SetDriverSeatSide(profile.driverSeatSide);
            SetPriorityMirror(profile.priorityMirror);
        }

        public void SetDriverSeatSide(TrafficSide side)
        {
            if (!_captured) CaptureBaseline();
            currentDriverSeatSide = side;

            // Left-hand drive is the authored baseline; right-hand drive mirrors X.
            // Computed from the baseline rather than the current position, so this
            // is idempotent and cannot accumulate drift across switches.
            float sign = side == TrafficSide.Left ? 1f : -1f;

            for (int i = 0; i < _mirrored.Length; i++)
            {
                Transform t = _mirrored[i];
                if (t == null) continue;

                Vector3 p = _baseLocalPositions[i];
                p.x *= sign;
                t.localPosition = p;
            }

            AimMirrorsAtDriver();

            Debug.Log($"[Cabin] driver seat → {side}-hand drive " +
                      $"(seat local x = {(driverAnchor != null ? driverAnchor.localPosition.x : 0f):F2})");
        }

        /// <summary>
        /// Re-angles all three mirror glasses at the seat after it moves.
        ///
        /// The mirror housings do not mirror — a real car has both door mirrors
        /// whichever side the driver sits on — but the angle of the glass depends
        /// entirely on where the driver's eyes are, and the seat has just moved
        /// about 0.76 m across the cabin.
        /// </summary>
        private void AimMirrorsAtDriver()
        {
            if (driverAnchor == null) return;

            AimMirror(leftMirror);
            AimMirror(rightMirror);
            AimMirror(centreMirror);
        }

        private void AimMirror(Transform mirror)
        {
            if (mirror == null) return;

            MirrorView view = mirror.GetComponentInChildren<MirrorView>(true);
            if (view != null) view.AimGlassAt(driverAnchor);
        }

        public void SetPriorityMirror(TrafficSide mirrorSide)
        {
            if (priorityMirrorHighlight == null) return;

            Transform target = mirrorSide == TrafficSide.Left ? leftMirror : rightMirror;
            if (target == null) return;

            // Parent to the mirror HEAD, not the mirror root. The head is what
            // rotates to face the driver; a highlight on the root would sit at a
            // different angle from the glass it is supposed to be outlining.
            MirrorView targetView = target.GetComponentInChildren<MirrorView>(true);
            if (targetView != null && targetView.AimPivot != null) target = targetView.AimPivot;

            priorityMirrorHighlight.transform.SetParent(target, false);

            // BEHIND the glass. The head's +Z faces the driver and the glass sits
            // at the head's origin, so a highlight at localPosition zero lands on
            // top of the reflection and blanks the mirror out. At -Z it is hidden
            // by the glass itself and only its oversized edges show, which is the
            // glowing border this is supposed to be.
            priorityMirrorHighlight.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            priorityMirrorHighlight.transform.localRotation = Quaternion.identity;
            priorityMirrorHighlight.SetActive(true);
        }

        public void Configure(Transform driver, Transform column, Transform pedals,
                              Transform stalk, Transform dashboard,
                              Transform mirrorLeft, Transform mirrorRight,
                              Transform mirrorCentre, GameObject highlight)
        {
            driverAnchor = driver;
            steeringColumnAnchor = column;
            pedalBoxAnchor = pedals;
            indicatorStalkAnchor = stalk;
            dashboardAnchor = dashboard;
            leftMirror = mirrorLeft;
            rightMirror = mirrorRight;
            centreMirror = mirrorCentre;
            priorityMirrorHighlight = highlight;
            CaptureBaseline();
        }
    }
}
