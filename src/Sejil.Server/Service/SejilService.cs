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
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Data.SqlClient;

namespace SejilSQL.Service
{
    public class SejilService
    {
        private readonly ISejilSettings _settings;
        private readonly string _connectionString;

        public SejilService(ISejilSettings settings)
        {
            _connectionString = settings.ConnectionString;
            _settings = settings;

            //InitializeDatabase();
        }

        public async Task EmitBatchAsync(IEnumerable<Event> events, string sourceApp)
            => await TryEmitBatchAsync(events, sourceApp);

        /// <summary>
        /// Writes the batch atomically (all or nothing). Returns false when it could not be written,
        /// so that the caller can have the sender retry without creating duplicates.
        /// </summary>
        public async Task<bool> TryEmitBatchAsync(IEnumerable<Event> events, string sourceApp)
        {
            try
            {
                var batch = events as IList<Event> ?? events.ToList();

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    if (_setBased == null)
                    {
                        _setBased = await SupportsOpenJsonAsync(conn);
                    }

                    if (_setBased.Value)
                    {
                        await InsertBatchAsync(conn, batch, sourceApp);
                    }
                    else
                    {
                        await InsertBatchRowByRowAsync(conn, batch, sourceApp);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                SelfLog.WriteLine(e.Message);
                return false;
            }
        }

        // Determined once from the database: OPENJSON needs compatibility level 130 (SQL Server 2016) or higher.
        private bool? _setBased;

        private static async Task<bool> SupportsOpenJsonAsync(SqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()";
                return Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= 130;
            }
        }

        // The whole batch in one round trip: the events are inserted with MERGE ... OUTPUT, which returns the generated id of each
        // one (matched to its position in the batch), then their properties are inserted with those ids. One INSERT per row costs
        // one network round trip each: 9000 rows is about 2.5 minutes at 16 ms, against about half a second for this statement.
        // OPTION (RECOMPILE) on the join is required: without it the plan compiled for the first (usually tiny) batch is reused for
        // big ones, and it re-parses the whole properties JSON for each event (1000 events: 29 s instead of 0.1 s).
        private const string InsertBatchSql = @"
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @map TABLE (idx int PRIMARY KEY, id bigint);
MERGE Journal.log AS t
USING (SELECT idx, message, level, ts, exception
       FROM OPENJSON(@events) WITH (idx int '$.i', message nvarchar(max) '$.m', level int '$.l', ts datetime2 '$.t', exception nvarchar(max) '$.x')) AS s
ON 1 = 0
WHEN NOT MATCHED THEN INSERT (sourceApp, message, level, timestamp, exception) VALUES (@sourceApp, s.message, s.level, s.ts, s.exception)
OUTPUT s.idx, inserted.id INTO @map;
INSERT INTO Journal.log_property (logId, name, value, timestamp)
SELECT m.id, p.name, p.value, p.ts
FROM OPENJSON(@properties) WITH (idx int '$.i', name nvarchar(256) '$.n', value nvarchar(max) '$.v', ts datetime2 '$.t') AS p
JOIN @map m ON m.idx = p.idx
OPTION (RECOMPILE);
COMMIT;";

