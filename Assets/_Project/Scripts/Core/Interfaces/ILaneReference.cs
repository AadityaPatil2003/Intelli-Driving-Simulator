using UnityEngine;

namespace IDS.Core
{
    /// <summary>
    /// Lane geometry query API. Implemented by Ananya's LaneReferenceSystem,
    /// consumed by Anushka's lane_offset_rms feature and by Omkar's cyclist
    /// scenario (to know which side is the kerb side).
    ///
    /// OWNER: Ananya (implementation).
    /// </summary>
    public interface ILaneReference
    {
        /// Closest point on the active lane centreline to a world position.
        Vector3 GetNearestLanePoint(Vector3 worldPosition);

        /// Lateral distance in metres from the lane centreline.
        /// Sign convention: POSITIVE = right of centreline in the direction of
        /// travel, NEGATIVE = left. This convention is fixed; do not flip it for
        /// left-hand traffic — the country profile handles that.
        float GetSignedLaneOffset(Vector3 worldPosition);

        /// Direction of travel of the lane at the nearest point. Used to resolve
        /// the sign above and to place hazards facing the right way.
        Vector3 GetLaneForward(Vector3 worldPosition);

        /// True if the position is beyond the lane surface entirely (off-road).
        bool IsOffRoad(Vector3 worldPosition);
    }
}
