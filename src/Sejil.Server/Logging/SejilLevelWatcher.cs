// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using SejilSQL.Data.Internal;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace SejilSQL.Logging
{
    /// <summary>
    /// Reads the shared minimum log level ([Journal].log_config) at start and then at every period, and applies it
    /// to a LoggingLevelSwitch: the same job as an application reading GET /min-log-level, with the database as the source.
    /// A failed read keeps the current level.
    /// </summary>
    internal sealed class SejilLevelWatcher : IDisposable
    {
        private readonly string _connectionString;
        private readonly string _levelId;
        private readonly LoggingLevelSwitch _levelSwitch;
        private readonly TimeSpan _period;
        private readonly Timer _timer;
        private volatile bool _disposed;

        public SejilLevelWatcher(string connectionString, string levelId, LoggingLevelSwitch levelSwitch, TimeSpan period)
        {
            _connectionString = connectionString;
            _levelId = levelId.Length > 20 ? levelId.Substring(0, 20) : levelId; // log_config.id is 20 characters wide
            _levelSwitch = levelSwitch;
            _period = period;
            _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        private void Poll()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = new SejilSqlProvider(null).GetLogLevelSql();
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@id", _levelId);

                        if (cmd.ExecuteScalar() is string value && Enum.TryParse(value, true, out LogEventLevel level) && Enum.IsDefined(typeof(LogEventLevel), level))
                        {
                            _levelSwitch.MinimumLevel = level;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SelfLog.WriteLine("SejilSQL: could not read the minimum log level '{0}': {1}", _levelId, e.Message);
            }
            finally
            {
                if (!_disposed)
                {
                    try
                    {
                        _timer.Change(_period, Timeout.InfiniteTimeSpan); // re-armed after the read: reads never overlap
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
