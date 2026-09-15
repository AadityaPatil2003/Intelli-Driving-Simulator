using UnityEngine;
using IDS.Core;
using IDS.Scenarios;

namespace IDS.Dev
{
    /// <summary>
    /// Keyboard shortcuts for driving the session without touching the editor.
    /// This is what lets a demo run start to finish hands-off, which is the
    /// Milestone 2 acceptance test: someone else completes a session while you
    /// keep your hands off the Inspector.
    ///
    ///   H   force-trigger the currently armed hazard
    ///   E   end the drive now and produce the report
    ///   R   restart the session
    ///   F1  print the current session state to the Console
    ///
    /// Disable this component for the final submission build.
    /// </summary>
    public class DevHotkeys : MonoBehaviour
    {
        [SerializeField] private bool activeInBuild = true;

        private SessionManager _session;
        private ScenarioManager _scenarios;

        private void Start()
        {
            _session = ServiceRegistry.Resolve<SessionManager>();
            _scenarios = ServiceRegistry.Resolve<ScenarioManager>();

            Debug.Log("[DevHotkeys] H = trigger hazard · E = end drive · " +
                      "R = restart · F1 = print state");
        }

        private void Update()
        {
            if (!activeInBuild && !Application.isEditor) return;

            if (Input.GetKeyDown(KeyCode.H))
            {
                if (_scenarios != null) _scenarios.ForceTriggerArmed();
                else Debug.LogWarning("[DevHotkeys] no ScenarioManager registered.");
            }

            if (Input.GetKeyDown(KeyCode.E)) _session?.EndDrive();
            if (Input.GetKeyDown(KeyCode.R)) _session?.RestartSession();

            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (_session == null) { Debug.Log("[State] no SessionManager."); return; }
                Debug.Log($"[State] phase={_session.Phase} " +
                          $"session={_session.SessionId} " +
                          $"elapsed={_session.ElapsedDriveTime:F1}s");
            }
        }
    }
}
