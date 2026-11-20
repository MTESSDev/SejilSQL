using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SejilSQL.Diagnostics;
using Xunit;

namespace SejilSQL.Test.Diagnostics
{
    public class SlowTraceServiceTests
    {
        private sealed class Captured
        {
            public LogLevel Level;
            public string Message;
            public Dictionary<string, object> Scope = new Dictionary<string, object>();
        }

        private sealed class CapturingFactory : ILoggerFactory, ILogger
        {
            public List<Captured> Events { get; } = new List<Captured>();
            private Dictionary<string, object> _scope = new Dictionary<string, object>();

            public ILogger CreateLogger(string categoryName) => this;
            public void AddProvider(ILoggerProvider provider) { }
            public void Dispose() { }
            public bool IsEnabled(LogLevel logLevel) => true;

            public IDisposable BeginScope<TState>(TState state)
            {
                _scope = ((IEnumerable<KeyValuePair<string, object>>)state).ToDictionary(p => p.Key, p => p.Value);
                return new Scope();
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => Events.Add(new Captured { Level = logLevel, Message = formatter(state, exception), Scope = _scope });

            private sealed class Scope : IDisposable { public void Dispose() { } }
        }

        private static (SlowTraceService Service, CapturingFactory Log, ActivitySource Source) Start(Action<SejilTraceOptions> configure = null)
        {
            var source = new ActivitySource("SejilTest." + Guid.NewGuid());
            var options = new SejilTraceOptions { SlowThreshold = TimeSpan.FromMilliseconds(500) };
            options.ActivitySources.Add(source.Name);
            configure?.Invoke(options);

            var log = new CapturingFactory();
            var service = new SlowTraceService(options, log);
            service.StartAsync(default).GetAwaiter().GetResult();
            return (service, log, source);
        }

        private static Activity StartSpan(ActivitySource source, TimeSpan age)
        {
            var activity = source.CreateActivity("GET /orders", ActivityKind.Server);
            activity.SetStartTime(DateTime.UtcNow - age);
            return activity.Start();
        }

        // A step of the request that is running (it hangs from the current span): an outgoing call that began `age` ago.
        private static Activity StartStep(ActivitySource source, string url, TimeSpan age, int status = 200, ActivityKind kind = ActivityKind.Client)
        {
            var step = source.CreateActivity("HTTP", kind);
            step.SetStartTime(DateTime.UtcNow - age);
            step.SetTag("http.request.method", "GET");
            step.SetTag("url.full", url);
            step.SetTag("http.response.status_code", status);
            return step.Start();
        }

        [Fact]
        public void A_slow_request_is_logged_once_with_the_tree_of_its_steps_and_their_times()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var request = StartSpan(source, TimeSpan.FromSeconds(2));
                StartStep(source, "http://inventory/api/stock?token=secret", TimeSpan.FromMilliseconds(1700)).Stop();
                StartStep(source, "http://payments/charge", TimeSpan.FromMilliseconds(600), status: 503).Stop();
                request.Stop();

                var e = Assert.Single(log.Events); // the steps are not logged on their own, even though one is slow too
                Assert.Equal(LogLevel.Warning, e.Level);
                Assert.Contains("GET /orders took", e.Message);
                Assert.Contains("slowest step: GET http://inventory/api/stock 17", e.Message);

                var tree = (string)e.Scope["Trace"];
                var lines = tree.Split('\n');
                Assert.StartsWith("GET /orders  20", lines[0]);                          // the request, ~2000 ms
                Assert.StartsWith("├─ GET http://inventory/api/stock  17", lines[1]);    // the first call, ~1700 ms
                Assert.StartsWith("└─ GET http://payments/charge  6", lines[2]);         // the second, ~600 ms
                Assert.Contains("[200]", lines[1]);
                Assert.Contains("[503]", lines[2]);
                Assert.Contains("(+3", lines[1]);                                         // started ~300 ms after the request
                Assert.DoesNotContain("token=secret", tree);                              // the query string never leaves the process
                Assert.Equal(2, e.Scope["SpanCount"]);
            }
        }

