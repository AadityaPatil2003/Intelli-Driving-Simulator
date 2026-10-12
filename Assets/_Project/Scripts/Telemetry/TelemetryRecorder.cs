using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using IDS.Core;

namespace IDS.Telemetry
{
    /// <summary>
    /// Samples the vehicle at a fixed rate and writes CSV. Physics runs at 50 Hz;
    /// logging is at 20 Hz — enough to resolve a braking reaction, cheap enough
    /// not to matter on the device.
    ///
    /// Writes are buffered and flushed periodically. A per-sample
    /// File.AppendAllText on Android costs frames and shows up as mysterious
    /// hitching during a hazard, which is the worst possible moment.
    ///
    /// FILES: Application.persistentDataPath/Sessions/&lt;sessionId&gt;.csv, with a
    /// matching _events.csv. Pull them off the headset with
    ///     adb pull /sdcard/Android/data/&lt;package&gt;/files/Sessions
    /// </summary>
    public class TelemetryRecorder : MonoBehaviour, ITelemetrySink
    {
        [Header("Sampling")]
        [Range(5, 50)]
        [SerializeField] private int sampleRateHz = 20;
        [SerializeField] private int flushEverySamples = 100;

        [Header("Sources (auto-resolved if empty)")]
        [SerializeField] private MonoBehaviour vehicleRef;   // IVehicleState
        [SerializeField] private MonoBehaviour laneRef;      // ILaneReference
        [SerializeField] private Transform headTransform;
        [SerializeField] private MirrorCheckDetector mirrorDetector;
        [SerializeField] private CountryProfileHolder profileHolder;

        public bool IsRecording { get; private set; }
        public string CurrentSessionId { get; private set; } = "";

        /// In-memory copy for the FeatureExtractor. Cleared at session start.
        public List<TelemetrySample> Samples { get; } = new();
        public List<TelemetryEvent> Events { get; } = new();

        /// <summary>Where the last session's CSV was written. Shown in the report.</summary>
        public string LastFilePath { get; private set; } = "";

        private IVehicleState _vehicle;
        private ILaneReference _lane;

        private float _sampleInterval;
        private float _sinceLastSample;
        private float _sessionStartTime;
        private string _filePath;
        private readonly StringBuilder _buffer = new();
        private int _unflushed;
        private string _activeScenarioId = "";
        private bool _collisionThisSample;

        private void Awake()
        {
            _sampleInterval = 1f / Mathf.Max(1, sampleRateHz);
            ServiceRegistry.Register<ITelemetrySink>(this);
            ServiceRegistry.Register(this);
        }

        private void Start()
        {
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Require<IVehicleState>("vehicle");
            _lane = laneRef as ILaneReference ?? ServiceRegistry.Require<ILaneReference>("lane reference");

            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;

            mirrorDetector ??= ServiceRegistry.Resolve<MirrorCheckDetector>();
            profileHolder ??= ServiceRegistry.Resolve<CountryProfileHolder>();

            if (_lane == null)
                Debug.LogWarning("[Telemetry] no ILaneReference — lane_offset will be " +
                                 "zero in every row and lane_offset_rms will be " +
                                 "meaningless. Do not report a lane-keeping score " +
                                 "from such a session.");
        }

        private void OnEnable()
        {
            SessionEvents.SessionStarted += OnSessionStarted;
            SessionEvents.SessionEnded += OnSessionEnded;
            SessionEvents.ScenarioTriggered += OnScenarioTriggered;
            SessionEvents.ScenarioResolved += OnScenarioResolved;
            SessionEvents.CollisionOccurred += OnCollision;
        }

        private void OnDisable()
        {
            SessionEvents.SessionStarted -= OnSessionStarted;
            SessionEvents.SessionEnded -= OnSessionEnded;
            SessionEvents.ScenarioTriggered -= OnScenarioTriggered;
            SessionEvents.ScenarioResolved -= OnScenarioResolved;
            SessionEvents.CollisionOccurred -= OnCollision;
            if (IsRecording) StopSession();
        }

        private void OnApplicationPause(bool paused)
        {
            // Taking the headset off backgrounds the app. Flush, or the last
            // buffered samples are lost along with the session.
            if (paused && IsRecording) Flush();
        }

        // ---- session lifecycle ------------------------------------------------

        private void OnSessionStarted(string sessionId) => StartSession(sessionId);
        private void OnSessionEnded(RiskAssessment assessment) => StopSession();

        public void StartSession(string sessionId)
        {
            CurrentSessionId = sessionId;
            _sessionStartTime = Time.time;
            Samples.Clear();
            Events.Clear();
            _buffer.Clear();
            _unflushed = 0;
            _activeScenarioId = "";
            _collisionThisSample = false;
            _sinceLastSample = 0f;
            _vehicle?.ResetSessionCounters();

            string dir = Path.Combine(Application.persistentDataPath, "Sessions");

            try
            {
                Directory.CreateDirectory(dir);
                _filePath = Path.Combine(dir, $"{sessionId}.csv");
                File.WriteAllText(_filePath, TelemetrySample.CsvHeader + "\n");
            }
            catch (IOException e)
            {
                Debug.LogError($"[Telemetry] cannot open {dir} for writing: {e.Message}. " +
                               "The session will still run but nothing will be recorded.");
                _filePath = "";
                return;
            }

            IsRecording = true;
            LastFilePath = _filePath;

            Debug.Log($"[Telemetry] recording → {_filePath}");
            RecordEvent("recording_started", $"schema_v{TelemetrySample.SchemaVersion}");
        }

