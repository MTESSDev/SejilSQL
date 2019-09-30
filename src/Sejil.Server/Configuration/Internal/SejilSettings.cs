// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Serilog.Core;
using System.IO;
using System;
using Sejil.Configuration;

namespace Sejil.Configuration
{
    public class SejilSettings : ISejilSettings
    {
        private const string UUID = "59A8F730-6AC5-427A-9492-A3A9EAD9556F";

        public string SejilAppHtml { get; private set; }
        public string Url { get; private set; }
        public LoggingLevelSwitch LoggingLevelSwitch { get; private set; }
        public string SqliteDbPath { get; private set; }
        public string[] NonPropertyColumns { get; private set; }
        public int PageSize { get; private set; }

        /// <summary>
        /// Gets or sets the title shown in the front end
        /// </summary>
        public string Title { get; set; } = "Sejil";

        /// <summary>
        /// Gets or sets the authentication scheme, used for the index page. Leave empty for no authentication.
        /// </summary>
        public string AuthenticationScheme { get; set; }

        public SejilSettings(string uri, LogEventLevel minLogLevel)
        {
            SejilAppHtml = ResourceHelper.GetEmbeddedResource("Sejil.index.html");
            Url = uri.StartsWith("/") ? uri : "/" + uri;
            LoggingLevelSwitch = new LoggingLevelSwitch
            {
                MinimumLevel = minLogLevel
            };

            if (IsRunningInAzure())
            {
                // If running in azure, we won't use local app folder as its temporary and will frequently be deleted.
                // Use home folder instead.
                SqliteDbPath = Path.Combine(Path.GetFullPath("/home"), $"Sejil-{UUID}.sqlite");
            }
            else
            {
                var appDataFolder = GetLocalAppFolder();
                var appName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
                SqliteDbPath = Path.Combine(appDataFolder, appName, $"Sejil-{UUID}.sqlite");
            }

            NonPropertyColumns = new[] { "message", "messagetemplate", "level", "timestamp", "exception", "sourceapp" };
            PageSize = 100;
        }

        public bool TrySetMinimumLogLevel(string minLogLevel)
        {
            switch (minLogLevel.ToLower())
            {
                case "trace":
                case "verbose":
                    LoggingLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
                    return true;
                case "debug":
                    LoggingLevelSwitch.MinimumLevel = LogEventLevel.Debug;
                    return true;
                case "information":
                    LoggingLevelSwitch.MinimumLevel = LogEventLevel.Information;
                    return true;
                case "warning":
                    LoggingLevelSwitch.MinimumLevel = LogEventLevel.Warning;
                    return true;
                case "error":
                    LoggingLevelSwitch.MinimumLevel = LogEventLevel.Error;
                    return true;
                case "critical":
                case "fatal":
                    LoggingLevelSwitch.MinimumLevel = LogEventLevel.Fatal;
                    return true;
            }

            return false;
        }

        private string GetLocalAppFolder()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        private bool IsRunningInAzure()
            => !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
    }
}