        [Fact]
        public void The_tree_follows_the_calls_that_make_calls()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var request = StartSpan(source, TimeSpan.FromSeconds(2));
                var a = StartStep(source, "http://a/", TimeSpan.FromMilliseconds(1800));
                StartStep(source, "http://b/", TimeSpan.FromMilliseconds(1500)).Stop();   // called by a
                a.Stop();
                request.Stop();

                var lines = ((string)Assert.Single(log.Events).Scope["Trace"]).Split('\n');
                Assert.Equal(3, lines.Length);
                Assert.StartsWith("└─ GET http://a/", lines[1]);
                Assert.StartsWith("   └─ GET http://b/", lines[2]);   // indented under a
            }
        }

        [Fact]
        public void A_fast_request_leaves_nothing_and_its_steps_are_forgotten()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var request = StartSpan(source, TimeSpan.Zero);
                StartStep(source, "http://inventory/", TimeSpan.Zero).Stop();
                Assert.Equal(1, service.BufferedTraces); // held while the request runs
                request.Stop();

                Assert.Empty(log.Events);
                Assert.Equal(0, service.BufferedTraces);
            }
        }

        [Fact]
        public void A_step_that_failed_makes_the_request_an_error_even_when_it_was_fast()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var request = StartSpan(source, TimeSpan.Zero);
                var step = StartStep(source, "http://payments/charge", TimeSpan.Zero, status: 500);
                step.SetStatus(ActivityStatusCode.Error, "HTTP 500");
                step.Stop();
                request.Stop();

                var e = Assert.Single(log.Events);
                Assert.Equal(LogLevel.Error, e.Level);
                Assert.Contains("a step failed: GET http://payments/charge", e.Message);
                Assert.Contains("FAILED", (string)e.Scope["Trace"]);
            }
        }

        [Fact]
        public void A_long_tree_is_cut_and_says_how_much_is_missing()
        {
            var (service, log, source) = Start(o => o.TraceTreeMaxLines = 5);
            using (service)
            {
                var request = StartSpan(source, TimeSpan.FromSeconds(2));
                for (var i = 0; i < 12; i++)
                {
                    StartStep(source, $"http://svc/{i}", TimeSpan.FromMilliseconds(100)).Stop();
                }

                request.Stop();

                var tree = (string)Assert.Single(log.Events).Scope["Trace"];
                Assert.Equal(6, tree.Split('\n').Length);   // the request, 4 steps, then the note
                Assert.EndsWith("… 8 more", tree);
                Assert.Equal(12, log.Events[0].Scope["SpanCount"]);
            }
        }

        [Fact]
        public void A_request_with_no_steps_has_no_tree()
        {
            var (service, log, source) = Start();
            using (service)
            {
                StartSpan(source, TimeSpan.FromSeconds(2)).Stop();

                var e = Assert.Single(log.Events);
                Assert.False(e.Scope.ContainsKey("Trace"));
                Assert.DoesNotContain("slowest step", e.Message);
            }
        }

        [Fact]
        public void A_step_that_ends_after_its_request_does_not_stay_in_memory()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var request = StartSpan(source, TimeSpan.Zero);
                var late = StartStep(source, "http://fire-and-forget/", TimeSpan.Zero);
                request.Stop(); // the request ends first
                late.Stop();

                Assert.Equal(0, service.BufferedTraces);
            }
        }

        [Fact]
        public void The_default_threshold_for_a_slow_operation_is_one_second()
        {
            Assert.Equal(TimeSpan.FromSeconds(1), new SejilTraceOptions().SlowThreshold);
        }

        [Fact]
        public void A_span_just_under_the_default_threshold_is_not_slow_and_one_just_over_is()
        {
            var (service, log, source) = Start(o => o.SlowThreshold = new SejilTraceOptions().SlowThreshold);
            using (service)
            {
                StartSpan(source, TimeSpan.FromMilliseconds(900)).Stop();
                Assert.Empty(log.Events);

                StartSpan(source, TimeSpan.FromMilliseconds(1100)).Stop();
                Assert.Single(log.Events);
            }
        }

        [Fact]
        public void A_fast_span_leaves_nothing()
        {
            var (service, log, source) = Start();
            using (service)
            {
                StartSpan(source, TimeSpan.Zero).Stop();

                Assert.Empty(log.Events);
            }
        }

        [Fact]
        public void A_slow_span_is_logged_as_a_warning_with_its_trace_id()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var span = StartSpan(source, TimeSpan.FromSeconds(2));
                var traceId = span.TraceId.ToString();
                span.Stop();

                var e = Assert.Single(log.Events);
                Assert.Equal(LogLevel.Warning, e.Level);
                Assert.Contains("GET /orders", e.Message);
                Assert.Equal(traceId, e.Scope["TraceId"]);
                Assert.Equal("Server", e.Scope["SpanKind"]);
                Assert.True(e.Scope.ContainsKey("SpanId"));
            }
        }

        [Fact]
        public void A_failed_span_is_logged_even_when_fast()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var span = StartSpan(source, TimeSpan.Zero);
                span.SetStatus(ActivityStatusCode.Error, "boom");
                span.Stop();

                var e = Assert.Single(log.Events);
                Assert.Equal(LogLevel.Error, e.Level);
                Assert.Contains("boom", e.Message);
            }
        }

        [Fact]
        public void Errors_can_be_turned_off()
        {
            var (service, log, source) = Start(o => o.LogErrors = false);
            using (service)
            {
                var span = StartSpan(source, TimeSpan.Zero);
                span.SetStatus(ActivityStatusCode.Error);
                span.Stop();

                Assert.Empty(log.Events);
            }
        }

        [Fact]
        public void Only_the_listed_tags_are_copied()
        {
            var (service, log, source) = Start();
            using (service)
            {
                var span = StartSpan(source, TimeSpan.FromSeconds(2));
                span.SetTag("http.route", "/orders/{id}");
                span.SetTag("db.statement", "SELECT secret FROM users");
                span.Stop();

                var e = Assert.Single(log.Events);
                Assert.Equal("/orders/{id}", e.Scope["http.route"]);
                Assert.False(e.Scope.ContainsKey("db.statement"));
            }
        }

        [Fact]
        public void The_filter_can_ignore_a_span()
        {
            var (service, log, source) = Start(o => o.Filter = a => a.GetTagItem("url.path") as string != "/log");
            using (service)
            {
                var ignored = StartSpan(source, TimeSpan.FromSeconds(2));
                ignored.SetTag("url.path", "/log");
                ignored.Stop();

                var kept = StartSpan(source, TimeSpan.FromSeconds(2));
                kept.SetTag("url.path", "/orders");
                kept.Stop();

                var e = Assert.Single(log.Events);
                Assert.Equal("/orders", e.Scope["url.path"]);
            }
        }

        [Fact]
        public void A_span_of_a_source_that_is_not_listed_is_ignored()
        {
            var (service, log, _) = Start();
            using (service)
            using (var other = new ActivitySource("SejilTest.Other." + Guid.NewGuid()))
            {
                var span = other.StartActivity("slow") ?? other.CreateActivity("slow", ActivityKind.Internal)?.Start();
                span?.Stop();

                Assert.Empty(log.Events);
            }
        }

        [Fact]
        public async Task Disposing_stops_the_listening()
        {
            var (service, log, source) = Start();
            await service.StopAsync(default);

            var span = source.StartActivity("GET /orders", ActivityKind.Server);

            Assert.Null(span); // no listener any more: no span is created
            Assert.Empty(log.Events);
        }
    }
}
