using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Brings XR up explicitly rather than relying on "Initialize XR on Startup",
    /// and logs loudly when it fails. On a shared-headset project the difference
    /// between "the app is broken" and "XR did not initialise" costs about forty
    /// minutes of lab time each occurrence, so it is worth the ceremony.
    ///
    /// Also reports the active loader, which is how you catch the case where the
    /// Oculus XR Plugin sneaks back in alongside OpenXR.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public class XRBootstrap : MonoBehaviour
    {
        [SerializeField] private bool initialiseOnStart = true;
        [Tooltip("Target display refresh. Quest 3 supports 72/80/90/120. Leave at " +
                 "72 until every mandatory feature works.")]
        [SerializeField] private int targetFrameRate = 72;

        public bool XRActive { get; private set; }
        public string ActiveLoader { get; private set; } = "none";

        private IEnumerator Start()
        {
            Application.targetFrameRate = targetFrameRate;

            if (!initialiseOnStart) yield break;

            var mgr = XRGeneralSettings.Instance?.Manager;
            if (mgr == null)
            {
                Debug.LogError("[XR] XRGeneralSettings.Manager is null. XR Plug-in " +
                               "Management is not configured for this build target.");
                yield break;
            }

            if (mgr.activeLoader == null)
            {
                yield return mgr.InitializeLoader();

                if (mgr.activeLoader == null)
                {
                    Debug.LogError("[XR] no active loader after initialisation. " +
                                   "Check Project Settings → XR Plug-in Management → " +
                                   "Android → OpenXR is ticked, and that the Meta " +
                                   "Quest feature group is enabled.");
                    yield break;
                }
            }

            mgr.StartSubsystems();
            XRActive = true;
            ActiveLoader = mgr.activeLoader.name;
            Debug.Log($"[XR] active loader: {ActiveLoader} @ {targetFrameRate} FPS target");

            ServiceRegistry.Register(this);
        }

        private void OnDestroy()
        {
            var mgr = XRGeneralSettings.Instance?.Manager;
            if (mgr == null || !XRActive) return;
            mgr.StopSubsystems();
            mgr.DeinitializeLoader();
            XRActive = false;
        }
    }
}
