using System.Collections.Generic;
using UnityEngine;

namespace IDS.Traffic
{
    /// <summary>
    /// A route for NPC vehicles. Separate from LanePath because traffic routes and
    /// the ego lane centreline are not the same thing — cross traffic at the
    /// intersection needs its own path, and the ego lane must not be polluted with
    /// waypoints that only exist for NPCs.
    ///
    /// OWNER: Ananya.
    /// </summary>
    public class TrafficPath : MonoBehaviour
    {
        [SerializeField] private List<Transform> waypoints = new();
        [SerializeField] private bool loop = true;
        [SerializeField] private float defaultSpeedKph = 35f;
        [SerializeField] private Color gizmoColour = new Color(0.9f, 0.6f, 0.2f);

        public bool Loop => loop;
        public float DefaultSpeedKph => defaultSpeedKph;
        public int Count => waypoints.Count;

        private void Awake()
        {
            if (waypoints.Count == 0)
                foreach (Transform c in transform) waypoints.Add(c);
        }

        public Vector3 GetWaypoint(int index)
        {
            if (waypoints.Count == 0) return transform.position;
            if (loop) index = ((index % waypoints.Count) + waypoints.Count) % waypoints.Count;
            else index = Mathf.Clamp(index, 0, waypoints.Count - 1);
            return waypoints[index].position;
        }

        public bool IsLastWaypoint(int index) => !loop && index >= waypoints.Count - 1;

        private void OnDrawGizmos()
        {
            if (waypoints.Count < 2) return;
            Gizmos.color = gizmoColour;
            int segs = loop ? waypoints.Count : waypoints.Count - 1;
            for (int i = 0; i < segs; i++)
                Gizmos.DrawLine(GetWaypoint(i), GetWaypoint(i + 1));
            foreach (var w in waypoints)
                if (w != null) Gizmos.DrawWireCube(w.position, Vector3.one * 0.4f);
        }
    }
}
