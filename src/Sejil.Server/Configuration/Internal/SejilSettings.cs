// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Serilog.Core;
using System.IO;
using System;
using SejilSQL.Configuration;

namespace SejilSQL.Configuration
{
    public class SejilSettings : ISejilSettings
    {
        public string SejilAppHtml { get; private set; }
        public string Url { get; private set; }
        public LoggingLevelSwitch LoggingLevelSwitch { get; private set; }
        public string ConnectionString { get; set; }
        public string[] NonPropertyColumns { get; private set; }
        public int PageSize { get; set; } = 100;

        /// <summary>
        /// Gets or sets the title shown in the front end
        /// </summary>
        public string Title { get; set; } = "Sejil";

        /// <summary>
        /// Gets or sets the authentication scheme, used for the index page. Leave empty for no authentication.
        /// </summary>
        public string AuthenticationScheme { get; set; }
        public int LogRetentionDays { get; set; } = 30;

        public SejilSettings(string uri, LogEventLevel minLogLevel)
        {
            SejilAppHtml = ResourceHelper.GetEmbeddedResource("SejilSQL.index.html");
            Url = uri.StartsWith("/") ? uri : "/" + uri;
            LoggingLevelSwitch = new LoggingLevelSwitch
            {
                MinimumLevel = minLogLevel
            };

            NonPropertyColumns = new[] { "message", "level", "timestamp", "exception", "sourceapp" };
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