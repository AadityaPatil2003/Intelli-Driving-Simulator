namespace IDS.Core
{
    /// <summary>
    /// The session state machine from the master plan, section 13.
    /// Owned by SessionManager. Omkar's UIManager switches panels on this.
    /// </summary>
    public enum SessionPhase
    {
        Boot,              // app just launched, XR coming up
        Welcome,           // title screen
        CountrySelection,  // pick Australia / India / USA
        Calibration,       // position the wheel prop, confirm
        Ready,             // brief instructions, waiting for driver
        Driving,           // telemetry recording, hazards armed
        HazardActive,      // a scenario is playing out
        Analysing,         // feature extraction + scoring
        Report,            // post-drive dashboard
        Complete           // recommended next session shown
    }
}
