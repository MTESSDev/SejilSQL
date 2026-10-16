// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.Configuration;
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
        /// Writes the log events straight to the SejilSQL database, with no API in between. In an application that already
        /// uses Serilog it replaces <c>WriteTo.Http(...)</c>.
        /// <para>
        /// With <c>ReadFrom.Configuration</c> (Serilog.Settings.Configuration) it needs no argument at all:
        /// <c>"WriteTo": [ { "Name": "SejilSql" } ]</c> and the package fills in <paramref name="configuration"/>, so the sink reads
        /// <c>ConnectionStrings:SejilSql</c> (or <c>SejilSql:ConnectionString</c>) and the other optional keys of the
        /// <c>SejilSql</c> section from the application configuration. Anything passed explicitly wins over the configuration.
        /// </para>
        /// </summary>
        /// <param name="sinkConfiguration">Where the sink is added.</param>
        /// <param name="connectionString">A connection string to the database that holds the [Journal] schema (see db.sql; the login needs INSERT),
        /// or the name of one in ConnectionStrings. Null: read from the configuration.</param>
        /// <param name="sourceApp">The application name shown in the viewer. Key <c>SourceApp</c>; defaults to the name of the entry assembly.</param>
        /// <param name="restrictedToMinimumLevel">The minimum level of the events that reach this sink.</param>
        /// <param name="levelSwitch">Switch kept in line with the shared minimum level of the database (see <paramref name="levelId"/>).
        /// When there is a level id and no switch, the sink uses its own, which filters only what it writes.</param>
        /// <param name="levelId">Identifier of the shared minimum level ([Journal].log_config). Key <c>LevelId</c>. None: the level is not read.</param>
        /// <param name="batchSize">Maximum number of events written in one round trip. Key <c>BatchSize</c>, 200.</param>
        /// <param name="queueLimit">Events waiting to be written; beyond it the newest are dropped, never the application slowed down. Key <c>QueueLimit</c>, 10000.</param>
        /// <param name="periodSeconds">Time the sink waits after a write so that a burst of events gathers into one batch. Key <c>PeriodSeconds</c>, 2.</param>
        /// <param name="levelPollSeconds">How often the shared minimum level is read. Key <c>LevelPollSeconds</c>, 60.</param>
        /// <param name="flushTimeoutSeconds">At shutdown, how long the queued events are retried when the database is unreachable. Key <c>FlushTimeoutSeconds</c>, 10.</param>
        /// <param name="section">Configuration section that holds the keys above. <c>SejilSql</c> by default.</param>
        /// <param name="configuration">The application configuration. Filled in by Serilog.Settings.Configuration; pass it yourself otherwise.</param>
        /// <remarks>A negative number means "not passed": the configuration, then the default, decides.</remarks>
        public static LoggerConfiguration SejilSql(
            this LoggerSinkConfiguration sinkConfiguration,
            string connectionString = null,
            string sourceApp = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch levelSwitch = null,
            string levelId = null,
            int batchSize = -1,
            int queueLimit = -1,
            int periodSeconds = -1,
            int levelPollSeconds = -1,
            int flushTimeoutSeconds = -1,
            string section = null,
            IConfiguration configuration = null)
        {
            if (sinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sinkConfiguration));
            }

            var options = SejilSqlOptions.Resolve(configuration, section, connectionString, sourceApp, levelId,
                batchSize, queueLimit, periodSeconds, levelPollSeconds, flushTimeoutSeconds);

            var service = new SejilService(options.ConnectionString);
            var sink = new SejilSqlSink(
                events => service.TryEmitBatchAsync(events, options.SourceApp),
                options.BatchSize,
                TimeSpan.FromSeconds(options.PeriodSeconds),
                options.QueueLimit,
                retryDelay: TimeSpan.FromSeconds(10),
                flushTimeout: TimeSpan.FromSeconds(options.FlushTimeoutSeconds));

            if (!string.IsNullOrWhiteSpace(options.LevelId))
            {
                // Warning until the shared level is read, like the applications that read it over HTTP.
                levelSwitch = levelSwitch ?? new LoggingLevelSwitch(LogEventLevel.Warning);
                sink.Companion = new SejilLevelWatcher(options.ConnectionString, options.LevelId, levelSwitch, TimeSpan.FromSeconds(options.LevelPollSeconds));
            }

            return sinkConfiguration.Sink(sink, restrictedToMinimumLevel, levelSwitch);
        }
    }
}
