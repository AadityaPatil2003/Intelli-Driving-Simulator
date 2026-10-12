using UnityEngine;
using IDS.Core;

namespace IDS.XR
{
    /// <summary>
    /// The controller bindings that move you through the session without
    /// pointing at a button.
    ///
    /// Everything here is on B / Y (secondaryButton on either controller),
    /// which XRControls.RecenterPressed deliberately does NOT claim. What the
    /// press does depends on the phase, and the one destructive action — ending
    /// a measured drive early — needs a HOLD rather than a press, so it cannot
    /// happen by accident mid-corner.
    ///
    ///   Driving / HazardActive   hold B or Y for 1 s   end the drive now and
    ///                                                   go straight to scoring
    ///   Report                   press B or Y          dismiss the report
    ///   Complete                 press B or Y          new session, back to the
    ///                                                   welcome screen, where the
    ///                                                   country can be changed
    ///   Ready                    press B or Y          back to the welcome screen
    ///                                                   without driving
    ///
    /// A progress value is published while the hold is building so the HUD can
    /// show it; without feedback a one-second hold feels like a dead button.
    /// </summary>
    public class VRSessionShortcuts : MonoBehaviour
    {
        [Tooltip("How long B/Y must be held during a drive to end it early.")]
        [SerializeField] private float holdToEndSeconds = 1f;

        private SessionManager _session;
        private SessionPhase _phase = SessionPhase.Boot;
        private float _heldFor;

        /// <summary>0–1 while the end-drive hold is building. 0 otherwise.</summary>
        public float EndDriveHoldProgress =>
            holdToEndSeconds <= 0f ? 0f : Mathf.Clamp01(_heldFor / holdToEndSeconds);

        private void Awake() => ServiceRegistry.Register(this);

        private void Start() => _session ??= ServiceRegistry.Resolve<SessionManager>();

        private void OnEnable() => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            _phase = phase;
            _heldFor = 0f;
        }

        private void Update()
        {
            if (_session == null)
            {
                _session = ServiceRegistry.Resolve<SessionManager>();
                if (_session == null) return;
            }

            bool driving = _phase == SessionPhase.Driving
                           || _phase == SessionPhase.HazardActive;

            if (driving) { TickEndDriveHold(); return; }

            _heldFor = 0f;

            if (!Pressed()) return;

            switch (_phase)
            {
                case SessionPhase.Ready:
                    Debug.Log("[Shortcuts] B/Y on Ready → welcome screen.");
                    _session.ReturnToWelcome();
                    break;

                case SessionPhase.Report:
                    Debug.Log("[Shortcuts] B/Y on Report → dismissed.");
                    _session.OnReportDismissed();
                    break;

                case SessionPhase.Complete:
                    Debug.Log("[Shortcuts] B/Y on Complete → new session.");
                    _session.ReturnToWelcome();
                    break;
            }
        }

        private void TickEndDriveHold()
        {
            if (!Held())
            {
                _heldFor = 0f;
                return;
            }

            _heldFor += Time.deltaTime;
            if (_heldFor < holdToEndSeconds) return;

            _heldFor = 0f;
            Debug.Log("[Shortcuts] B/Y held → ending the drive early.");
            _session.EndDrive();
        }

        private static bool Pressed() =>
            XRControls.SecondaryPressed(Hand.Left)
            || XRControls.SecondaryPressed(Hand.Right);

        private static bool Held() =>
            XRControls.Held(Hand.Left, "secondaryButton")
            || XRControls.Held(Hand.Right, "secondaryButton");
    }
}
