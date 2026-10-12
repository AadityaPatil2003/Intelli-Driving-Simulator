using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using IDS.Core;
using IDS.XR;

namespace IDS.Telemetry
{
    /// <summary>
    /// Records what actually happened during the drive and prints it on the
    /// report screen, where the driver is standing still and can read it.
    ///
    /// The HUD diagnostic strip was the wrong answer to the same problem: it is
    /// live, so reading it means looking away from the road, and capturing it
    /// means taking a screenshot while driving, which nobody can do. Everything
    /// that matters for debugging is a PEAK or a LIST, and both survive to the
    /// end of the drive perfectly well.
    ///
    /// What it answers, in one line, without anybody having to reproduce
    /// anything:
    ///
    ///   peak throttle 0.00  ->  the controller input never reached the car
    ///   peak throttle 1.00, peak speed 0  ->  input fine, physics wrong
    ///   steering provider "none"  ->  VRSteeringWheel never registered
    ///   armed: (none)  ->  the scenario queue never started
    ///   armed: cyclist, triggered: (none)  ->  armed but never met its
    ///                                          trigger conditions
    /// </summary>
    public class SessionDiagnostics : MonoBehaviour
    {
        [SerializeField] private TMP_Text output;

        [Tooltip("Shows the diagnostics line on the report. OFF for the demo. "
                 + "The same line still goes to the Unity console every drive, "
                 + "so nothing is lost by hiding it.")]
        [SerializeField] private bool enabledOnReport = false;

        private VehicleInputAdapter _input;
        private IVehicleState _vehicle;

        private readonly List<string> _armed = new();
        private readonly List<string> _triggered = new();

        private float _peakThrottle;
        private float _peakBrake;
        private float _peakSteer;
        private float _peakSpeed;
        private string _steerProvider = "none";
        private string _pedalProvider = "none";
        private bool _recording;

        private void Awake() => ServiceRegistry.Register(this);

        private void OnEnable()
        {
            SessionEvents.PhaseChanged += OnPhaseChanged;
            SessionEvents.ScenarioArmed += OnArmed;
            SessionEvents.ScenarioTriggered += OnTriggered;
            SessionEvents.SessionEnded += OnSessionEnded;
        }

        private void OnDisable()
        {
            SessionEvents.PhaseChanged -= OnPhaseChanged;
            SessionEvents.ScenarioArmed -= OnArmed;
            SessionEvents.ScenarioTriggered -= OnTriggered;
            SessionEvents.SessionEnded -= OnSessionEnded;
        }

        private void OnArmed(string id)
        {
            if (!_armed.Contains(id)) _armed.Add(id);
        }

        private void OnTriggered(string id)
        {
            if (!_triggered.Contains(id)) _triggered.Add(id);
        }

        private void OnPhaseChanged(SessionPhase phase)
        {
            bool driving = phase == SessionPhase.Driving
                           || phase == SessionPhase.HazardActive;

            if (driving && !_recording) Reset();
            _recording = driving;
        }

        private void Reset()
        {
            _armed.Clear();
            _triggered.Clear();
            _peakThrottle = _peakBrake = _peakSteer = _peakSpeed = 0f;
            _steerProvider = _pedalProvider = "none";
        }

        private void Update()
        {
            if (!_recording) return;

            _input ??= ServiceRegistry.Resolve<VehicleInputAdapter>();
            _vehicle ??= ServiceRegistry.Resolve<IVehicleState>();

            if (_input != null)
            {
                if (_input.Accelerator > _peakThrottle) _peakThrottle = _input.Accelerator;
                if (_input.Brake > _peakBrake) _peakBrake = _input.Brake;

                float steer = Mathf.Abs(_input.Steering);
                if (steer > _peakSteer) _peakSteer = steer;

                _steerProvider = _input.ActiveSteeringProvider;
                _pedalProvider = _input.ActivePedalProvider;
            }

            if (_vehicle != null)
            {
                float kph = Mathf.Abs(_vehicle.CurrentSpeedKph);
                if (kph > _peakSpeed) _peakSpeed = kph;
            }
        }

        private void OnSessionEnded(RiskAssessment assessment) => Render();

        private void Render()
        {
            if (output == null) return;

            var sb = new StringBuilder();

            sb.Append($"peak throttle {_peakThrottle:0.00}   ");
            sb.Append($"brake {_peakBrake:0.00}   ");
            sb.Append($"steer {_peakSteer:0.00}   ");
            sb.Append($"peak speed {_peakSpeed:0} km/h\n");

            sb.Append($"input: steer [{_steerProvider}]  pedals [{_pedalProvider}]\n");

            sb.Append("armed: ");
            sb.Append(_armed.Count > 0 ? string.Join(", ", _armed) : "(none)");
            sb.Append("    triggered: ");
            sb.Append(_triggered.Count > 0 ? string.Join(", ", _triggered) : "(none)");

            string line = sb.ToString();

            // Always logged, shown only if asked. The console copy is what makes
            // a failed drive diagnosable after the fact without putting debug
            // text in front of a participant.
            Debug.Log("[Diagnostics] " + line.Replace("\n", " | "));

            output.text = enabledOnReport ? line : "";
        }

        public void Configure(TMP_Text text) => output = text;
    }
}
