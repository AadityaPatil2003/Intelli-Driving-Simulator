using System.Collections.Generic;
using UnityEngine;

namespace IDS.Vehicle
{
    public class LanePath : MonoBehaviour
    {
        [Tooltip("Ordered centreline points. Auto-collected from children if empty.")]
        [SerializeField] private List<Transform> waypoints = new();

        [Tooltip("Lane half-width in metres. Used for the off-road test.")]
        [SerializeField] private float laneHalfWidth = 1.75f;

        [Tooltip("Does this lane loop back to its first point?")]
        [SerializeField] private bool closedLoop;

        [Header("Gizmos")]
        [SerializeField] private Color gizmoColour = new Color(0.2f, 0.9f, 0.4f);

        public float LaneHalfWidth => laneHalfWidth;
        public bool  ClosedLoop => closedLoop;
        public int   Count => waypoints.Count;

        private void Awake()
        {
            if (waypoints.Count == 0) CollectChildren();
        }

        public void CollectChildren()
        {
            waypoints.Clear();
            foreach (Transform child in transform) waypoints.Add(child);
        }

        public Vector3 GetPoint(int index)
        {
            if (waypoints.Count == 0) return transform.position;
            index = closedLoop
                ? ((index % waypoints.Count) + waypoints.Count) % waypoints.Count
                : Mathf.Clamp(index, 0, waypoints.Count - 1);
            return waypoints[index] != null ? waypoints[index].position : transform.position;
        }

        public void GetClosest(Vector3 worldPos, out Vector3 closestPoint,
                               out Vector3 segmentForward, out int segmentIndex)
        {
            closestPoint = transform.position;
            segmentForward = transform.forward;
            segmentIndex = 0;

            if (waypoints.Count < 2) return;

            float best = float.MaxValue;
            int segments = closedLoop ? waypoints.Count : waypoints.Count - 1;

            for (int i = 0; i < segments; i++)
            {
                Vector3 a = GetPoint(i);
                Vector3 b = GetPoint(i + 1);
                Vector3 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 0.0001f) continue;

                float t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / lenSq);
                Vector3 p = a + ab * t;

                float d = new Vector2(worldPos.x - p.x, worldPos.z - p.z).sqrMagnitude;
                if (d >= best) continue;

                best = d;
                closestPoint = p;
                segmentForward = ab.normalized;
                segmentIndex = i;
            }
        }

        private void OnDrawGizmos()
        {
            if (waypoints.Count < 2) return;
            Gizmos.color = gizmoColour;

            int segments = closedLoop ? waypoints.Count : waypoints.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = GetPoint(i), b = GetPoint(i + 1);
                Gizmos.DrawLine(a, b);

                Vector3 dir = (b - a).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, dir) * laneHalfWidth;
                Gizmos.color = new Color(gizmoColour.r, gizmoColour.g, gizmoColour.b, 0.35f);
                Gizmos.DrawLine(a + side, b + side);
                Gizmos.DrawLine(a - side, b - side);
                Gizmos.color = gizmoColour;
            }

            foreach (var w in waypoints)
                if (w != null) Gizmos.DrawWireSphere(w.position, 0.25f);
        }
    }
}