using System;
using System.Globalization;
using UnityEngine;

namespace IDS.Telemetry
{
    /// <summary>
    /// One row of the session CSV. The schema is frozen at Milestone 2 — Anushka's
    /// classifier trains on it, so adding or reordering a column silently breaks
    /// every previously recorded session. If a column must be added, add it at the
    /// END and bump SchemaVersion.
    ///
    /// OWNER: Anushka.
    /// </summary>
    [Serializable]
    public struct TelemetrySample
    {
        public const int SchemaVersion = 1;

        public float  t;              // seconds since session start
        public string sessionId;
        public string countryProfile;
        public string scenarioId;     // "" when no hazard is active

        public float  posX, posY, posZ;
        public float  speedKph;

        public float  steeringInput;  // -1..1
        public float  brakeInput;     // 0..1
        public float  accelInput;     // 0..1

        public float  laneOffset;     // metres, +right of centreline
        public float  headYaw;        // degrees relative to vehicle forward
        public float  headPitch;      // degrees

        public bool   hazardActive;
        public bool   collision;
        public bool   mirrorCheck;    // true on the frame a check is registered

        public static string CsvHeader =>
            "t,session_id,country_profile,scenario_id," +
            "pos_x,pos_y,pos_z,speed_kph," +
            "steering_input,brake_input,accel_input," +
            "lane_offset,head_yaw,head_pitch," +
            "hazard_active,collision,mirror_check";

        public string ToCsvRow()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(",",
                t.ToString("F3", c),
                Sanitise(sessionId),
                Sanitise(countryProfile),
                Sanitise(scenarioId),
                posX.ToString("F3", c), posY.ToString("F3", c), posZ.ToString("F3", c),
                speedKph.ToString("F2", c),
                steeringInput.ToString("F4", c),
                brakeInput.ToString("F4", c),
                accelInput.ToString("F4", c),
                laneOffset.ToString("F4", c),
                headYaw.ToString("F2", c),
                headPitch.ToString("F2", c),
                hazardActive ? "1" : "0",
                collision ? "1" : "0",
                mirrorCheck ? "1" : "0");
        }

        // Commas and newlines in a field would corrupt the row. Cheaper and safer
        // than full CSV quoting, and no field here legitimately needs a comma.
        private static string Sanitise(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace(",", ";").Replace("\n", " ").Trim();
    }
}
