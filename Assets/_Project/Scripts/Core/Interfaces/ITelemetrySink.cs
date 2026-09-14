namespace IDS.Core
{
    /// <summary>
    /// How other streams push discrete events into the telemetry log. Continuous
    /// channels are pulled by the recorder itself; this is for things that happen
    /// at a moment in time (hazard triggered, collision, alert shown).
    ///
    /// OWNER: Anushka (implementation). Called by Omkar's scenarios and alerts.
    /// </summary>
    public interface ITelemetrySink
    {
        /// <param name="eventName">Short stable token, e.g. "hazard_triggered".</param>
        /// <param name="detail">Free-form; keep it CSV-safe (no commas).</param>
        void RecordEvent(string eventName, string detail = "");

        bool IsRecording { get; }
        string CurrentSessionId { get; }
    }
}
