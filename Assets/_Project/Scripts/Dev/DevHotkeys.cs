using UnityEngine;
using UnityEngine.InputSystem;
using IDS.Core;
using IDS.Scenarios;

namespace IDS.Dev
{
    /// <summary>
    /// Keyboard shortcuts for driving the session without touching the editor.
    /// This is what lets a demo run start to finish hands-off — someone else
    /// completes a session while nobody touches the Inspector.
    ///
    ///     H   force-trigger the currently armed hazard
    ///     E   end the drive now and produce the report
    ///     R   restart the session
    ///     F1  print the current session state to the Console
    ///
    /// Reads the Input System directly, so the project does not need the
    /// deprecated Input Manager backend turned on for three development scripts.
    /// On the headset `Keyboard.current` is null and every one of these is a no-op.
    /// </summary>
    public class DevHotkeys : MonoBehaviour
    {
        [Tooltip("Leave on: on the headset there is no keyboard, so these cannot " +
                 "fire. Turn it off only if a participant session will have a " +
                 "keyboard attached.")]
        [SerializeField] private bool activeInBuild = true;

        private SessionManager _session;
        private ScenarioManager _scenarios;

        private void Start()
        {
            _session = ServiceRegistry.Resolve<SessionManager>();
            _scenarios = ServiceRegistry.Resolve<ScenarioManager>();

            if (Keyboard.current != null)
                Debug.Log("[DevHotkeys] H = trigger hazard · E = end drive · " +
                          "R = restart · F1 = print state");
        }

        private void Update()
        {
            if (!activeInBuild && !Application.isEditor) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.hKey.wasPressedThisFrame)
            {
                if (_scenarios != null) _scenarios.ForceTriggerArmed();
                else Debug.LogWarning("[DevHotkeys] no ScenarioManager registered.");
            }

            if (kb.eKey.wasPressedThisFrame) _session?.EndDrive();
            if (kb.rKey.wasPressedThisFrame) _session?.RestartSession();

            if (!kb.f1Key.wasPressedThisFrame) return;

            if (_session == null) { Debug.Log("[State] no SessionManager."); return; }

            Debug.Log($"[State] phase={_session.Phase} session={_session.SessionId} " +
                      $"elapsed={_session.ElapsedDriveTime:F1}s " +
                      $"armed={_scenarios?.ArmedScenarioId}");
        }
    }
}
