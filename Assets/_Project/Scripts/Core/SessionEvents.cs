using System;

namespace IDS.Core
{
    /// <summary>
    /// The only place subsystems are allowed to shout at each other. A static
    /// event bus rather than direct references means the UI can be built and
    /// tested before the scorer exists, and vice versa.
    ///
    /// RULES
    /// 1. Subscribe in OnEnable, unsubscribe in OnDisable. Always. A missed
    ///    unsubscribe survives scene loads and causes ghost callbacks on
    ///    destroyed objects, which are genuinely horrible to debug.
    /// 2. Never assume ordering between subscribers.
    /// 3. Publishers are named in the comment on each event. Do not raise an
    ///    event you do not own.
    /// 4. Use a named method as the handler, never a lambda — a lambda cannot be
    ///    unsubscribed, so rule 1 becomes impossible to honour.
    /// </summary>
    public static class SessionEvents
    {
        // Raised by SessionManager
        public static event Action<string> SessionStarted;          // sessionId
        public static event Action<RiskAssessment> SessionEnded;
        public static event Action<SessionPhase> PhaseChanged;

        // Raised by CountryProfileManager
        public static event Action<string> CountryProfileChanged;   // profileId

        // Raised by VRSeatRecenter
        public static event Action SeatRecentred;

        // Raised by ScenarioManager / HazardScenarioBase
        public static event Action<string> ScenarioArmed;           // scenarioId
        public static event Action<string> ScenarioTriggered;
        public static event Action<ScenarioResult> ScenarioResolved;

        // Raised by AlertManager's callers
        public static event Action<string, AlertPriority> AlertRaised;

        // Raised by VehicleController
        public static event Action<float> CollisionOccurred;        // impact speed kph

        // Raised by VehicleInputAdapter
        public static event Action<string> InputProviderChanged;    // description

        public static void RaiseSessionStarted(string id) => SessionStarted?.Invoke(id);
        public static void RaiseSessionEnded(RiskAssessment a) => SessionEnded?.Invoke(a);
        public static void RaisePhaseChanged(SessionPhase p) => PhaseChanged?.Invoke(p);
        public static void RaiseCountryProfileChanged(string id) => CountryProfileChanged?.Invoke(id);
        public static void RaiseSeatRecentred() => SeatRecentred?.Invoke();
        public static void RaiseScenarioArmed(string id) => ScenarioArmed?.Invoke(id);
        public static void RaiseScenarioTriggered(string id) => ScenarioTriggered?.Invoke(id);
        public static void RaiseScenarioResolved(ScenarioResult r) => ScenarioResolved?.Invoke(r);
        public static void RaiseAlert(string msg, AlertPriority p) => AlertRaised?.Invoke(msg, p);
        public static void RaiseCollision(float speedKph) => CollisionOccurred?.Invoke(speedKph);
        public static void RaiseInputProviderChanged(string n) => InputProviderChanged?.Invoke(n);

        /// <summary>
        /// Called from SessionManager.OnDestroy so an editor domain reload does
        /// not leave stale subscribers between Play sessions.
        /// </summary>
        public static void ClearAll()
        {
            SessionStarted = null; SessionEnded = null; PhaseChanged = null;
            CountryProfileChanged = null; SeatRecentred = null;
            ScenarioArmed = null; ScenarioTriggered = null; ScenarioResolved = null;
            AlertRaised = null; CollisionOccurred = null; InputProviderChanged = null;
        }
    }

    public enum AlertPriority { Info = 0, Warning = 1, Critical = 2 }
}
