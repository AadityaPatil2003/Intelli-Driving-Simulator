using UnityEngine;

namespace IDS.XR
{
    /// <summary>
    /// Drives this transform from a controller's tracked pose.
    ///
    /// This replaces TrackedPoseDriver deliberately. TrackedPoseDriver reads its
    /// pose through serialized InputActionProperty fields, which means the
    /// component works or silently does nothing depending on whether an action
    /// asset was wired correctly in the Inspector — and a scene built by script
    /// cannot verify that wiring. Reading the pose in code is four lines and
    /// either works or logs.
    ///
    /// PARENTING: this must be a child of the XR Origin's Camera Offset object,
    /// because the pose XRControls returns is in tracking space, which is what
    /// the Camera Offset transform represents.
    ///
    /// The visual child is hidden while the controller is not tracked, so a
    /// controller that goes to sleep leaves nothing floating in the cabin.
    /// </summary>
    public class XRControllerAnchor : MonoBehaviour
    {
        [SerializeField] private Hand hand = Hand.Right;

        [Tooltip("Hidden while the controller is not tracked. Usually the " +
                 "controller's visual mesh and its pointer line.")]
        [SerializeField] private GameObject visual;

        [Tooltip("Keep the last known pose when tracking is lost, rather than " +
                 "snapping to the origin. Prevents the pointer whipping across " +
                 "the cabin when a controller briefly drops out.")]
        [SerializeField] private bool holdLastPose = true;

        public Hand Hand => hand;
        public bool IsTracked { get; private set; }

        private bool _everTracked;

        public void Configure(Hand which, GameObject visualRoot)
        {
            hand = which;
            visual = visualRoot;
        }

        private void Update()
        {
            // Declared and pre-assigned rather than inlined as `out` variables:
            // behind a short-circuiting &&, inline out variables are not
            // definitely assigned and will not compile.
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            bool tracked = XRControls.Available(hand) &&
                           XRControls.TryGetPose(hand, out pos, out rot);

            if (tracked)
            {
                transform.localPosition = pos;
                transform.localRotation = rot;
                _everTracked = true;
            }
            else if (!holdLastPose || !_everTracked)
            {
                // Park it somewhere harmless rather than at the driver's eyes.
                transform.localPosition = new Vector3(hand == Hand.Left ? -0.3f : 0.3f,
                                                      -0.6f, 0.2f);
                transform.localRotation = Quaternion.identity;
            }

            IsTracked = tracked;
            if (visual != null && visual.activeSelf != tracked) visual.SetActive(tracked);
        }
    }
}
