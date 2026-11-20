using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SejilSQL.Diagnostics
{
    /// <summary>
    /// Detects thread pool starvation (requests piling up because every thread is busy or blocked) and logs it, and logs nothing
    /// while the pool is healthy.
    /// <para>
    /// It runs on a thread of its own, outside the pool, so it keeps working while the pool is starved. At each probe it queues
    /// a work item and times how long the pool takes to start it. A healthy pool takes a few milliseconds.
    /// </para>
    /// </summary>
    internal sealed class ThreadPoolWatcher : IHostedService, IDisposable
    {
        private readonly SejilTraceOptions _options;
        private readonly ILogger _logger;
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private Thread _thread;

        private readonly InFlightTracker _inFlight;

        public ThreadPoolWatcher(SejilTraceOptions options, ILoggerFactory loggerFactory, InFlightTracker inFlight = null)
        {
            _inFlight = inFlight;
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger("SejilSQL.ThreadPool");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_options.ThreadPoolWatcher && _thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "SejilSQL.ThreadPoolWatcher" };
                _thread.Start();
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _stop.Set();
            _thread?.Join(TimeSpan.FromSeconds(2));
            return Task.CompletedTask;
        }

        public void Dispose() => _stop.Dispose();

        private void Run()
        {
            var period = _options.ThreadPoolSamplePeriod;
            var consecutive = 0;
            var alertSince = (DateTime?)null;
            var lastAlert = DateTime.MinValue;

            while (!_stop.Wait(period))
            {
                try
                {
                    var latency = Probe(period);
                    var pending = ThreadPool.PendingWorkItemCount;
                    var symptom = latency >= _options.ThreadPoolLatencyThreshold || pending >= _options.ThreadPoolPendingThreshold;

                    if (symptom)
                    {
                        consecutive++;

                        if (consecutive >= _options.ThreadPoolConsecutiveSamples)
                        {
                            var now = DateTime.UtcNow;
                            if (alertSince == null || now - lastAlert >= _options.ThreadPoolRepeatEvery)
                            {
                                alertSince = alertSince ?? now;
                                lastAlert = now;
                                Report(LogLevel.Warning, "Thread pool starvation suspected: a work item waited {LatencyMs} ms for a thread, {PendingWorkItems} waiting", latency, pending, now - alertSince.Value);
                            }
                        }
                    }
                    else
                    {
                        if (alertSince != null)
                        {
                            Report(LogLevel.Information, "Thread pool back to normal after {StarvedForSeconds} s", latency, pending, DateTime.UtcNow - alertSince.Value);
                        }

                        consecutive = 0;
                        alertSince = null;
                    }
                }
                catch
                {
                    // A failed probe must not stop the watcher.
                }
            }
        }

        // Queues a work item to the pool and measures how long it waits for a thread. Returns the wait, capped at maxWait.
        private static TimeSpan Probe(TimeSpan maxWait)
        {
            var started = new ManualResetEventSlim(false);
            var clock = Stopwatch.StartNew();
            var waited = TimeSpan.Zero;

            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                waited = clock.Elapsed;
                started.Set();
            }, null);

            return started.Wait(maxWait) ? waited : maxWait;
        }

        private void Report(LogLevel level, string message, TimeSpan latency, long pending, TimeSpan duration)
        {
            ThreadPool.GetMinThreads(out var minWorkers, out _);
            ThreadPool.GetAvailableThreads(out var availableWorkers, out _);
            ThreadPool.GetMaxThreads(out var maxWorkers, out _);

            var scope = new System.Collections.Generic.Dictionary<string, object>
            {
                ["ThreadCount"] = ThreadPool.ThreadCount,
                ["MinWorkerThreads"] = minWorkers,
                ["BusyWorkerThreads"] = maxWorkers - availableWorkers,
                ["MaxWorkerThreads"] = maxWorkers,
                ["CompletedWorkItems"] = ThreadPool.CompletedWorkItemCount,
            };

            // The routes to look at first. They hold threads, or wait without holding any: the stack of the busy threads is what tells.
            if (level != LogLevel.Information && _inFlight?.Summary() is string inFlight)
            {
                scope["InFlightCount"] = _inFlight.Count;
                scope["InFlightRequests"] = inFlight;
            }

            using (_logger.BeginScope(scope))
            {
                if (level == LogLevel.Information)
                {
                    _logger.Log(level, message, Math.Round(duration.TotalSeconds));
                }
                else
                {
                    _logger.Log(level, message, Math.Round(latency.TotalMilliseconds), pending);
                }
            }
        }
    }
}
