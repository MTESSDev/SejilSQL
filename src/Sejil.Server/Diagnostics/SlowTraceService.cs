using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SejilSQL.Diagnostics
{
    /// <summary>
    /// Listens to the spans of the application (<see cref="Activity"/>) and turns the slow or failed requests into log events,
    /// so they reach the sinks already in place (SejilSQL included). A request that is fast and succeeds leaves nothing.
    /// <para>
    /// A request is the span that has no parent in this process (an incoming request, or an outgoing call made by a background job)
    /// and the spans below it. Its steps are kept in memory while it runs; when it ends, if it was slow or one of its steps failed,
    /// ONE event is written with the whole tree (<c>Trace</c> property), otherwise they are forgotten.
    /// </para>
    /// <para>
    /// Every event carries <c>TraceId</c>: the other services that took part in the request (the ones the call went to) log their
    /// own side with the same id, so searching <c>TraceId = '...'</c> in the viewer puts the two sides together.
    /// </para>
    /// </summary>
    internal sealed class SlowTraceService : IHostedService, IDisposable
    {
        // What is held in memory for the requests that are running: a bound on each, and on all.
        private const int MaxSpansPerTrace = 200;
        private const int MaxTraces = 5000;
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

        private readonly SejilTraceOptions _options;
        private readonly ILogger _logger;
        private readonly InFlightTracker _inFlight;
        private readonly ConcurrentDictionary<string, TraceBuffer> _buffers = new ConcurrentDictionary<string, TraceBuffer>();
        private ActivityListener _listener;
        private long _stops;

        public SlowTraceService(SejilTraceOptions options, ILoggerFactory loggerFactory, InFlightTracker inFlight = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger("SejilSQL.Traces");
            _inFlight = inFlight ?? new InFlightTracker();
        }

        /// <summary>Requests whose steps are held in memory right now.</summary>
        internal int BufferedTraces => _buffers.Count;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var sources = new HashSet<string>(_options.ActivitySources, StringComparer.Ordinal);

            _listener = new ActivityListener
            {
                ShouldListenTo = source => sources.Contains(source.Name),
                // The duration is only known at the end, so every span is recorded; only the slow or failed requests are kept.
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity => _inFlight.Started(activity),
                ActivityStopped = OnActivityStopped,
            };
            ActivitySource.AddActivityListener(_listener);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            var listener = Interlocked.Exchange(ref _listener, null);
            listener?.Dispose();
        }

        internal void OnActivityStopped(Activity activity)
        {
            try
            {
                _inFlight.Stopped(activity);

                if (activity.Parent != null)
                {
                    Remember(activity);
                    return;
                }

                // No parent in this process: the request is over, with everything that ran under it.
                var dropped = 0;
                var steps = _buffers.TryRemove(activity.SpanId.ToString(), out var buffer)
                    ? buffer.Snapshot(out dropped)
                    : new List<SpanRecord>();

                var rootFailed = _options.LogErrors && activity.Status == ActivityStatusCode.Error;
                var failed = rootFailed || (_options.LogErrors && steps.Any(s => s.Failed));
                var slow = activity.Duration >= _options.SlowThreshold;
                if (!failed && !slow)
                {
                    return;
                }

                if (_options.Filter != null && !_options.Filter(activity))
                {
                    return;
                }

                Write(activity, steps, dropped, failed);
            }
            catch
            {
                // Called by the code that ends the span: it must never throw into the application.
            }
        }

        // A step of a request that is still running: kept until the request ends.
        private void Remember(Activity activity)
        {
            var root = activity;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            if (root.IsStopped)
            {
                return; // a step that outlived its request: nobody will read it
            }

            var key = root.SpanId.ToString();
            if (_buffers.Count >= MaxTraces && !_buffers.ContainsKey(key))
            {
                PurgeStale();
                if (_buffers.Count >= MaxTraces)
                {
                    return; // too many requests at once: this one just gets no tree
                }
            }

            _buffers.GetOrAdd(key, _ => new TraceBuffer()).Add(SpanRecord.From(activity), MaxSpansPerTrace);

            if (Interlocked.Increment(ref _stops) % 2000 == 0)
            {
                PurgeStale();
            }
        }

        // A request whose root never ended (an abandoned span) would stay in memory for ever.
        private void PurgeStale()
        {
            var limit = DateTime.UtcNow - StaleAfter;
            foreach (var entry in _buffers)
            {
                if (entry.Value.CreatedUtc < limit)
                {
                    _buffers.TryRemove(entry.Key, out _);
                }
            }
        }

        private void Write(Activity root, List<SpanRecord> steps, int dropped, bool failed)
        {
            var scope = new Dictionary<string, object>
            {
                ["TraceId"] = root.TraceId.ToString(),
                ["SpanId"] = root.SpanId.ToString(),
                ["SpanKind"] = root.Kind.ToString(),
            };

            if (root.ParentSpanId != default)
            {
                scope["ParentSpanId"] = root.ParentSpanId.ToString();
            }

            foreach (var tag in root.TagObjects)
            {
                if (tag.Value != null && _options.Tags.Contains(tag.Key))
                {
                    scope[tag.Key] = tag.Value;
                }
            }

            var rootRecord = SpanRecord.From(root);
            var slowest = TraceTree.Slowest(steps);

            if (steps.Count > 0)
            {
                scope["SpanCount"] = steps.Count + dropped;
                scope["Trace"] = TraceTree.Render(rootRecord, steps, _options.TraceTreeMaxLines);
            }

            var durationMs = Math.Round(root.Duration.TotalMilliseconds);

            using (_logger.BeginScope(scope))
            {
                if (failed)
                {
                    var reason = root.Status == ActivityStatusCode.Error
                        ? root.StatusDescription
                        : "a step failed: " + steps.First(s => s.Failed).Name;
                    _logger.LogError("Failed {Operation:l} after {DurationMs} ms: {StatusDescription:l}", rootRecord.Name, durationMs, reason);
                }
                else if (slowest != null)
                {
                    _logger.LogWarning("Slow {Operation:l} took {DurationMs} ms; slowest step: {SlowestStep:l} {SlowestMs} ms",
                        rootRecord.Name, durationMs, slowest.Name, Math.Round(slowest.Duration.TotalMilliseconds));
                }
                else
                {
                    _logger.LogWarning("Slow {Operation:l} took {DurationMs} ms", rootRecord.Name, durationMs);
                }
            }
        }

        private sealed class TraceBuffer
        {
            private readonly object _lock = new object();
            private readonly List<SpanRecord> _spans = new List<SpanRecord>();
            private int _dropped;

            public DateTime CreatedUtc { get; } = DateTime.UtcNow;

            public void Add(SpanRecord span, int max)
            {
                lock (_lock)
                {
                    if (_spans.Count < max)
                    {
                        _spans.Add(span);
                    }
                    else
                    {
                        _dropped++;
                    }
                }
            }

            public List<SpanRecord> Snapshot(out int dropped)
            {
                lock (_lock)
                {
                    dropped = _dropped;
                    return new List<SpanRecord>(_spans);
                }
            }
        }
    }
}
