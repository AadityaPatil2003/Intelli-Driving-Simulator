using UnityEngine;
using IDS.Core;

namespace IDS.AI
{
    /// <summary>
    /// One road system, as data. Nothing in the codebase may hard-code Australian
    /// behaviour — scripts ask the current profile. This ScriptableObject is what
    /// makes "one codebase, both road systems" true rather than a slide claim.
    ///
    /// Create the three assets via Tools → Intelli-Driving → Create Country Profiles.
    ///
    /// OWNER: Anushka (values), consumed by Ananya (environment) and Omkar (UI).
    /// </summary>
    [CreateAssetMenu(fileName = "CountryProfile",
        menuName = "Intelli-Driving/Country Profile", order = 0)]
    public class CountryProfile : ScriptableObject
    {
        [Header("Identity")]
        public string profileId = "australia";
        public string displayName = "Australia (VIC)";

        [Header("Geometry")]
        [Tooltip("Which side of the road traffic drives on.")]
        public TrafficSide trafficSide = TrafficSide.Left;

        [Tooltip("Which side of the cabin the driver sits on. Normally the " +
                 "opposite of trafficSide, but stored separately so an odd " +
                 "configuration can be represented.")]
        public TrafficSide driverSeatSide = TrafficSide.Right;

        [Tooltip("The mirror that matters most for blind-spot checks in this " +
                 "system. For left-side traffic with a right-side driver, that is " +
                 "the LEFT mirror — the kerb side, where cyclists are.")]
        public TrafficSide priorityMirror = TrafficSide.Left;

        [Header("Rules")]
        [Tooltip("At a roundabout, give way to traffic coming from this side.")]
        public TrafficSide roundaboutGiveWaySide = TrafficSide.Right;

        public bool turnOnRedAllowed = false;
        public bool hookTurnsEnabled = false;
        public bool tramRulesEnabled = false;

        [Header("Speed")]
        public float defaultSpeedLimitKph = 50f;

        [Header("Scoring adjustments")]
        [Tooltip("Expected mirror checks per minute for a well-adapted driver in " +
                 "this system. Used to normalise mirror_check_freq — a driver " +
                 "arriving from a low-mirror-discipline culture should not be " +
                 "scored against a different country's baseline.")]
        public float expectedMirrorChecksPerMinute = 6f;

        [Tooltip("Minimum gap in seconds considered safe at an unsignalised " +
                 "junction in this system.")]
        public float safeGapSeconds = 4f;

        [Header("Presentation")]
        [TextArea(2, 4)]
        public string keyDifferencesBlurb =
            "Traffic keeps left. Give way to the right at roundabouts. " +
            "Hook turns and trams in central Melbourne.";

        /// <summary>
        /// Which lateral direction counts as "drifting toward the familiar lane"
        /// for a driver arriving from the OPPOSITE system. Used by the report to
        /// say something more useful than "you drifted".
        /// </summary>
        public float FamiliarDriftSign =>
            trafficSide == TrafficSide.Left ? +1f : -1f;
    }
}
