// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Reflection;
using SejilSQL.Logging;
using SejilSQL.Service;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Serilog
{
    public static class SejilSqlLoggerConfigurationExtensions
    {
        /// <summary>
        /// Writes the log events straight to the SejilSQL database, with no API in between: in an application that already
        /// uses Serilog, this line replaces <c>WriteTo.Http(...)</c>. The same method works from the configuration
        /// (Serilog.Settings.Configuration): <c>"Using": ["SejilSQL"]</c> and <c>{ "Name": "SejilSql", "Args": { ... } }</c>.
        /// </summary>
        /// <param name="sinkConfiguration">Where the sink is added.</param>
        /// <param name="connectionString">The database that holds the [Journal] schema (see db.sql). The login needs INSERT on it.</param>
        /// <param name="sourceApp">The application name shown in the viewer. Defaults to the name of the entry assembly.</param>
        /// <param name="restrictedToMinimumLevel">The minimum level of the events that reach this sink.</param>
        /// <param name="levelSwitch">With <paramref name="levelId"/>: switch kept in line with the shared minimum level of the database.</param>
        /// <param name="levelId">Identifier of the shared minimum level ([Journal].log_config). Null: the level is not read.</param>
        /// <param name="batchSize">Maximum number of events written in one round trip.</param>
        /// <param name="queueLimit">Events waiting to be written. Beyond it the newest are dropped, never the application slowed down.</param>
        /// <param name="periodSeconds">Time the sink waits after a write so that a burst of events gathers into one batch.</param>
        /// <param name="levelPollSeconds">How often the shared minimum level is read.</param>
        /// <param name="flushTimeoutSeconds">At shutdown, how long the queued events are retried when the database is unreachable.</param>
        public static LoggerConfiguration SejilSql(
            this LoggerSinkConfiguration sinkConfiguration,
            string connectionString,
            string sourceApp = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch levelSwitch = null,
            string levelId = null,
            int batchSize = 200,
            int queueLimit = 10000,
            int periodSeconds = 2,
            int levelPollSeconds = 60,
            int flushTimeoutSeconds = 10)
        {
            if (sinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sinkConfiguration));
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A connection string is required.", nameof(connectionString));
            }

            var application = !string.IsNullOrWhiteSpace(sourceApp)
                ? sourceApp
                : Assembly.GetEntryAssembly()?.GetName().Name ?? AppDomain.CurrentDomain.FriendlyName;

            var service = new SejilService(connectionString);
            var sink = new SejilSqlSink(
                events => service.TryEmitBatchAsync(events, application),
                batchSize,
                TimeSpan.FromSeconds(periodSeconds),
                queueLimit,
                retryDelay: TimeSpan.FromSeconds(10),
                flushTimeout: TimeSpan.FromSeconds(flushTimeoutSeconds));

            if (levelSwitch != null && !string.IsNullOrWhiteSpace(levelId))
            {
                sink.Companion = new SejilLevelWatcher(connectionString, levelId, levelSwitch, TimeSpan.FromSeconds(levelPollSeconds));
            }

            return sinkConfiguration.Sink(sink, restrictedToMinimumLevel, levelSwitch);
        }
    }
}
