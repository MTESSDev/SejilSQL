// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using Sejil.Configuration;
using System.Diagnostics;
using System.Dynamic;
using Newtonsoft.Json;

namespace Sejil.Logging.Service
{
    public class SejilService
    {
        private readonly string _connectionString;

        public SejilService(ISejilSettings settings)
        {
            _connectionString = $"DataSource={settings.SqliteDbPath}";

            InitializeDatabase();
        }

        public async Task EmitBatchAsync(IEnumerable<Event> events, string sourceApp)
        {
            try
            {
                using (var conn = new SqliteConnection(_connectionString))
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
                                /* if (logEvent.Properties.Any(p => (p.Key == "RequestPath" || p.Key == "Path") &&
                                     p.ToString().Contains(_uri)))
                                 {
                                     continue;
                                 }*/

                                var logId = await InsertLogEntryAsync(cmdLogEntry, logEvent, sourceApp);

                                /* foreach (KeyValuePair<string, object> item in logEvent.Properties)
                                 {
                                     var grgg = item;
                                 }*/

                                if (logEvent.Properties != null)
                                {
                                    foreach (KeyValuePair<string, object> property in logEvent.Properties)
                                    {
                                        await InsertLogEntryPropertyAsync(cmdLogEntryProperty, logId, property);
                                    }
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

        private async Task<string> InsertLogEntryAsync(SqliteCommand cmd, Event log, string sourceApp)
        {
            var id = Guid.NewGuid().ToString();

            cmd.Parameters["@id"].Value = id;
            cmd.Parameters["@sourceApp"].Value = sourceApp;
            cmd.Parameters["@message"].Value = log.RenderedMessage; //.MessageTemplate.Render(log.Properties);
            cmd.Parameters["@messageTemplate"].Value = log.MessageTemplate;
            cmd.Parameters["@level"].Value = log.Level.ToString();
            cmd.Parameters["@timestamp"].Value = log.Timestamp.ToUniversalTime();
            cmd.Parameters["@exception"].Value = log.Exception ?? (object)DBNull.Value; //log.Exception?.Demystify().ToString() ?? (object)DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
            return id;
        }

        private async Task InsertLogEntryPropertyAsync(SqliteCommand cmd, string logId, KeyValuePair<string, object> property)
        {
            cmd.Parameters["@logId"].Value = logId;
            cmd.Parameters["@name"].Value = property.Key;
            cmd.Parameters["@value"].Value = StripStringQuotes(JsonConvert.SerializeObject(property.Value)) ?? (object)DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        private SqliteCommand CreateLogEntryInsertCommand(SqliteConnection conn, SqliteTransaction tran)
        {
            var sql = "INSERT INTO log (id, sourceApp, message, messageTemplate, level, timestamp, exception)" +
                "VALUES (@id, @sourceApp, @message, @messageTemplate, @level, @timestamp, @exception);";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = tran;

            cmd.Parameters.Add(new SqliteParameter("@id", DbType.String));
            cmd.Parameters.Add(new SqliteParameter("@sourceApp", DbType.String));
            cmd.Parameters.Add(new SqliteParameter("@message", DbType.String));
            cmd.Parameters.Add(new SqliteParameter("@messageTemplate", DbType.String));
            cmd.Parameters.Add(new SqliteParameter("@level", DbType.String));
            cmd.Parameters.Add(new SqliteParameter("@timestamp", DbType.DateTime2));
            cmd.Parameters.Add(new SqliteParameter("@exception", DbType.String));

            return cmd;
        }

        private SqliteCommand CreateLogEntryPropertyInsertCommand(SqliteConnection conn, SqliteTransaction tran)
        {
            var sql = "INSERT INTO log_property (logId, name, value)" +
                "VALUES (@logId, @name, @value);";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = tran;

            cmd.Parameters.Add(new SqliteParameter("@logId", DbType.String));
            cmd.Parameters.Add(new SqliteParameter("@name", DbType.String));
            cmd.Parameters.Add(new SqliteParameter("@value", DbType.String));

            return cmd;
        }

        private void InitializeDatabase()
        {
            using (var conn = new SqliteConnection(_connectionString))
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


    public class Event
    {
        public DateTimeOffset Timestamp { get; set; }
        public LogEventLevel Level { get; set; }
        public string MessageTemplate { get; set; }
        public string RenderedMessage { get; set; }
        public ExpandoObject Properties { get; set; }
        public Renderings Renderings { get; set; }
        public string Exception { get; set; }
    }


    public class Eventid
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class Renderings
    {
        public Keyid[] KeyId { get; set; }
        public Expirationdate[] ExpirationDate { get; set; }
        public Hostingrequeststartinglog[] HostingRequestStartingLog { get; set; }
        public Hostingrequestfinishedlog[] HostingRequestFinishedLog { get; set; }
    }

    public class Keyid
    {
        public string Format { get; set; }
        public string Rendering { get; set; }
    }

    public class Expirationdate
    {
        public string Format { get; set; }
        public string Rendering { get; set; }
    }

    public class Hostingrequeststartinglog
    {
        public string Format { get; set; }
        public string Rendering { get; set; }
    }

    public class Hostingrequestfinishedlog
    {
        public string Format { get; set; }
        public string Rendering { get; set; }
    }

}
