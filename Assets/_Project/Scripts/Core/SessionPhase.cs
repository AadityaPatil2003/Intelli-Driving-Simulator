namespace IDS.Core
{
    /// <summary>
    /// The session state machine. Owned by SessionManager; UIManager switches
    /// panels on it and nothing else is allowed to decide what is on screen.
    ///
    /// The order is the required flow:
    ///
    ///     Boot → Welcome → CountrySelection → Recenter → Ready
    ///          → Driving ⇄ HazardActive → Analysing → Report → Complete
    ///          → (Welcome again, or Ready again via Drive Again)
    ///
    /// Recenter replaces what used to be Calibration. In the mixed-reality build
    /// calibration meant aligning the virtual cabin to a physical wheel prop. In
    /// a full VR build there is no prop: the only thing that needs aligning is
    /// the driver's seated head pose against the virtual driving position, which
    /// is a one-button recentre the driver can repeat at any time.
    /// </summary>
    public enum SessionPhase
    {
        Boot,              // app launched, XR coming up
        Welcome,           // title screen
        CountrySelection,  // Australia / India / USA-UAE
        Recenter,          // sit straight, look forward, press Recenter
        Ready,             // in the cabin, instructions shown, waiting to drive
        Driving,           // telemetry recording, hazards armed
        HazardActive,      // a scenario is playing out
        Analysing,         // feature extraction + scoring
        Report,            // post-drive scores and recommendation
        Complete           // recommended next session, restart or exit
    }
}
