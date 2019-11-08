// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using SejilSQL.Configuration;
using System.Diagnostics;
using System.Data.SqlClient;
using System.Data.Common;

namespace SejilSQL.Logging.Sinks
{
    internal class SejilSink : PeriodicBatchingSink
    {
        private static readonly int _defaultBatchSizeLimit = 50;
        private static TimeSpan _defaultBatchEmitPeriod = TimeSpan.FromSeconds(5);

        private readonly ISejilSettings _settings;

        public SejilSink(ISejilSettings settings) : base(_defaultBatchSizeLimit, _defaultBatchEmitPeriod)
        {
            _settings = settings;
        }

        protected override async Task EmitBatchAsync(IEnumerable<LogEvent> events)
        {
            try
            {
                using (var conn = new SqlConnection(_settings.ConnectionString))
                {
                    await conn.OpenAsync();
                    using (var tran = conn.BeginTransaction())
                    {
                        using (var cmdLogEntry = CreateLogEntryInsertCommand(conn, tran))
                        using (var cmdLogEntryProperty = CreateLogEntryPropertyInsertCommand(conn, tran))
                        {
                            foreach (var logEvent in events)
                            {
                                // Do not log events that were generated from browsing Sejil URL.
                                if (logEvent.Properties.Any(p => (p.Key == "RequestPath" || p.Key == "Path") &&
                                    p.Value.ToString().Contains(_settings.Url)))
                                {
                                    continue;
                                }

                                var logId = await InsertLogEntryAsync(cmdLogEntry, logEvent);
                                foreach (var property in logEvent.Properties)
                                {
                                    await InsertLogEntryPropertyAsync(cmdLogEntryProperty, logId, logEvent.Timestamp, property);
                                }
                            }
                        }
                        tran.Commit();
                    }
                    conn.Close();
                }
            }
            catch (Exception e)
            {
                SelfLog.WriteLine(e.Message);
            }
        }

        private async Task<long> InsertLogEntryAsync(SqlCommand cmd, LogEvent log)
        {
            cmd.Parameters["@message"].Value = log.MessageTemplate.Render(log.Properties);
            cmd.Parameters["@level"].Value = (int)log.Level;
            cmd.Parameters["@timestamp"].Value = log.Timestamp;
            cmd.Parameters["@exception"].Value = log.Exception?.Demystify().ToString() ?? (object)DBNull.Value;
            cmd.Parameters["@sourceApp"].Value = AppDomain.CurrentDomain.FriendlyName;

            var id = await cmd.ExecuteScalarAsync();
            return (long)id;
        }

        private async Task InsertLogEntryPropertyAsync(SqlCommand cmd, long logId, DateTimeOffset timestamp, KeyValuePair<string, LogEventPropertyValue> property)
        {
            cmd.Parameters["@logId"].Value = logId;
            cmd.Parameters["@name"].Value = property.Key;
            cmd.Parameters["@timestamp"].Value = timestamp;
            cmd.Parameters["@value"].Value = StripStringQuotes(property.Value?.ToString()) ?? (object)DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        private SqlCommand CreateLogEntryInsertCommand(SqlConnection conn, SqlTransaction tran)
        {
            var sql = "INSERT INTO [Journal].log (message, level, timestamp, exception, sourceApp)" +
                        "VALUES (@message, @level, @timestamp, @exception, @sourceApp); " +
                        " SELECT CONVERT(bigint,SCOPE_IDENTITY())";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = tran;

            cmd.Parameters.Add(new SqlParameter("@message", DbType.String));
            cmd.Parameters.Add(new SqlParameter("@level", DbType.Int32));
            cmd.Parameters.Add(new SqlParameter("@timestamp", DbType.DateTime2));
            cmd.Parameters.Add(new SqlParameter("@exception", DbType.String));
            cmd.Parameters.Add(new SqlParameter("@sourceApp", DbType.String));

            return cmd;
        }

        private SqlCommand CreateLogEntryPropertyInsertCommand(SqlConnection conn, SqlTransaction tran)
        {
            var sql = "INSERT INTO [Journal].log_property (logId, name, value, timestamp)" +
                "VALUES (@logId, @name, @value, @timestamp);";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = tran;

            cmd.Parameters.Add(new SqlParameter("@logId", DbType.Int64));
            cmd.Parameters.Add(new SqlParameter("@name", DbType.String));
            cmd.Parameters.Add(new SqlParameter("@value", DbType.String));
            cmd.Parameters.Add(new SqlParameter("@timestamp", DbType.DateTime2));

            return cmd;
        }

        private void InitializeDatabase()
        {
            using (var conn = new SqlConnection(_settings.ConnectionString))
            {
                conn.Open();
                var sql = ResourceHelper.GetEmbeddedResource("Sejil.db.sql");
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private string StripStringQuotes(string value)
            => (value?.Length > 0 && value[0] == '"' && value[value.Length - 1] == '"')
                ? value.Substring(1, value.Length - 2)
                : value;
    }
}
