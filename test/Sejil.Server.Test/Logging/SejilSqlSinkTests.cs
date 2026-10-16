// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SejilSQL.Logging;
using SejilSQL.Service;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace SejilSQL.Test.Logging
{
    public class SejilSqlSinkTests
    {
        private static readonly MessageTemplateParser Parser = new MessageTemplateParser();

        private static LogEvent CreateEvent(string template, LogEventLevel level = LogEventLevel.Information, Exception exception = null, params (string Name, object Value)[] properties)
        {
            var props = properties.Select(p => new LogEventProperty(p.Name, p.Value is LogEventPropertyValue v ? v : new ScalarValue(p.Value)));
            return new LogEvent(new DateTimeOffset(2026, 10, 7, 18, 30, 0, TimeSpan.FromHours(-4)), level, exception, Parser.Parse(template), props);
        }

        // ---------- conversion : mêmes textes que la route POST /log ----------

        [Fact]
        public void ToEvent_copies_timestamp_level_message_and_exception()
        {
            var logEvent = CreateEvent("Step {Etape} failed", LogEventLevel.Error, new InvalidOperationException("boom"), ("Etape", "appelCac"));

            var e = LogEventConverter.ToEvent(logEvent);

            Assert.Equal(logEvent.Timestamp, e.Timestamp);
            Assert.Equal(LogEventLevel.Error, e.Level);
            Assert.Equal("Step \"appelCac\" failed", e.RenderedMessage);
            Assert.Contains("InvalidOperationException: boom", e.Exception);
        }

        [Fact]
        public void ToEvent_has_no_properties_and_no_exception_when_there_are_none()
        {
            var e = LogEventConverter.ToEvent(CreateEvent("Hello"));

            Assert.Null(e.Properties);
            Assert.Null(e.Exception);
        }

        [Fact]
        public void ToEvent_stores_property_values_as_the_sender_wrote_them_whatever_the_culture()
        {
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("fr-CA"); // decimal comma
            try
            {
                var e = LogEventConverter.ToEvent(CreateEvent("x", properties: new (string, object)[]
                {
                    ("Text", "a text"), ("Int", 3), ("Decimal", 12.5m), ("Double", 12.5), ("Bool", true),
                    ("Date", new DateTime(2026, 10, 7, 21, 48, 0, DateTimeKind.Utc)), ("Nothing", null),
                }));

                Assert.Equal("a text", e.Properties["Text"]);
                Assert.Equal("3", e.Properties["Int"]);
                Assert.Equal("12.5", e.Properties["Decimal"]);
                Assert.Equal("12.5", e.Properties["Double"]);
                Assert.Equal("true", e.Properties["Bool"]);
                Assert.StartsWith("2026-10-07T21:48:00", e.Properties["Date"]);
                Assert.EndsWith("Z", e.Properties["Date"]);
                Assert.Null(e.Properties["Nothing"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [Fact]
        public void ToEvent_stores_a_structure_as_its_json_text()
        {
            var structure = new StructureValue(new[]
            {
                new LogEventProperty("a", new ScalarValue(1)),
                new LogEventProperty("b", new SequenceValue(new LogEventPropertyValue[] { new ScalarValue(1), new ScalarValue(2) })),
            });

            var e = LogEventConverter.ToEvent(CreateEvent("x", properties: new (string, object)[] { ("Objet", structure) }));

            Assert.Equal("{\"a\":1,\"b\":[1,2]}", e.Properties["Objet"]);
        }

        // ---------- file d'attente, lots, reprise ----------

        private static SejilSqlSink CreateSink(Func<IList<Event>, Task<bool>> write, int batchSize = 100, int queueLimit = 1000, int periodMs = 20, int retryMs = 20, int flushMs = 5000)
            => new SejilSqlSink(write, batchSize, TimeSpan.FromMilliseconds(periodMs), queueLimit, TimeSpan.FromMilliseconds(retryMs), TimeSpan.FromMilliseconds(flushMs));

        [Fact]
        public void Dispose_writes_every_queued_event_in_order_and_in_batches_of_at_most_batchSize()
        {
            var batches = new List<List<string>>();
            var sink = CreateSink(events => { lock (batches) { batches.Add(events.Select(e => e.RenderedMessage).ToList()); } return Task.FromResult(true); }, batchSize: 4);

            for (var i = 0; i < 10; i++)
            {
                sink.Emit(CreateEvent("event " + i));
            }
            sink.Dispose();

            var all = batches.SelectMany(b => b).ToList();
            Assert.Equal(Enumerable.Range(0, 10).Select(i => "event " + i), all);
            Assert.All(batches, b => Assert.True(b.Count <= 4));
        }

        [Fact]
        public void A_batch_that_cannot_be_written_is_kept_and_written_later_without_duplicates()
        {
            var attempts = 0;
            var written = new List<string>();
            var sink = CreateSink(events =>
            {
                if (Interlocked.Increment(ref attempts) <= 2)
                {
                    return Task.FromResult(false); // database unreachable
                }
                lock (written) { written.AddRange(events.Select(e => e.RenderedMessage)); }
                return Task.FromResult(true);
            });

            sink.Emit(CreateEvent("one"));
            sink.Emit(CreateEvent("two"));
            sink.Dispose();

            Assert.Equal(new[] { "one", "two" }, written);
            Assert.True(attempts >= 3);
        }

        [Fact]
        public void A_writer_that_throws_does_not_stop_the_sink_or_the_application()
        {
            var calls = 0;
            var written = new List<string>();
            var sink = CreateSink(events =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("login failed");
                }
                lock (written) { written.AddRange(events.Select(e => e.RenderedMessage)); }
                return Task.FromResult(true);
            });

            sink.Emit(CreateEvent("kept"));
            sink.Dispose();

            Assert.Equal(new[] { "kept" }, written);
        }

        [Fact]
        public void Emit_never_blocks_and_drops_the_newest_events_when_the_queue_is_full()
        {
            var release = new ManualResetEventSlim();
            var written = new List<string>();
            var sink = CreateSink(events =>
            {
                release.Wait(); // the database is slow: nothing is written until released
                lock (written) { written.AddRange(events.Select(e => e.RenderedMessage)); }
                return Task.FromResult(true);
            }, batchSize: 1, queueLimit: 5);

            var emitted = Task.Run(() =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    sink.Emit(CreateEvent("event " + i));
                }
            });

            Assert.True(emitted.Wait(TimeSpan.FromSeconds(5)), "Emit blocked while the database was slow");

            release.Set();
            sink.Dispose();

            Assert.True(written.Count <= 6, $"{written.Count} events written for a queue of 5");
            Assert.Equal("event 0", written[0]);
        }

        [Fact]
        public void Dispose_gives_up_after_the_flush_timeout_when_the_database_stays_unreachable()
        {
            var sink = CreateSink(events => Task.FromResult(false), retryMs: 10, flushMs: 200);
            sink.Emit(CreateEvent("lost"));

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            sink.Dispose();

            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {elapsed.Elapsed}");
        }

        // ---------- la ligne de configuration ----------

        [Fact]
        public void SejilSql_requires_a_connection_string()
        {
            Assert.Throws<ArgumentException>(() => new LoggerConfiguration().WriteTo.SejilSql(" "));
        }

        [Fact]
        public void SejilSql_can_be_added_and_disposed_without_a_reachable_database()
        {
            // An unreachable database must never break the application: the events are kept, then given up on at shutdown.
            var logger = new LoggerConfiguration()
                .WriteTo.SejilSql("Server=127.0.0.1,1;Database=none;Connect Timeout=1;User Id=x;Password=x", "TestApp", periodSeconds: 0, flushTimeoutSeconds: 1)
                .CreateLogger();

            logger.Information("hello {Name}", "world");

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            ((IDisposable)logger).Dispose();
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"Dispose took {elapsed.Elapsed}");
        }
    }
}
