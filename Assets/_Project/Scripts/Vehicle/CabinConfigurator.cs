using UnityEngine;
using IDS.Core;
using IDS.AI;

namespace IDS.Vehicle
{
    /// <summary>
    /// The novelty claim, implemented. ONE vehicle prefab, ONE scene. The drive
    /// side is a data-driven transform, not a second vehicle and not a second map.
    ///
    /// What flips when the country profile says right-hand drive:
    ///   - driver seat / XR Origin anchor  → mirrored across the vehicle's local X
    ///   - steering column + wheel prop anchor
    ///   - pedal box anchor
    ///   - indicator stalk (visual only)
    ///   - which mirror is the "priority" mirror
    ///
    /// What does NOT flip here (Ananya's environment code and Anushka's profile
    /// handle these): lane direction, traffic spawn side, intersection priority.
    ///
    /// HOW TO USE: put the anchors in the prefab at their LEFT-HAND-DRIVE
    /// positions and let this script mirror them. Do not author both sides by
    /// hand — that is the duplication this design exists to avoid.
    ///
    /// OWNER: Ananya.
    /// </summary>
    public class CabinConfigurator : MonoBehaviour
    {
        [Header("Anchors - author these at LEFT-hand-drive positions")]
        [SerializeField] private Transform driverAnchor;       // where the XR Origin sits
        [SerializeField] private Transform steeringColumnAnchor;
        [SerializeField] private Transform pedalBoxAnchor;
        [SerializeField] private Transform indicatorStalkAnchor;

        [Header("Mirrors")]
        [SerializeField] private Transform leftMirror;
        [SerializeField] private Transform rightMirror;
        [Tooltip("Highlight applied to whichever mirror the current profile says " +
                 "matters most. Purely a visual cue for the learner.")]
        [SerializeField] private GameObject priorityMirrorHighlight;

        [Header("State")]
        [SerializeField] private TrafficSide currentDriverSeatSide = TrafficSide.Left;

        private Vector3[] _baseLocalPositions;
        private Transform[] _mirroredTransforms;
        private bool _captured;

        private void Awake()
        {
            CaptureBasePositions();
            ServiceRegistry.Register(this);
        }

        private void OnEnable()  => SessionEvents.CountryProfileChanged += OnProfileChanged;
        private void OnDisable() => SessionEvents.CountryProfileChanged -= OnProfileChanged;

        private void CaptureBasePositions()
        {
            _mirroredTransforms = new[]
            {
                driverAnchor, steeringColumnAnchor, pedalBoxAnchor, indicatorStalkAnchor
            };

            _baseLocalPositions = new Vector3[_mirroredTransforms.Length];
            for (int i = 0; i < _mirroredTransforms.Length; i++)
                _baseLocalPositions[i] = _mirroredTransforms[i] != null
                    ? _mirroredTransforms[i].localPosition
                    : Vector3.zero;

            _captured = true;
        }

        private void OnProfileChanged(string profileId)
        {
            var profile = ServiceRegistry.Resolve<CountryProfileManager>()?.Current;
            if (profile == null) return;
            Apply(profile);
        }

        /// <summary>Called by CountryProfileManager, or directly from a test scene.</summary>
        public void Apply(CountryProfile profile)
        {
            if (profile == null) return;
            SetDriverSeatSide(profile.driverSeatSide);
            SetPriorityMirror(profile.priorityMirror);
        }

        public void SetDriverSeatSide(TrafficSide side)
        {
            if (!_captured) CaptureBasePositions();
            currentDriverSeatSide = side;

            // Left-hand drive is the authored baseline. Right-hand drive mirrors X.
            float sign = side == TrafficSide.Left ? 1f : -1f;

            for (int i = 0; i < _mirroredTransforms.Length; i++)
            {
                var t = _mirroredTransforms[i];
                if (t == null) continue;

                Vector3 p = _baseLocalPositions[i];
                p.x = Mathf.Abs(p.x) * Mathf.Sign(_baseLocalPositions[i].x) * sign;
                t.localPosition = p;
            }

            Debug.Log($"[Cabin] driver seat → {side}-hand drive");
        }

        public void SetPriorityMirror(TrafficSide mirrorSide)
        {
            if (priorityMirrorHighlight == null) return;

            Transform target = mirrorSide == TrafficSide.Left ? leftMirror : rightMirror;
            if (target == null) return;

            priorityMirrorHighlight.transform.SetParent(target, false);
            priorityMirrorHighlight.transform.localPosition = Vector3.zero;
            priorityMirrorHighlight.SetActive(true);
        }

        public TrafficSide CurrentDriverSeatSide => currentDriverSeatSide;

        /// <summary>Where Aaditya parents the XR Origin so the driver sits in the seat.</summary>
        public Transform DriverAnchor => driverAnchor;
    }
}
