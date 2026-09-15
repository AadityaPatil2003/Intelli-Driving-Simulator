using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using IDS.Core;

namespace IDS.Telemetry
{
    /// <summary>
    /// Samples the vehicle at a fixed rate and writes CSV. Physics runs at 50 Hz
    /// but we log at 20 Hz — enough to resolve a braking reaction, cheap enough
    /// not to matter on the device.
    ///
    /// Writes are buffered and flushed periodically rather than every sample.
    /// A per-sample File.AppendAllText on Android costs frames and is the kind of
    /// thing that shows up as mysterious hitching during a hazard.
    ///
    /// FILES: Application.persistentDataPath/Sessions/session_<timestamp>.csv
    /// Pull them off the device with adb (see 03_QUEST_DEPLOYMENT.md).
    ///
    /// OWNER: Anushka.
    /// </summary>
    public class TelemetryRecorder : MonoBehaviour, ITelemetrySink
    {
        [Header("Sampling")]
        [Range(5, 50)]
        [SerializeField] private int sampleRateHz = 20;
        [SerializeField] private int flushEverySamples = 100;

        [Header("Sources (auto-resolved if empty)")]
        [SerializeField] private MonoBehaviour vehicleRef;      // IVehicleState
        [SerializeField] private MonoBehaviour laneRef;         // ILaneReference
        [SerializeField] private Transform headTransform;
        [SerializeField] private MirrorCheckDetector mirrorDetector;

        public bool IsRecording { get; private set; }
        public string CurrentSessionId { get; private set; } = "";

        /// In-memory copy for the FeatureExtractor. Cleared at session start.
        public List<TelemetrySample> Samples { get; } = new();
        public List<TelemetryEvent> Events { get; } = new();

        private IVehicleState _vehicle;
        private ILaneReference _lane;
        private CountryProfileHolder _profile;

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
            _vehicle = vehicleRef as IVehicleState ?? ServiceRegistry.Require<IVehicleState>("Ananya");
            _lane    = laneRef as ILaneReference   ?? ServiceRegistry.Require<ILaneReference>("Ananya");
            if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
            mirrorDetector ??= GetComponent<MirrorCheckDetector>();
            _profile ??= FindFirstObjectByType<CountryProfileHolder>();
        }

        private void OnEnable()
        {
            SessionEvents.SessionStarted    += OnSessionStarted;
            SessionEvents.SessionEnded      += OnSessionEnded;
            SessionEvents.ScenarioTriggered += OnScenarioTriggered;
            SessionEvents.ScenarioResolved  += OnScenarioResolved;
            SessionEvents.CollisionOccurred += OnCollision;
        }

        private void OnDisable()
        {
            SessionEvents.SessionStarted    -= OnSessionStarted;
            SessionEvents.SessionEnded      -= OnSessionEnded;
            SessionEvents.ScenarioTriggered -= OnScenarioTriggered;
            SessionEvents.ScenarioResolved  -= OnScenarioResolved;
            SessionEvents.CollisionOccurred -= OnCollision;
            if (IsRecording) StopSession();
        }

        // ---- session lifecycle ----------------------------------------------

        private void OnSessionStarted(string sessionId) => StartSession(sessionId);
        private void OnSessionEnded(RiskAssessment _) => StopSession();

        public void StartSession(string sessionId)
        {
            CurrentSessionId = sessionId;
            _sessionStartTime = Time.time;
            Samples.Clear();
            Events.Clear();
            _buffer.Clear();
            _unflushed = 0;
            _activeScenarioId = "";
            _vehicle?.ResetSessionCounters();

            string dir = Path.Combine(Application.persistentDataPath, "Sessions");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, $"{sessionId}.csv");

            File.WriteAllText(_filePath, TelemetrySample.CsvHeader + "\n");
            IsRecording = true;

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
                countryProfile = _profile?.ProfileId ?? "unknown",
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
            RecordEvent("hazard_resolved",
                $"{r.ScenarioId}|react={r.ReactionTime:F2}|collision={r.Collision}");
            _activeScenarioId = "";
        }

        private void OnCollision(float impactKph)
        {
            _collisionThisSample = true;
            RecordEvent("collision", impactKph.ToString("F1"));
        }

        private void WriteEventLog()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path = _filePath.Replace(".csv", "_events.csv");
            var sb = new StringBuilder("t,event,detail\n");
            foreach (var e in Events)
                sb.Append($"{e.t:F3},{e.name},{e.detail}\n");
            try { File.WriteAllText(path, sb.ToString()); }
            catch (IOException ex) { Debug.LogError($"[Telemetry] event log failed: {ex.Message}"); }
        }

        public void SetProfileHolder(CountryProfileHolder holder) => _profile = holder;
    }

    public struct TelemetryEvent
    {
        public float t;
        public string name;
        public string detail;
    }

    /// <summary>
    /// Tiny indirection so the telemetry code does not have to know about the AI
    /// stream's CountryProfile type just to write a profile id into the CSV.
    /// Put one of these anywhere in the scene.
    /// </summary>
    public class CountryProfileHolder : MonoBehaviour
    {
        public string ProfileId { get; private set; } = "unknown";

        private void OnEnable()  => SessionEvents.CountryProfileChanged += Set;
        private void OnDisable() => SessionEvents.CountryProfileChanged -= Set;

        private void Set(string id) => ProfileId = id;
    }
}
