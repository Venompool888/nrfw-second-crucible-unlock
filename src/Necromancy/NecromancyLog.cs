using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private static readonly ConcurrentQueue<string> Pending = new();
        private static readonly object LogWriterLock = new();
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private static string _logPath;
        private static StreamWriter _logWriter;
        private static bool _logWriterFailed;
        private static long _lastLogFlushTick;

        private static void InitLog(string userDataDirectory)
        {
            _logPath = Path.Combine(userDataDirectory, "NRFWNecromancy.log");
            _logWriterFailed = false;
            _lastLogFlushTick = Environment.TickCount64;
        }

        private static void Write(string text)
        {
            if (Pending.Count < 512) Pending.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
        }

        private static void Flush(bool force = false)
        {
            if (Pending.IsEmpty)
            {
                if (force) FlushWriter();
                return;
            }

            int maxBatch = force ? int.MaxValue : 64;
            var lines = new List<string>();
            while (lines.Count < maxBatch && Pending.TryDequeue(out var line))
            {
                lines.Add(line);
                MelonLogger.Msg("[necromancy] " + line);
            }

            if (lines.Count == 0) return;

            lock (LogWriterLock)
            {
                if (_logWriterFailed || string.IsNullOrEmpty(_logPath)) return;
                try
                {
                    if (_logWriter == null)
                    {
                        var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        _logWriter = new StreamWriter(stream, Utf8NoBom) { AutoFlush = false };
                    }
                    foreach (string line in lines) _logWriter.WriteLine(line);
                    long now = Environment.TickCount64;
                    if (force || lines.Count >= 64 || now - _lastLogFlushTick >= 1000)
                    {
                        _logWriter.Flush();
                        _lastLogFlushTick = now;
                    }
                }
                catch
                {
                    _logWriterFailed = true;
                    try { _logWriter?.Dispose(); } catch { }
                    _logWriter = null;
                }
            }
        }

        private static void FlushWriter()
        {
            lock (LogWriterLock)
            {
                if (_logWriter == null) return;
                try
                {
                    _logWriter.Flush();
                    _lastLogFlushTick = Environment.TickCount64;
                }
                catch
                {
                    _logWriterFailed = true;
                    try { _logWriter.Dispose(); } catch { }
                    _logWriter = null;
                }
            }
        }

        private static void CloseLog()
        {
            Flush(force: true);
            lock (LogWriterLock)
            {
                try { _logWriter?.Flush(); } catch { }
                try { _logWriter?.Dispose(); } catch { }
                _logWriter = null;
            }
        }
    }
}
