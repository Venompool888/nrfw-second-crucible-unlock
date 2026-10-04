using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using CrucibleUnlock;

internal static class MotionTraceSinkTests
{
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception("FAIL: " + description);
    }

    public static int Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "nr fw motion tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "trace.jsonl");
            var sink = new MotionTraceSink(file);
            for (int i = 0; i < 30; i++)
            {
                var record = sink.NewRecord("sample", 19);
                record.root = new MotionVec3 { x = i, y = 2, z = -3 };
                record.note = "unicode 起身\nnewline";
                Check(sink.TryWrite(record), "normal sample accepted");
            }
            sink.Dispose();
            Check(sink.Error == null && sink.WrittenCount == 30, "shutdown drains accepted samples");
            string[] lines = File.ReadAllLines(file);
            Check(lines.Length == 30, "one JSON document per line including embedded newlines");
            for (int i = 0; i < lines.Length; i++)
            {
                using var json = JsonDocument.Parse(lines[i]);
                Check(json.RootElement.GetProperty("seq").GetInt64() == i + 1, "sequence order");
                Check(json.RootElement.GetProperty("root").GetProperty("x").GetSingle() == i, "coordinates serialized without game types");
            }
            Check(!sink.TryWrite(sink.NewRecord("late")), "writes after close rejected");
            bool overwriteRejected = false;
            try { using var duplicate = new MotionTraceSink(file); }
            catch (IOException) { overwriteRejected = true; }
            Check(overwriteRejected, "existing log cannot be overwritten");

            var failing = new MotionTraceSink(new FailingWriter(), 4);
            failing.TryWrite(failing.NewRecord("sample"));
            Check(SpinWait.SpinUntil(() => failing.Error != null, 2000), "disk failure visible as health state");
            Check(!failing.TryWrite(failing.NewRecord("sample")), "I/O failure rejects later records without throwing");
            failing.Dispose();

            var blockedWriter = new BlockingWriter();
            var bounded = new MotionTraceSink(blockedWriter, 2);
            bounded.TryWrite(bounded.NewRecord("sample"));
            Check(blockedWriter.Entered.Wait(2000), "writer stall reached");
            Check(bounded.TryWrite(bounded.NewRecord("sample")), "first queued sample");
            Check(bounded.TryWrite(bounded.NewRecord("sample")), "second queued sample");
            Check(!bounded.TryWrite(bounded.NewRecord("sample")) && bounded.DroppedCount == 1, "full queue drops without blocking game callback");
            blockedWriter.Release.Set(); bounded.Dispose();
            Check(bounded.Error == null && bounded.WrittenCount == 3, "remaining queued samples preserved");
            return 12;
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class FailingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string value) => throw new IOException("synthetic disk failure");
    }

    private sealed class BlockingWriter : TextWriter
    {
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string value) { Entered.Set(); Release.Wait(); }
    }
}
