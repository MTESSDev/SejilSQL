using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using SejilSQL.Diagnostics;
using Xunit;

namespace SejilSQL.Test.Diagnostics
{
    public class ThreadPoolWatcherTests
    {
        private sealed class Capture : ILoggerFactory, ILogger
        {
            public ConcurrentQueue<(LogLevel Level, string Message)> Events { get; } = new ConcurrentQueue<(LogLevel, string)>();

            public ILogger CreateLogger(string categoryName) => this;
            public void AddProvider(ILoggerProvider provider) { }
            public void Dispose() { }
            public bool IsEnabled(LogLevel logLevel) => true;
            public IDisposable BeginScope<TState>(TState state) => new NoScope();
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => Events.Enqueue((logLevel, formatter(state, exception)));

            private sealed class NoScope : IDisposable { public void Dispose() { } }
        }

        [Fact]
        public void A_healthy_pool_logs_nothing()
        {
            var log = new Capture();
            var options = new SejilTraceOptions
            {
                ThreadPoolSamplePeriod = TimeSpan.FromMilliseconds(30),
                ThreadPoolLatencyThreshold = TimeSpan.FromSeconds(30),   // never reached
                ThreadPoolPendingThreshold = int.MaxValue,               // never reached
                ThreadPoolConsecutiveSamples = 1,
            };

            using (var watcher = new ThreadPoolWatcher(options, log))
            {
                watcher.StartAsync(default).GetAwaiter().GetResult();
                Thread.Sleep(300);
                watcher.StopAsync(default).GetAwaiter().GetResult();
            }

            Assert.Empty(log.Events);
        }

        [Fact]
        public void A_symptom_that_lasts_is_logged_as_a_warning()
        {
            var log = new Capture();
            var options = new SejilTraceOptions
            {
                ThreadPoolSamplePeriod = TimeSpan.FromMilliseconds(30),
                ThreadPoolPendingThreshold = 0,          // always a symptom
                ThreadPoolConsecutiveSamples = 2,
                ThreadPoolRepeatEvery = TimeSpan.FromHours(1),
            };

            using (var watcher = new ThreadPoolWatcher(options, log))
            {
                watcher.StartAsync(default).GetAwaiter().GetResult();
                SpinWait.SpinUntil(() => !log.Events.IsEmpty, TimeSpan.FromSeconds(5));
                Thread.Sleep(200); // long enough for a second warning, which must not come (RepeatEvery)
                watcher.StopAsync(default).GetAwaiter().GetResult();
            }

            var warning = Assert.Single(log.Events);
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Contains("starvation", warning.Message);
        }

        [Fact]
        public void The_watcher_can_be_turned_off()
        {
            var log = new Capture();
            var options = new SejilTraceOptions
            {
                ThreadPoolWatcher = false,
                ThreadPoolSamplePeriod = TimeSpan.FromMilliseconds(30),
                ThreadPoolPendingThreshold = 0,
                ThreadPoolConsecutiveSamples = 1,
            };

            using (var watcher = new ThreadPoolWatcher(options, log))
            {
                watcher.StartAsync(default).GetAwaiter().GetResult();
                Thread.Sleep(200);
                watcher.StopAsync(default).GetAwaiter().GetResult();
            }

            Assert.Empty(log.Events);
        }
    }
}
