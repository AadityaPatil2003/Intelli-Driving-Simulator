using System.Collections.Generic;
using UnityEngine;
using IDS.Core;

namespace IDS.UI
{
    /// <summary>
    /// Teaches the procedure instead of only marking it.
    ///
    /// This is a TRAINING simulator. A driver who moves out past a cyclist
    /// without a mirror check and is told so three minutes later, in a report,
    /// has learned almost nothing: they cannot remember what they were doing at
    /// the time, and nobody ever told them what the right sequence was. The
    /// scoring stays exactly as it is — what changes is that each hazard now
    /// says what it wants BEFORE it happens, cues the step AS it happens, and
    /// gives the verdict immediately AFTER, while the driver still remembers
    /// the moment.
    ///
    /// Three beats per hazard:
    ///
    ///   BRIEFING   on arm      "cyclist ahead — mirror, then move out"
    ///   CUE        on trigger  "check your left mirror now"
    ///   VERDICT    on resolve  "moved out with no mirror check" / "good check"
    ///
    /// COACHING OFF is a real mode, not a debug switch. For a measured
    /// assessment run — and for the user-testing baseline, where the whole point
    /// is to catch the habits a driver brought with them — the prompts have to
    /// be silent, or the simulator is measuring its own coaching. Set
    /// <see cref="Enabled"/> to false for that.
    /// </summary>
    public class DriveCoach : MonoBehaviour
    {
        [Tooltip("On for a training drive. OFF for a measured assessment or a "
                 + "user-testing baseline, where prompting the driver would "
                 + "contaminate the very habits being measured.")]
        [SerializeField] private bool coachingEnabled = true;

        [Tooltip("Minimum gap between two coaching lines, so a briefing and a "
                 + "cue a fraction of a second apart do not overwrite each other.")]
        [SerializeField] private float minSpacingSeconds = 1.6f;

        private readonly Queue<(string text, AlertPriority priority)> _pending = new();
        private float _nextAt;

        public bool Enabled
        {
            get => coachingEnabled;
            set => coachingEnabled = value;
        }

        private void Awake() => ServiceRegistry.Register(this);

        private void OnEnable() => SessionEvents.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => SessionEvents.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(SessionPhase phase)
        {
            if (phase == SessionPhase.Driving || phase == SessionPhase.HazardActive) return;
            _pending.Clear();
        }

        private void Update()
        {
            if (_pending.Count == 0 || Time.time < _nextAt) return;

            (string text, AlertPriority priority) = _pending.Dequeue();
            SessionEvents.RaiseAlert(text, priority);
            _nextAt = Time.time + minSpacingSeconds;
        }

        /// <summary>
        /// Queues a coaching line. Queued rather than raised immediately because
        /// a hazard can arm and trigger within the same second, and a verdict
        /// that replaces its own cue before the driver has read it is worse than
        /// no verdict at all.
        /// </summary>
        public void Say(string text, AlertPriority priority = AlertPriority.Info)
        {
            if (!coachingEnabled || string.IsNullOrWhiteSpace(text)) return;
            _pending.Enqueue((text, priority));
        }

        /// <summary>Verdicts jump the queue — they are the teachable moment.</summary>
        public void SayNow(string text, AlertPriority priority)
        {
            if (!coachingEnabled || string.IsNullOrWhiteSpace(text)) return;

            _pending.Clear();
            SessionEvents.RaiseAlert(text, priority);
            _nextAt = Time.time + minSpacingSeconds;
        }

        public static void Tell(string text, AlertPriority priority = AlertPriority.Info) =>
            ServiceRegistry.Resolve<DriveCoach>()?.Say(text, priority);

        public static void TellNow(string text, AlertPriority priority) =>
            ServiceRegistry.Resolve<DriveCoach>()?.SayNow(text, priority);
    }
}
