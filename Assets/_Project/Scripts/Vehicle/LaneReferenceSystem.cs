using System.Collections.Generic;
using UnityEngine;
using IDS.Core;

namespace IDS.Vehicle
{
    public class LaneReferenceSystem : MonoBehaviour, ILaneReference
    {
        [Tooltip("All drivable lane paths in the district. Auto-collected if empty.")]
        [SerializeField] private List<LanePath> lanePaths = new();

        private LanePath _cachedPath;

        private void Awake()
        {
            if (lanePaths.Count == 0)
                lanePaths.AddRange(GetComponentsInChildren<LanePath>(true));

            if (lanePaths.Count == 0)
                Debug.LogWarning("[LaneReference] no LanePath found — lane offset " +
                                 "will always read zero and lane_offset_rms will be " +
                                 "meaningless. Add a LanePath to the road.");

            ServiceRegistry.Register<ILaneReference>(this);
        }

        private LanePath FindBestPath(Vector3 worldPos, out Vector3 point,
                                      out Vector3 forward)
        {
            point = worldPos;
            forward = Vector3.forward;

            LanePath best = null;
            float bestDist = float.MaxValue;

            if (_cachedPath != null)
            {
                _cachedPath.GetClosest(worldPos, out var cp, out var cf, out _);
                bestDist = Vector3.SqrMagnitude(
                    new Vector3(worldPos.x - cp.x, 0f, worldPos.z - cp.z));
                best = _cachedPath; point = cp; forward = cf;

                if (bestDist < 9f) return best;
            }

            foreach (var path in lanePaths)
            {
                if (path == null || path == _cachedPath) continue;
                path.GetClosest(worldPos, out var p, out var f, out _);
                float d = Vector3.SqrMagnitude(
                    new Vector3(worldPos.x - p.x, 0f, worldPos.z - p.z));
                if (d >= bestDist) continue;
                bestDist = d; best = path; point = p; forward = f;
            }

            if (best != null) _cachedPath = best;
            return best;
        }

        public Vector3 GetNearestLanePoint(Vector3 worldPosition)
        {
            FindBestPath(worldPosition, out Vector3 point, out _);
            return point;
        }

        public float GetSignedLaneOffset(Vector3 worldPosition)
        {
            var path = FindBestPath(worldPosition, out Vector3 point, out Vector3 forward);
            if (path == null) return 0f;

            Vector3 toVehicle = worldPosition - point;
            toVehicle.y = 0f;

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return Vector3.Dot(toVehicle, right);
        }

        public Vector3 GetLaneForward(Vector3 worldPosition)
        {
            FindBestPath(worldPosition, out _, out Vector3 forward);
            return forward;
        }

        public bool IsOffRoad(Vector3 worldPosition)
        {
            var path = FindBestPath(worldPosition, out Vector3 point, out Vector3 forward);
            if (path == null) return false;

            Vector3 toVehicle = worldPosition - point;
            toVehicle.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return Mathf.Abs(Vector3.Dot(toVehicle, right)) > path.LaneHalfWidth;
        }
    }
}