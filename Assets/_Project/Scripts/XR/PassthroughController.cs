using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace IDS.XR
{
    /// <summary>
    /// Turns Meta Quest passthrough on and configures the camera so the real room
    /// shows through. With Unity's Meta OpenXR provider, passthrough arrives via
    /// AR Foundation's camera background — so the scene needs an ARSession and an
    /// ARCameraManager on the XR Origin's camera.
    ///
    /// SCENE SETUP (XR_Test.unity):
    ///   XR Origin (XR Rig)
    ///     └── Camera Offset
    ///           └── Main Camera   + ARCameraManager + ARCameraBackground
    ///   AR Session                 (GameObject with ARSession component)
    ///   Passthrough Controller     (this script)
    ///
    /// The camera's Background Type must be Solid Color with colour (0,0,0,0).
    /// Any opaque clear colour paints over the passthrough feed and you get the
    /// classic "black screen but the app is running" symptom.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PassthroughController : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private ARSession arSession;
        [SerializeField] private bool enablePassthroughOnStart = true;

        [Tooltip("Editor / desktop fallback colour so the scene is not black " +
                 "when there is no passthrough source.")]
        [SerializeField] private Color editorFallbackSky = new Color(0.42f, 0.55f, 0.65f);

        private Camera _camera;
        public bool PassthroughActive { get; private set; }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (cameraManager == null) cameraManager = GetComponent<ARCameraManager>();
            if (arSession == null)     arSession = FindObjectOfType<ARSession>();
        }

        private IEnumerator Start()
        {
            // The XR subsystem is not always up on the first frame.
            yield return null;

            if (enablePassthroughOnStart) SetPassthrough(true);
            else SetPassthrough(false);
        }

        public void SetPassthrough(bool on)
        {
            bool supported = arSession != null && cameraManager != null
                             && !Application.isEditor;

            if (on && supported)
            {
                arSession.enabled = true;
                cameraManager.enabled = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                PassthroughActive = true;
                Debug.Log("[Passthrough] enabled");
            }
            else
            {
                if (cameraManager != null) cameraManager.enabled = false;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = editorFallbackSky;
                PassthroughActive = false;
                Debug.Log(on
                    ? "[Passthrough] requested but unavailable — using fallback sky"
                    : "[Passthrough] disabled");
            }
        }

        public void TogglePassthrough() => SetPassthrough(!PassthroughActive);
    }
}
