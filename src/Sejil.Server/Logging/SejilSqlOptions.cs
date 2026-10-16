// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace SejilSQL.Logging
{
    /// <summary>
    /// What the sink needs, from what the application passed explicitly and, for everything it did not pass,
    /// from the configuration: an explicit value wins, then the <c>SejilSql</c> section, then the default.
    /// </summary>
    internal sealed class SejilSqlOptions
    {
        public const string DefaultSection = "SejilSql";

        public string ConnectionString { get; private set; }
        public string SourceApp { get; private set; }
        public string LevelId { get; private set; }
        public int BatchSize { get; private set; }
        public int QueueLimit { get; private set; }
        public int PeriodSeconds { get; private set; }
        public int LevelPollSeconds { get; private set; }
        public int FlushTimeoutSeconds { get; private set; }

        /// <param name="configuration">The application configuration, or null when the sink was not given one.</param>
        /// <param name="section">Section that holds the keys below. <c>SejilSql</c> when null.</param>
        /// <param name="connectionString">A connection string, or the name of one in ConnectionStrings. Null: read from the configuration.</param>
        /// <param name="batchSize">Negative: not passed, read from the configuration.</param>
        public static SejilSqlOptions Resolve(
            IConfiguration configuration,
            string section,
            string connectionString,
            string sourceApp,
            string levelId,
            int batchSize,
            int queueLimit,
            int periodSeconds,
            int levelPollSeconds,
            int flushTimeoutSeconds)
        {
            section = string.IsNullOrWhiteSpace(section) ? DefaultSection : section;

            string Setting(string key) => string.IsNullOrWhiteSpace(configuration?[$"{section}:{key}"]) ? null : configuration[$"{section}:{key}"].Trim();

            int Number(int passed, string key, int fallback)
                => passed >= 0 ? passed
                 : int.TryParse(Setting(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured >= 0 ? configured
                 : fallback;

            return new SejilSqlOptions
            {
                ConnectionString = ResolveConnectionString(configuration, section, connectionString),
                SourceApp = FirstNonEmpty(sourceApp, Setting("SourceApp"), Assembly.GetEntryAssembly()?.GetName().Name, AppDomain.CurrentDomain.FriendlyName),
                LevelId = FirstNonEmpty(levelId, Setting("LevelId")),
                BatchSize = Math.Max(1, Number(batchSize, "BatchSize", 200)),
                QueueLimit = Math.Max(1, Number(queueLimit, "QueueLimit", 10000)),
                PeriodSeconds = Number(periodSeconds, "PeriodSeconds", 2),
                LevelPollSeconds = Math.Max(1, Number(levelPollSeconds, "LevelPollSeconds", 60)),
                FlushTimeoutSeconds = Number(flushTimeoutSeconds, "FlushTimeoutSeconds", 10),
            };
        }

        private static string ResolveConnectionString(IConfiguration configuration, string section, string argument)
        {
            var tried = new List<string>();

            if (!string.IsNullOrWhiteSpace(argument))
            {
                if (argument.IndexOf('=') >= 0)
                {
                    return argument; // a connection string
                }

                // a name, like Serilog.Sinks.MSSqlServer: the entry of that name in ConnectionStrings
                var named = configuration?.GetConnectionString(argument);
                if (!string.IsNullOrWhiteSpace(named))
                {
                    return named;
                }
                tried.Add($"ConnectionStrings:{argument}");
            }
            else
            {
                var fromSection = configuration?[$"{section}:ConnectionString"];
                if (!string.IsNullOrWhiteSpace(fromSection))
                {
                    return fromSection;
                }
                tried.Add($"{section}:ConnectionString");

                var native = configuration?.GetConnectionString("SejilSql");
                if (!string.IsNullOrWhiteSpace(native))
                {
                    return native;
                }
                tried.Add("ConnectionStrings:SejilSql");
            }

            throw new InvalidOperationException(configuration == null
                ? "SejilSql: no connection string. Pass connectionString, or give the sink the configuration (ReadFrom.Configuration, or the configuration parameter)."
                : $"SejilSql: no connection string found. Looked for {string.Join(", ", tried)}.");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
