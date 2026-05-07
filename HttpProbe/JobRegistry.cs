using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HttpProbe
{
    internal enum JobStatus { Running, Completed, Failed, Killed }

    internal class JobInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public JobStatus Status { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public string LastError { get; set; }
        public string LastResult { get; set; }
    }

    internal static class JobRegistry
    {
        private static readonly Dictionary<string, JobEntry> _jobs = new Dictionary<string, JobEntry>();
        private static readonly object _lock = new object();
        private static long _seq;

        // Spawn a long-running task on a background thread. The task receives a CancellationToken
        // that fires when the job is killed via DELETE /jobs/{id}.
        public static JobInfo Spawn(string name, Func<CancellationToken, Task<string>> work)
        {
            string id = Interlocked.Increment(ref _seq).ToString();
            var cts = new CancellationTokenSource();

            var info = new JobInfo
            {
                Id = id,
                Name = name ?? "(unnamed)",
                Status = JobStatus.Running,
                StartedAtUtc = DateTime.UtcNow,
            };

            var entry = new JobEntry { Info = info, Cts = cts };

            lock (_lock) _jobs[id] = entry;

            _ = Task.Run(async () =>
            {
                try
                {
                    string result = await work(cts.Token);
                    lock (_lock)
                    {
                        info.LastResult = result;
                        info.Status = cts.IsCancellationRequested ? JobStatus.Killed : JobStatus.Completed;
                        info.FinishedAtUtc = DateTime.UtcNow;
                    }
                }
                catch (OperationCanceledException)
                {
                    lock (_lock)
                    {
                        info.Status = JobStatus.Killed;
                        info.FinishedAtUtc = DateTime.UtcNow;
                    }
                }
                catch (Exception e)
                {
                    lock (_lock)
                    {
                        info.Status = JobStatus.Failed;
                        info.LastError = e.ToString();
                        info.FinishedAtUtc = DateTime.UtcNow;
                    }
                    Plugin.Log.LogError($"[HttpProbe] Job '{name}' ({id}) crashed: {e}");
                }
            }, cts.Token);

            return info;
        }

        public static List<JobInfo> List()
        {
            lock (_lock) return _jobs.Values.Select(e => e.Info).ToList();
        }

        public static JobInfo Get(string id)
        {
            lock (_lock) return _jobs.TryGetValue(id, out var e) ? e.Info : null;
        }

        public static bool Kill(string id)
        {
            JobEntry entry;
            lock (_lock) { if (!_jobs.TryGetValue(id, out entry)) return false; }
            try { entry.Cts.Cancel(); } catch { /* nbd */ }
            return true;
        }

        public static int RemoveCompleted()
        {
            lock (_lock)
            {
                var done = _jobs.Where(kv => kv.Value.Info.Status != JobStatus.Running).Select(kv => kv.Key).ToList();
                foreach (var k in done) _jobs.Remove(k);
                return done.Count;
            }
        }

        // Kill everything; called on plugin unload.
        public static void KillAll()
        {
            List<JobEntry> snapshot;
            lock (_lock) snapshot = _jobs.Values.ToList();
            foreach (var e in snapshot)
            {
                try { e.Cts.Cancel(); } catch { /* nbd */ }
            }
        }

        private class JobEntry
        {
            public JobInfo Info;
            public CancellationTokenSource Cts;
        }
    }
}
