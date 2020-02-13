using System;
using System.Collections.Concurrent;
using System.Threading;
using Robust.Shared.Exceptions;

namespace Robust.Shared.Asynchronous
{
    internal class RobustSynchronizationContext : SynchronizationContext
    {
        // Used only on release.
        // ReSharper disable once NotAccessedField.Local
        private readonly IRuntimeLog _runtimeLog;

        public RobustSynchronizationContext(IRuntimeLog runtimeLog)
        {
            _runtimeLog = runtimeLog;
        }

        private readonly BlockingCollection<(SendOrPostCallback d, object state)> _pending = new BlockingCollection<(SendOrPostCallback, object)>();

        public override void Send(SendOrPostCallback d, object state)
        {

            if (Current != this)
            {
                var cts = new CancellationTokenSource();
                try
                {
                    using var e = new ManualResetEventSlim(false, 0);
                    Post(_ =>
                    {
                        d(state);
                        // ReSharper disable once AccessToDisposedClosure
                        e.Set();
                    }, null);
                    e.Wait(cts.Token);
                }
                catch (ThreadAbortException)
                {
                    cts.Cancel();
                }
                catch (ThreadInterruptedException)
                {
                    cts.Cancel();
                }
            }

            d(state);
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            _pending.Add((d, state));
        }

        public void ProcessPendingTasks()
        {
            while (_pending.TryTake(out var task))
            {
#if RELEASE
                try
#endif
                {
                    task.d(task.state);
                }
#if RELEASE
                catch (Exception e)
                {
                    _runtimeLog.LogException(e, "Async Queued Callback");
                }
#endif
            }
        }
    }
}
