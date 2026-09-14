using System;
using IDS.Core;

namespace IDS.Core
{
    /// <summary>
    /// The only place streams are allowed to shout at each other. Using a static
    /// event bus rather than direct references means Omkar's UI can be built and
    /// tested before Anushka's scorer exists, and vice versa.
    ///
    /// RULES
    /// 1. Subscribe in OnEnable, unsubscribe in OnDisable. Always. A missed
    ///    unsubscribe survives scene loads and causes ghost callbacks that are
    ///    genuinely horrible to debug.
    /// 2. Never assume ordering between subscribers.
    /// 3. Publishers are named in the comment on each event. Do not raise an
    ///    event you do not own.
    /// </summary>
    public static class SessionEvents
    {
        // Raised by SessionManager (Aaditya)
        public static event Action<string> SessionStarted;          // sessionId
        public static event Action<RiskAssessment> SessionEnded;
        public static event Action<SessionPhase> PhaseChanged;

        // Raised by CountryProfileManager (Anushka's data, Aaditya's manager)
        public static event Action<string> CountryProfileChanged;   // profileId

        // Raised by CalibrationManager (Aaditya)
        public static event Action CalibrationCompleted;

        // Raised by ScenarioManager (Omkar)
        public static event Action<string> ScenarioArmed;           // scenarioId
        public static event Action<string> ScenarioTriggered;
        public static event Action<ScenarioResult> ScenarioResolved;

        // Raised by AlertManager (Omkar)
        public static event Action<string, AlertPriority> AlertRaised;

        // Raised by VehicleController (Ananya)
        public static event Action<float> CollisionOccurred;        // impact speed kph

        // Raised by VehicleInputAdapter (Aaditya)
        public static event Action<string> InputProviderChanged;    // provider name

        public static void RaiseSessionStarted(string id) => SessionStarted?.Invoke(id);
        public static void RaiseSessionEnded(RiskAssessment a) => SessionEnded?.Invoke(a);
        public static void RaisePhaseChanged(SessionPhase p) => PhaseChanged?.Invoke(p);
        public static void RaiseCountryProfileChanged(string id) => CountryProfileChanged?.Invoke(id);
        public static void RaiseCalibrationCompleted() => CalibrationCompleted?.Invoke();
        public static void RaiseScenarioArmed(string id) => ScenarioArmed?.Invoke(id);
        public static void RaiseScenarioTriggered(string id) => ScenarioTriggered?.Invoke(id);
        public static void RaiseScenarioResolved(ScenarioResult r) => ScenarioResolved?.Invoke(r);
        public static void RaiseAlert(string msg, AlertPriority p) => AlertRaised?.Invoke(msg, p);
        public static void RaiseCollision(float speedKph) => CollisionOccurred?.Invoke(speedKph);
        public static void RaiseInputProviderChanged(string n) => InputProviderChanged?.Invoke(n);

        /// Call from SessionManager.OnDestroy in the editor so domain reload does
        /// not leave stale subscribers between Play sessions.
        public static void ClearAll()
        {
            SessionStarted = null; SessionEnded = null; PhaseChanged = null;
            CountryProfileChanged = null; CalibrationCompleted = null;
            ScenarioArmed = null; ScenarioTriggered = null; ScenarioResolved = null;
            AlertRaised = null; CollisionOccurred = null; InputProviderChanged = null;
        }
    }

    public enum AlertPriority { Info = 0, Warning = 1, Critical = 2 }
}
