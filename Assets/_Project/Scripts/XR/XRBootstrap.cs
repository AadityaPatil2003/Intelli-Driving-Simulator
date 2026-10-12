using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// Brings XR up and says loudly when it does not come up. On a shared-headset
    /// project the difference between "the app is broken" and "XR did not
    /// initialise" costs about forty minutes of lab time per occurrence, so the
    /// ceremony is worth it.
    ///
    /// It also names the active loader, which is how you catch a second XR plugin
    /// sneaking in alongside OpenXR — the symptom of that is a black display with
    /// no error at all.
    ///
    /// It does not fight XR Plug-in Management's own "Initialize XR on Startup":
    /// if a loader is already active it leaves it alone and only reports.
    /// </summary>
    public class XRBootstrap : MonoBehaviour
    {
        [SerializeField] private bool initialiseOnStart = true;

        [Tooltip("Target display refresh. Quest 3 supports 72/80/90/120. Raise " +
                 "this only after a full session has held frame rate at 72.")]
        [SerializeField] private int targetFrameRate = 72;

        public bool XRActive { get; private set; }

        // Whether THIS component initialised the loader. If XR was already
        // running when we arrived, tearing it down on destroy kills the display
        // for whatever started it — a black headset with no error anywhere.
        private bool _startedLoaderHere;
        public string ActiveLoader { get; private set; } = "none";

        private IEnumerator Start()
        {
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount = 0;   // the compositor paces frames, not vSync

            ServiceRegistry.Register(this);

            if (!initialiseOnStart) yield break;

            XRManagerSettings manager = XRGeneralSettings.Instance?.Manager;
            if (manager == null)
            {
                Debug.LogError("[XR] XRGeneralSettings.Manager is null — XR Plug-in " +
                               "Management has no settings for this build target. " +
                               "Project Settings → XR Plug-in Management → Android, " +
                               "tick OpenXR.");
                yield break;
            }

            if (manager.activeLoader == null)
            {
                yield return manager.InitializeLoader();

                if (manager.activeLoader == null)
                {
                    Debug.LogError("[XR] no active loader after initialisation. Check " +
                                   "Project Settings → XR Plug-in Management → Android " +
                                   "→ OpenXR is ticked, and that under OpenXR the Meta " +
                                   "Quest feature group and the Oculus Touch " +
                                   "controller profile are both enabled. In the editor " +
                                   "without a headset this message is expected.");
                    yield break;
                }

                manager.StartSubsystems();
                _startedLoaderHere = true;
            }

            XRActive = true;
            ActiveLoader = manager.activeLoader.name;

            Debug.Log($"[XR] active loader: {ActiveLoader}, target {targetFrameRate} FPS, " +
                      $"display {UnityEngine.XR.XRSettings.eyeTextureWidth}×" +
                      $"{UnityEngine.XR.XRSettings.eyeTextureHeight} per eye");
        }

        private void OnDestroy()
        {
            if (!XRActive || !_startedLoaderHere) return;

            XRManagerSettings manager = XRGeneralSettings.Instance?.Manager;
            if (manager == null) return;

            manager.StopSubsystems();
            manager.DeinitializeLoader();
            XRActive = false;
        }
    }
}
