using UnityEngine;
using IDS.Core;

namespace IDS.Telemetry
{
    /// <summary>
    /// Holds the current country profile's id as a plain string.
    ///
    /// A deliberate indirection: the telemetry layer needs a profile id to write
    /// into every CSV row, and nothing else about country profiles. Referencing
    /// IDS.AI.CountryProfile for that would couple the recorder to the scoring
    /// layer's data model, so a change there would ripple into the file format.
    ///
    /// One of these goes anywhere in the scene.
    /// </summary>
    public class CountryProfileHolder : MonoBehaviour
    {
        public string ProfileId { get; private set; } = "unknown";

        private void Awake() => ServiceRegistry.Register(this);

        private void OnEnable() => SessionEvents.CountryProfileChanged += Set;
        private void OnDisable() => SessionEvents.CountryProfileChanged -= Set;

        private void Set(string id) => ProfileId = string.IsNullOrEmpty(id) ? "unknown" : id;
    }
}
