using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HttpProbe
{
    // Marshals work from background threads (HttpListener callbacks, jobs) onto Unity's main thread.
    // Unity APIs (and most game APIs that touch GameObjects / Hero / World) crash if called off-thread.
    internal class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> Queue = new Queue<Action>();
        private static readonly object QueueLock = new object();

        public static int MainThreadId { get; private set; } = -1;

        public static bool IsOnMainThread()
        {
            return Thread.CurrentThread.ManagedThreadId == MainThreadId;
        }

        // Run on main thread, return Task<T> that completes once the work is done (or throws).
        public static Task<T> Run<T>(Func<T> fn)
        {
            if (IsOnMainThread())
            {
                try { return Task.FromResult(fn()); }
                catch (Exception e)
                {
                    var failed = new TaskCompletionSource<T>();
                    failed.SetException(e);
                    return failed.Task;
                }
            }

            var tcs = new TaskCompletionSource<T>();
            lock (QueueLock)
            {
                Queue.Enqueue(() =>
                {
                    try { tcs.SetResult(fn()); }
                    catch (Exception e) { tcs.SetException(e); }
                });
            }
            return tcs.Task;
        }

        public static Task RunVoid(Action a)
        {
            return Run<bool>(() => { a(); return true; });
        }

        private void Awake()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void Update()
        {
            // Drain a snapshot to avoid holding the lock while running user code (which could re-enqueue).
            Action[] snapshot;
            lock (QueueLock)
            {
                if (Queue.Count == 0) return;
                snapshot = Queue.ToArray();
                Queue.Clear();
            }

            foreach (var a in snapshot)
            {
                try { a(); }
                catch (Exception e) { Plugin.Log.LogError($"[HttpProbe] main-thread task threw: {e}"); }
            }
        }
    }
}