        private async Task InsertBatchAsync(SqlConnection conn, IList<Event> batch, string sourceApp)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = InsertBatchSql;
                cmd.CommandType = CommandType.Text;
                cmd.CommandTimeout = 120;
                cmd.Parameters.Add(new SqlParameter("@events", SqlDbType.NVarChar, -1) { Value = WriteEvents(batch) });
                cmd.Parameters.Add(new SqlParameter("@properties", SqlDbType.NVarChar, -1) { Value = WriteProperties(batch) });
                cmd.Parameters.Add(new SqlParameter("@sourceApp", SqlDbType.NVarChar, 200) { Value = sourceApp });
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // [{"i": position in the batch, "m": message, "l": level, "t": timestamp, "x": exception}, ...]
        private static string WriteEvents(IList<Event> batch)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartArray();
                for (var i = 0; i < batch.Count; i++)
                {
                    var e = batch[i];
                    w.WriteStartObject();
                    w.WriteNumber("i", i);
                    w.WriteString("m", e.RenderedMessage);
                    w.WriteNumber("l", (int)e.Level);
                    w.WriteString("t", e.Timestamp.DateTime);
                    w.WriteString("x", e.Exception);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        // [{"i": position of its event in the batch, "n": name, "v": value, "t": timestamp}, ...]
        private static string WriteProperties(IList<Event> batch)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartArray();
                for (var i = 0; i < batch.Count; i++)
                {
                    if (batch[i].Properties == null)
                    {
                        continue;
                    }

                    foreach (var property in batch[i].Properties)
                    {
                        w.WriteStartObject();
                        w.WriteNumber("i", i);
                        w.WriteString("n", property.Key);
                        w.WriteString("v", property.Value);
                        w.WriteString("t", batch[i].Timestamp.DateTime);
                        w.WriteEndObject();
                    }
                }
                w.WriteEndArray();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        // For databases below compatibility level 130: one INSERT per row, in a single transaction.
        private async Task InsertBatchRowByRowAsync(SqlConnection conn, IList<Event> batch, string sourceApp)
        {
            using (var tran = conn.BeginTransaction())
            {
                using (var cmdLogEntry = CreateLogEntryInsertCommand(conn, tran))
                using (var cmdLogEntryProperty = CreateLogEntryPropertyInsertCommand(conn, tran))
                {
                    foreach (var logEvent in batch)
                    {
                        var logId = await InsertLogEntryAsync(cmdLogEntry, logEvent, sourceApp);

                        if (logEvent.Properties != null)
                        {
                            foreach (KeyValuePair<string, string> property in logEvent.Properties)
                            {
                                await InsertLogEntryPropertyAsync(cmdLogEntryProperty, logId, logEvent.Timestamp, property);
                            }
                        }
                    }
                }
                tran.Commit();
            }
        }

        public async Task CleanupDb()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    var sql = $"DELETE TOP(5000) FROM [JOURNAL].log          WHERE timestamp <= DATEADD(day, -{_settings.LogRetentionDays}, GETDATE());" +
                              $"DELETE TOP(50000) FROM [JOURNAL].log_property WHERE timestamp <= DATEADD(day, -{_settings.LogRetentionDays}, GETDATE());";
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        cmd.CommandTimeout = 150;
                        var nb = await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {

                IEnumerable<Event> listEvent = new List<Event>() { new Event() { Exception = ex.ToString(), Level = LogEventLevel.Fatal, Timestamp = DateTime.Now, RenderedMessage = "Cleanup database fail - " + ex.Message } };
                await EmitBatchAsync(listEvent, "SejilCleanup");

            }
        }

        private async Task<long> InsertLogEntryAsync(SqlCommand cmd, Event log, string sourceApp)
        {
            cmd.Parameters["@sourceApp"].Value = sourceApp;
            cmd.Parameters["@message"].Value = log.RenderedMessage;
            cmd.Parameters["@level"].Value = (int)log.Level;
            cmd.Parameters["@timestamp"].Value = log.Timestamp;
            cmd.Parameters["@exception"].Value = log.Exception ?? (object)DBNull.Value; //log.Exception?.Demystify().ToString() ?? (object)DBNull.Value;

            return (long)await cmd.ExecuteScalarAsync();
        }

        private async Task InsertLogEntryPropertyAsync(SqlCommand cmd, long logId, DateTimeOffset timestamp, KeyValuePair<string, string> property)
        {
            cmd.Parameters["@logId"].Value = logId;
            cmd.Parameters["@name"].Value = property.Key;
            cmd.Parameters["@timestamp"].Value = timestamp;
            cmd.Parameters["@value"].Value = (object)property.Value ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        private SqlCommand CreateLogEntryInsertCommand(SqlConnection conn, SqlTransaction tran)
        {
            var sql = "INSERT INTO Journal.log (sourceApp, message, level, timestamp, exception)" +
                "VALUES (@sourceApp, @message, @level, @timestamp, @exception); SELECT CONVERT(bigint,SCOPE_IDENTITY())";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = tran;

            cmd.Parameters.Add(new SqlParameter("@sourceApp", DbType.String));
            cmd.Parameters.Add(new SqlParameter("@message", DbType.String));
            cmd.Parameters.Add(new SqlParameter("@level", DbType.Int32));
            cmd.Parameters.Add(new SqlParameter("@timestamp", DbType.DateTime2));
            cmd.Parameters.Add(new SqlParameter("@exception", DbType.String));

            return cmd;
        }

        private SqlCommand CreateLogEntryPropertyInsertCommand(SqlConnection conn, SqlTransaction tran)
        {
            var sql = "INSERT INTO Journal.log_property (logId, name, value, timestamp)" +
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

    }


    /// <summary>One log event, as sent by Serilog.Sinks.Http.</summary>
    public class Event
    {
        public DateTimeOffset Timestamp { get; set; }
        public LogEventLevel Level { get; set; }
        public string RenderedMessage { get; set; }
        public string Exception { get; set; }

        /// <summary>
        /// Property values as text, exactly as the sender wrote them: a JSON string as is, anything else
        /// (number, boolean, object, array) as its JSON text. A JSON null is stored as NULL.
        /// </summary>
        public IDictionary<string, string> Properties { get; set; }
    }
}
