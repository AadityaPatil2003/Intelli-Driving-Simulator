#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using IDS.Core;
using IDS.AI;

namespace IDS.EditorTools
{
    /// <summary>
    /// Creates the three country profile assets with sensible starting values so
    /// nobody has to fill in eleven fields by hand three times. Run once:
    /// Tools → Intelli-Driving → Create Country Profiles.
    ///
    /// Anushka owns the values afterwards — this only bootstraps them.
    ///
    /// OWNER: Aaditya (tooling) / Anushka (values).
    /// </summary>
    public static class CountryProfileCreator
    {
        private const string Folder = "Assets/_Project/Data/CountryProfiles";

        [MenuItem("Tools/Intelli-Driving/Create Country Profiles")]
        public static void CreateAll()
        {
            EnsureFolder();

            Create("Australia", p =>
            {
                p.profileId = "australia";
                p.displayName = "Australia (VIC)";
                p.trafficSide = TrafficSide.Left;
                p.driverSeatSide = TrafficSide.Right;
                p.priorityMirror = TrafficSide.Left;
                p.roundaboutGiveWaySide = TrafficSide.Right;
                p.turnOnRedAllowed = false;
                p.hookTurnsEnabled = true;
                p.tramRulesEnabled = true;
                p.defaultSpeedLimitKph = 50f;
                p.expectedMirrorChecksPerMinute = 6f;
                p.safeGapSeconds = 4f;
                p.keyDifferencesBlurb =
                    "Traffic keeps left, driver sits right. Give way to the right " +
                    "at roundabouts. Hook turns and trams in central Melbourne. " +
                    "No turn on red.";
            });

            Create("India", p =>
            {
                p.profileId = "india";
                p.displayName = "India";
                p.trafficSide = TrafficSide.Left;
                p.driverSeatSide = TrafficSide.Right;
                p.priorityMirror = TrafficSide.Left;
                p.roundaboutGiveWaySide = TrafficSide.Right;
                p.turnOnRedAllowed = false;
                p.hookTurnsEnabled = false;
                p.tramRulesEnabled = false;
                p.defaultSpeedLimitKph = 50f;
                // Lower expectation reflects a different mirror-use convention,
                // not a judgement about drivers. Scoring a driver against a
                // baseline from another country is exactly the mistake this
                // project exists to avoid.
                p.expectedMirrorChecksPerMinute = 4f;
                p.safeGapSeconds = 3f;
                p.keyDifferencesBlurb =
                    "Traffic keeps left, driver sits right. Lane discipline is " +
                    "informal and mixed. No hook turns or tram rules.";
            });

            Create("USA", p =>
            {
                p.profileId = "usa";
                p.displayName = "USA / UAE";
                p.trafficSide = TrafficSide.Right;
                p.driverSeatSide = TrafficSide.Left;
                p.priorityMirror = TrafficSide.Right;
                p.roundaboutGiveWaySide = TrafficSide.Left;
                p.turnOnRedAllowed = true;
                p.hookTurnsEnabled = false;
                p.tramRulesEnabled = false;
                p.defaultSpeedLimitKph = 55f;
                p.expectedMirrorChecksPerMinute = 6f;
                p.safeGapSeconds = 4f;
                p.keyDifferencesBlurb =
                    "Traffic keeps right, driver sits left. Give way to the left " +
                    "at roundabouts. Right turn on red is generally permitted. " +
                    "Strict lane discipline.";
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CountryProfileCreator] wrote 3 profiles to {Folder}");
        }

        private static void Create(string fileName, System.Action<CountryProfile> configure)
        {
            string path = $"{Folder}/{fileName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<CountryProfile>(path);
            if (existing != null)
            {
                Debug.Log($"[CountryProfileCreator] {fileName}.asset already exists, " +
                          "leaving it alone. Delete it first if you want a reset.");
                return;
            }

            var profile = ScriptableObject.CreateInstance<CountryProfile>();
            configure(profile);
            AssetDatabase.CreateAsset(profile, path);
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data"))
                AssetDatabase.CreateFolder("Assets/_Project", "Data");
            AssetDatabase.CreateFolder("Assets/_Project/Data", "CountryProfiles");
        }
    }
}
#endif
