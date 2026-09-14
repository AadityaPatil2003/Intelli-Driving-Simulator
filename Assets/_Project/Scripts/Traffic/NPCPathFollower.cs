using UnityEngine;

namespace IDS.Traffic
{
    /// <summary>
    /// Simple point-to-point mover for pedestrians and cyclists. Ananya provides
    /// the movement; Omkar decides when it starts and what counts as success.
    ///
    /// Deliberately not a NavMesh agent: the paths are three metres of straight
    /// line and a NavMesh bake is one more thing to break on the device.
    ///
    /// OWNER: Ananya. Consumer: Omkar (pedestrian + cyclist scenarios).
    /// </summary>
    public class NPCPathFollower : MonoBehaviour
    {
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField] private float speedMetresPerSec = 1.4f;   // walking pace
        [SerializeField] private bool faceDirectionOfTravel = true;
        [SerializeField] private bool stopAtEnd = true;

        public bool IsMoving { get; private set; }
        public bool HasArrived { get; private set; }
        public float SpeedMetresPerSec
        {
            get => speedMetresPerSec;
            set => speedMetresPerSec = Mathf.Max(0f, value);
        }

        private void Awake() => SnapToStart();

        private void Update()
        {
            if (!IsMoving || endPoint == null) return;

            Vector3 target = endPoint.position;
            Vector3 toTarget = target - transform.position;

            if (toTarget.magnitude < 0.05f)
            {
                HasArrived = true;
                if (stopAtEnd) IsMoving = false;
                return;
            }

            Vector3 dir = toTarget.normalized;
            transform.position += dir * (speedMetresPerSec * Time.deltaTime);

            if (faceDirectionOfTravel && dir.sqrMagnitude > 0.001f)
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            }
        }

        public void Begin()
        {
            HasArrived = false;
            IsMoving = true;
        }

        public void Halt() => IsMoving = false;

        public void SnapToStart()
        {
            IsMoving = false;
            HasArrived = false;
            if (startPoint != null) transform.position = startPoint.position;
        }

        /// <summary>Distance from an arbitrary point, for proximity scoring.</summary>
        public float DistanceTo(Vector3 worldPos) =>
            Vector3.Distance(transform.position, worldPos);

        private void OnDrawGizmosSelected()
        {
            if (startPoint == null || endPoint == null) return;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(startPoint.position, endPoint.position);
            Gizmos.DrawWireSphere(startPoint.position, 0.3f);
            Gizmos.DrawWireSphere(endPoint.position, 0.3f);
        }
    }
}
