using System.Collections.Generic;
using UnityEngine;
using TMPro;
using IDS.Core;

namespace IDS.UI
{
    /// <summary>
    /// In-drive alerts. The hard part is not showing them, it is not showing too
    /// many: an alert that fires for every small error becomes noise the learner
    /// stops reading, at which point the alert system is worse than nothing.
    ///
    /// Three defences:
    ///   1. per-message cooldown — the same alert cannot repeat quickly
    ///   2. global minimum spacing — no alert within N seconds of any other
    ///   3. priority — a Critical alert can interrupt an Info one, never the reverse
    ///
    /// OWNER: Omkar.
    /// </summary>
    public class AlertManager : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private GameObject alertRoot;
        [SerializeField] private TMP_Text alertText;
        [SerializeField] private float displayDuration = 2.2f;

        [Header("Rate limiting")]
        [Tooltip("Same message cannot repeat within this many seconds.")]
        [SerializeField] private float perMessageCooldown = 8f;
        [Tooltip("No alert at all within this many seconds of the previous one.")]
        [SerializeField] private float globalSpacing = 3f;

        [Header("Colours by priority")]
        [SerializeField] private Color infoColour = new Color(0.85f, 0.9f, 1f);
        [SerializeField] private Color warningColour = new Color(1f, 0.8f, 0.3f);
        [SerializeField] private Color criticalColour = new Color(1f, 0.35f, 0.3f);

        private readonly Dictionary<string, float> _lastShown = new();
        private float _lastAnyAlertAt = -99f;
        private float _hideAt = -1f;
        private AlertPriority _currentPriority = AlertPriority.Info;

        /// Count of suppressed alerts, useful in the report: "the system wanted to
        /// warn 40 times and showed 6" is a finding about the alert design.
        public int SuppressedCount { get; private set; }
        public int ShownCount { get; private set; }

        private void Awake()
        {
            Hide();
            ServiceRegistry.Register(this);
        }

        private void OnEnable()
        {
            SessionEvents.AlertRaised += OnAlertRaised;
            SessionEvents.SessionStarted += OnSessionStarted;
        }

        private void OnDisable()
        {
            SessionEvents.AlertRaised -= OnAlertRaised;
            SessionEvents.SessionStarted -= OnSessionStarted;
        }

        private void OnSessionStarted(string _)
        {
            _lastShown.Clear();
            SuppressedCount = ShownCount = 0;
        }

        private void Update()
        {
            if (_hideAt > 0f && Time.time >= _hideAt) Hide();
        }

        private void OnAlertRaised(string message, AlertPriority priority)
        {
            if (!ShouldShow(message, priority))
            {
                SuppressedCount++;
                return;
            }

            Show(message, priority);
        }

        private bool ShouldShow(string message, AlertPriority priority)
        {
            // A critical alert overrides spacing. If a pedestrian is in the road,
            // rate limiting is the wrong instinct.
            if (priority == AlertPriority.Critical) return true;

            if (Time.time - _lastAnyAlertAt < globalSpacing) return false;

            if (_lastShown.TryGetValue(message, out float last) &&
                Time.time - last < perMessageCooldown) return false;

            // Do not let a lower-priority alert stomp a visible higher one.
            if (_hideAt > 0f && priority < _currentPriority) return false;

            return true;
        }

        private void Show(string message, AlertPriority priority)
        {
            if (alertRoot != null) alertRoot.SetActive(true);
            if (alertText != null)
            {
                alertText.text = message;
                alertText.color = priority switch
                {
                    AlertPriority.Critical => criticalColour,
                    AlertPriority.Warning  => warningColour,
                    _                      => infoColour
                };
            }

            _currentPriority = priority;
            _hideAt = Time.time + displayDuration;
            _lastAnyAlertAt = Time.time;
            _lastShown[message] = Time.time;
            ShownCount++;

            ServiceRegistry.Resolve<ITelemetrySink>()?
                .RecordEvent("alert_shown", $"{priority}|{message}");
        }

        private void Hide()
        {
            if (alertRoot != null) alertRoot.SetActive(false);
            _hideAt = -1f;
            _currentPriority = AlertPriority.Info;
        }

        // Convenience methods for the standard alert set.
        public static void CheckMirror()  => SessionEvents.RaiseAlert("CHECK MIRROR", AlertPriority.Warning);
        public static void MaintainLane() => SessionEvents.RaiseAlert("MAINTAIN LANE", AlertPriority.Warning);
        public static void ReduceSpeed()  => SessionEvents.RaiseAlert("REDUCE SPEED", AlertPriority.Warning);
        public static void UnsafeGap()    => SessionEvents.RaiseAlert("UNSAFE GAP", AlertPriority.Critical);
    }
}
