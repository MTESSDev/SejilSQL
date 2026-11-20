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