        public void StopSession()
        {
            if (!IsRecording) return;
            IsRecording = false;
            Flush();
            WriteEventLog();
            Debug.Log($"[Telemetry] stopped. {Samples.Count} samples, " +
                      $"{Events.Count} events → {_filePath}");
        }

        private void Update()
        {
            if (!IsRecording) return;

            _sinceLastSample += Time.deltaTime;
            if (_sinceLastSample < _sampleInterval) return;
            _sinceLastSample -= _sampleInterval;

            CaptureSample();
        }

        private void CaptureSample()
        {
            if (_vehicle == null) return;

            Vector3 pos = _vehicle.Position;

            float headYaw = 0f, headPitch = 0f;
            if (headTransform != null)
            {
                Vector3 local = Quaternion.Inverse(
                                    Quaternion.LookRotation(_vehicle.Forward, Vector3.up))
                                * headTransform.forward;
                headYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                headPitch = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            }

            var sample = new TelemetrySample
            {
                t = Time.time - _sessionStartTime,
                sessionId = CurrentSessionId,
                countryProfile = profileHolder != null ? profileHolder.ProfileId : "unknown",
                scenarioId = _activeScenarioId,
                posX = pos.x, posY = pos.y, posZ = pos.z,
                speedKph = _vehicle.CurrentSpeedKph,
                steeringInput = _vehicle.SteeringInput,
                brakeInput = _vehicle.BrakeInput,
                accelInput = _vehicle.AcceleratorInput,
                laneOffset = _lane?.GetSignedLaneOffset(pos) ?? 0f,
                headYaw = headYaw,
                headPitch = headPitch,
                hazardActive = !string.IsNullOrEmpty(_activeScenarioId),
                collision = _collisionThisSample,
                mirrorCheck = mirrorDetector != null && mirrorDetector.ConsumeCheckFlag()
            };

            _collisionThisSample = false;

            Samples.Add(sample);
            _buffer.Append(sample.ToCsvRow()).Append('\n');

            if (++_unflushed >= flushEverySamples) Flush();
        }

        private void Flush()
        {
            if (_buffer.Length == 0 || string.IsNullOrEmpty(_filePath)) return;
            try { File.AppendAllText(_filePath, _buffer.ToString()); }
            catch (IOException e) { Debug.LogError($"[Telemetry] write failed: {e.Message}"); }
            _buffer.Clear();
            _unflushed = 0;
        }

        // ---- events ----------------------------------------------------------

        public void RecordEvent(string eventName, string detail = "")
        {
            if (!IsRecording && eventName != "recording_started") return;
            Events.Add(new TelemetryEvent
            {
                t = Time.time - _sessionStartTime,
                name = eventName,
                detail = detail
            });
        }

        private void OnScenarioTriggered(string scenarioId)
        {
            _activeScenarioId = scenarioId;
            RecordEvent("hazard_triggered", scenarioId);
        }

        private void OnScenarioResolved(ScenarioResult r)
        {
            // InvariantCulture throughout: these strings are parsed back by the
            // feature extractor and read by analysis scripts outside Unity. On a
            // comma-decimal locale, "0,84" would not parse as a number in either
            // place, and the reaction time would silently become "no response".
            CultureInfo c = CultureInfo.InvariantCulture;

            RecordEvent("hazard_resolved",
                $"{r.ScenarioId}|react={r.ReactionTime.ToString("F2", c)}" +
                $"|collision={(r.Collision ? 1 : 0)}");

            _activeScenarioId = "";
        }

        private void OnCollision(float impactKph)
        {
            _collisionThisSample = true;
            RecordEvent("collision", impactKph.ToString("F1", CultureInfo.InvariantCulture));
        }

        private void WriteEventLog()
        {
            if (string.IsNullOrEmpty(_filePath)) return;

            string path = _filePath.Replace(".csv", "_events.csv");
            var sb = new StringBuilder("t,event,detail\n");
            foreach (TelemetryEvent e in Events)
                sb.Append($"{e.t:F3},{e.name},{e.detail}\n");

            try { File.WriteAllText(path, sb.ToString()); }
            catch (IOException ex) { Debug.LogError($"[Telemetry] event log failed: {ex.Message}"); }
        }

        public void Configure(MonoBehaviour vehicle, MonoBehaviour lane, Transform head,
                              MirrorCheckDetector mirrors, CountryProfileHolder holder)
        {
            vehicleRef = vehicle;
            laneRef = lane;
            headTransform = head;
            mirrorDetector = mirrors;
            profileHolder = holder;
        }
    }

    public struct TelemetryEvent
    {
        public float t;
        public string name;
        public string detail;
    }
}
