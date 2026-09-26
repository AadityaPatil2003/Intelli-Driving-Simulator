#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using IDS.UI;

namespace IDS.EditorTools
{
    /// <summary>
    /// Connects the Main scene's buttons to UIManager, and says what it found.
    ///
    ///     Tools → Intelli-Driving → Wire Main Scene Buttons
    ///
    /// Why this is a separate script: a Button's onClick list is serialized, and
    /// adding to it needs UnityEventTools, which is editor-only. Doing it by hand
    /// is six drag-and-drops that are easy to get subtly wrong — a listener on
    /// the wrong object looks identical in the Inspector and simply never fires.
    ///
    /// It is safe to run repeatedly. Existing listeners on a button are cleared
    /// first, so running it twice does not double-fire.
    ///
    /// Run it with the Main scene OPEN.
    /// </summary>
    public static class MainSceneButtonWirer
    {
        [MenuItem("Tools/Intelli-Driving/Wire Main Scene Buttons", false, 23)]
        public static void WireButtons()
        {
            var ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (ui == null)
            {
                EditorUtility.DisplayDialog("No UIManager",
                    "No UIManager in the open scene.\n\n" +
                    "Open Assets/_Project/Scenes/Main/Main.unity first, then run " +
                    "this again.", "OK");
                return;
            }

            // Inactive included: most panels are switched off at build time, so
            // an active-only search finds almost nothing and looks like the
            // buttons were never created.
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include);

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Found {buttons.Length} button(s) in the scene:\n");

            int wired = 0;

            foreach (var button in buttons.OrderBy(b => Path(b.transform)))
            {
                UnityAction action = ActionFor(button.name, ui);
                string path = Path(button.transform);

                if (action == null)
                {
                    report.AppendLine($"   [skip] {path}");
                    continue;
                }

                // Clear first so re-running does not stack duplicate listeners.
                for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(button.onClick, i);

                UnityEventTools.AddPersistentListener(button.onClick, action);

                // A Button with no targetGraphic gives no visual press feedback,
                // which testers read as "the button is broken".
                if (button.targetGraphic == null)
                    button.targetGraphic = button.GetComponent<Graphic>();

                EditorUtility.SetDirty(button);
                wired++;
                report.AppendLine($"   [ok]   {path}  →  UIManager.{action.Method.Name}");
            }

            if (buttons.Length == 0)
            {
                report.AppendLine("   (none)\n\nThe scene has no Button components at all. " +
                                  "Rebuild it with Tools → Intelli-Driving → Build Main XR Scene " +
                                  "and check the Console for errors during the build.");
            }

            EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("<b>[ButtonWirer]</b>\n" + report);
            EditorUtility.DisplayDialog("Button wiring",
                $"Wired {wired} of {buttons.Length} button(s).\n\n" +
                "Full list is in the Console.", "OK");
        }

        /// <summary>
        /// Name-based, because the bootstrapper names buttons deterministically.
        /// An unknown name is skipped and reported rather than guessed at.
        /// </summary>
        private static UnityAction ActionFor(string name, UIManager ui)
        {
            switch (name)
            {
                case "StartButton":       return ui.OnStartPressed;
                case "CalibrateButton":   return ui.OnCalibratePressed;
                case "ConfirmButton":     return ui.OnCalibrationConfirmPressed;
                case "BeginDriveButton":  return ui.OnBeginDrivePressed;
                case "DriveAgainButton":  return ui.OnStartPressed;
                case "DoneButton":        return ui.OnExitPressed;
                case "EndDriveButton":    return ui.OnEndDrivePressed;
                default:                  return null;   // country cards wire themselves
            }
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
#endif