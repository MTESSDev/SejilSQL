using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SejilSQL.Diagnostics
{
    /// <summary>
    /// Listens to the spans of the application (<see cref="Activity"/>) and turns the slow or failed ones into log events,
    /// so they reach the sinks already in place (SejilSQL included). A span that is fast and succeeds is dropped: nothing is stored for it.
    /// <para>
    /// Every event carries <c>TraceId</c>, <c>SpanId</c> and <c>ParentSpanId</c>: searching <c>TraceId = '...'</c> in the viewer
    /// gives the slow steps of one request together.
    /// </para>
    /// </summary>
    internal sealed class SlowTraceService : IHostedService, IDisposable
    {
        private readonly SejilTraceOptions _options;
        private readonly ILogger _logger;
        private ActivityListener _listener;

        public SlowTraceService(SejilTraceOptions options, ILoggerFactory loggerFactory)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger("SejilSQL.Traces");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var sources = new HashSet<string>(_options.ActivitySources, StringComparer.Ordinal);

            _listener = new ActivityListener
            {
                ShouldListenTo = source => sources.Contains(source.Name),
                // The duration is only known at the end, so every span is recorded; only the slow or failed ones are kept.
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
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
                var failed = _options.LogErrors && activity.Status == ActivityStatusCode.Error;
                var slow = activity.Duration >= _options.SlowThreshold;
                if (!failed && !slow)
                {
                    return;
                }

                if (_options.Filter != null && !_options.Filter(activity))
                {
                    return;
                }

                var scope = new Dictionary<string, object>
                {
                    ["TraceId"] = activity.TraceId.ToString(),
                    ["SpanId"] = activity.SpanId.ToString(),
                    ["SpanKind"] = activity.Kind.ToString(),
                };

                if (activity.ParentSpanId != default)
                {
                    scope["ParentSpanId"] = activity.ParentSpanId.ToString();
                }

                foreach (var tag in activity.TagObjects)
                {
                    if (tag.Value != null && _options.Tags.Contains(tag.Key))
                    {
                        scope[tag.Key] = tag.Value;
                    }
                }

                var durationMs = Math.Round(activity.Duration.TotalMilliseconds);

                using (_logger.BeginScope(scope))
                {
                    if (failed)
                    {
                        _logger.LogError("Failed {Operation} after {DurationMs} ms: {StatusDescription}", activity.DisplayName, durationMs, activity.StatusDescription);
                    }
                    else
                    {
                        _logger.LogWarning("Slow {Operation} took {DurationMs} ms", activity.DisplayName, durationMs);
                    }
                }
            }
            catch
            {
                // Called by the code that ends the span: it must never throw into the application.
            }
        }
    }
}
