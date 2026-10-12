using UnityEngine;
using IDS.Core;
using IDS.AI;

namespace IDS.UI
{
    /// <summary>
    /// Briefs the driver on the junction before they reach it.
    ///
    /// The cross road at z 520 is a real intersection with real cross traffic,
    /// and up to now it had no sign, no give-way line and no warning — a car
    /// simply arrived from one side and the driver was scored on the gap they
    /// accepted. That is a trap, not a training exercise. Worse, it is the one
    /// hazard where the correct behaviour differs by country, which is the whole
    /// point of the project: the side the danger comes from, and the side you
    /// must look to first, flip with the traffic side.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class JunctionCoach : MonoBehaviour
    {
        [SerializeField] private CountryProfileManager countryProfiles;

        private bool _spoken;

        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnEnable() => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            if (phase == SessionPhase.Driving || phase == SessionPhase.HazardActive) return;
            _spoken = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_spoken) return;
            if (other.attachedRigidbody == null) return;
            if (other.attachedRigidbody.GetComponent<IVehicleState>() == null) return;

            _spoken = true;

            countryProfiles ??= ServiceRegistry.Resolve<CountryProfileManager>();
            CountryProfile profile = countryProfiles?.Current;

            // Left-hand traffic: the nearest conflicting vehicle reaches you from
            // the right. Right-hand traffic: from the left.
            string first = profile != null && profile.trafficSide == TrafficSide.Left
                ? "RIGHT" : "LEFT";

            DriveCoach.Tell("JUNCTION AHEAD — SLOW DOWN AND GIVE WAY");
            DriveCoach.Tell($"LOOK {first} FIRST, THEN BOTH WAYS", AlertPriority.Warning);
        }

        public void Configure(CountryProfileManager profiles) => countryProfiles = profiles;
    }
}
