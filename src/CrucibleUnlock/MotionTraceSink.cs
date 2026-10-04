using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace CrucibleUnlock
{
    // Only copied managed values may cross to the writer thread. No Unity objects,
    // IL2CPP wrappers, native pointers, or Frame references belong in these records.
    public sealed class MotionVec3
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
    }

    public sealed class MotionTraceRecord
    {
        public string kind { get; set; }
        public long seq { get; set; }
        public string utc { get; set; }
        public double elapsed_ms { get; set; }
        public long entity_raw { get; set; }
        public int? unity_frame { get; set; }
        public int? frame_number { get; set; }
        public int? discontinuous_frame { get; set; }
        public bool? teleport_flag { get; set; }
        public bool? transform_was_synced_previous_frame { get; set; }
        public int? phase { get; set; }
        public MotionVec3 root { get; set; }
        public MotionVec3 visual { get; set; }
        public MotionVec3 avatar { get; set; }
        public MotionVec3 pelvis { get; set; }
        public MotionVec3 pelvis_local { get; set; }
        public MotionVec3 skeleton { get; set; }
        public MotionVec3 skeleton_local { get; set; }
        public MotionVec3 animator { get; set; }
        public MotionVec3 interpolated { get; set; }
        public MotionVec3 previous_ideal { get; set; }
        public MotionVec3 position_error { get; set; }
        public MotionVec3 root_velocity { get; set; }
        public MotionVec3 raw_position { get; set; }
        public MotionPresentationSnapshot presentation { get; set; }
        public MotionActionSnapshot animation_state { get; set; }
        public string base_action { get; set; }
        public string layer_action { get; set; }
        public double? base_action_time { get; set; }
        public double? layer_action_time { get; set; }
        public string note { get; set; }
    }

    public sealed class MotionTraceSink : IDisposable
    {
        private readonly BlockingCollection<MotionTraceRecord> _queue;
        private readonly TextWriter _writer;
        private readonly Thread _thread;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        private string _error;
        private int _closing;
        private long _sequence, _written, _dropped;
        public string Error => Volatile.Read(ref _error);
        public long WrittenCount => Interlocked.Read(ref _written);
        public long DroppedCount => Interlocked.Read(ref _dropped);

        public MotionTraceSink(string absoluteOutputFile) : this(OpenNew(absoluteOutputFile), 8192) { }

        // Internal injection is used by offline tests for blocked / failed file I/O.
        internal MotionTraceSink(TextWriter writer, int capacity)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _queue = new BlockingCollection<MotionTraceRecord>(capacity);
            _thread = new Thread(WriteLoop) { IsBackground = true, Name = "NRFW boss motion log writer" };
            _thread.Start();
        }

        private static TextWriter OpenNew(string path)
        {
            if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("Trace path must be absolute.");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 65536);
            return new StreamWriter(stream, new UTF8Encoding(false), 65536);
        }

        public MotionTraceRecord NewRecord(string kind, long entityRaw = 0) => new MotionTraceRecord
        {
            kind = kind, utc = DateTime.UtcNow.ToString("O"), elapsed_ms = _clock.Elapsed.TotalMilliseconds,
            entity_raw = entityRaw
        };

        // Never wait for disk or queue capacity on a game callback. A record must
        // not be changed by its producer after this method has accepted it.
        public bool TryWrite(MotionTraceRecord record)
        {
            if (record == null) return false;
            if (Volatile.Read(ref _closing) != 0 || Error != null)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            record.seq = Interlocked.Increment(ref _sequence);
            try
            {
                if (_queue.TryAdd(record)) return true;
            }
            catch (InvalidOperationException) { }
            Interlocked.Increment(ref _dropped);
            return false;
        }

        private void WriteLoop()
        {
            var flush = Stopwatch.StartNew();
            try
            {
                while (!_queue.IsCompleted)
                {
                    if (_queue.TryTake(out var record, 100))
                    {
                        _writer.WriteLine(JsonSerializer.Serialize(record, _json));
                        Interlocked.Increment(ref _written);
                    }
                    if (flush.ElapsedMilliseconds >= 250)
                    {
                        _writer.Flush(); flush.Restart();
                    }
                }
                _writer.Flush();
            }
            catch (Exception error)
            {
                Volatile.Write(ref _error, error.GetType().Name + ": " + error.Message);
            }
            finally
            {
                try { _writer.Dispose(); }
                catch (Exception error) { Volatile.Write(ref _error, error.GetType().Name + ": " + error.Message); }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closing, 1) != 0) return;
            _queue.CompleteAdding();
            // Bounded shutdown: a stalled disk must not freeze the game exit path.
            if (!_thread.Join(1500)) Volatile.Write(ref _error, "Writer did not finish within the shutdown window.");
        }
    }
}
