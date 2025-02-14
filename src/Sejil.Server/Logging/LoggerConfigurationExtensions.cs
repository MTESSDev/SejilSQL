// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using SejilSQL;
using SejilSQL.Configuration;
using SejilSQL.Logging.Sinks;
using Serilog;
using Serilog.Configuration;
using Serilog.Debugging;
using Serilog.Events;

namespace SejilSQL.Logging
{
    internal static class LoggerConfigurationExtensions
    {
        public static LoggerConfiguration Sejil(
            this LoggerSinkConfiguration loggerConfiguration,
            ISejilSettings settings,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum)
        {
            try
            {
                //var sqliteDbFile = new FileInfo(settings.SqliteDbPath);
                //sqliteDbFile.Directory.Create();

                return loggerConfiguration.Sink(
                    new SejilSink(settings),
                    restrictedToMinimumLevel);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
                throw;
            }
        }
    }
}