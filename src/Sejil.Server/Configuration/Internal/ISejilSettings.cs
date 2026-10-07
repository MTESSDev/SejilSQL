// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Serilog.Core;
using System.IO;

namespace SejilSQL.Configuration
{
    public interface ISejilSettings
    {
        string SejilAppHtml { get; }
        string Url { get; }
        LoggingLevelSwitch LoggingLevelSwitch { get; }
        string ConnectionString { get; set; }
        string[] NonPropertyColumns { get; }
        int PageSize { get; set; }
        bool TrySetMinimumLogLevel(string minLogLevel);

        /// <summary>
        /// Gets or sets the title shown in the front end
        /// </summary>
        string Title { get; set; }

        /// <summary>
        /// Gets or sets the authentication scheme, used for the index page. Leave empty for no authentication.
        /// </summary>
        string AuthenticationScheme { get; set; }

        int LogRetentionDays { get; set; }

        /// <summary>
        /// Route (from the site root) that receives the batches sent by Serilog.Sinks.Http
        /// (JSON array, <c>?sourceApp=</c> required). Anonymous. Null or empty disables it.
        /// </summary>
        string IngestPath => "/log";

        /// <summary>
        /// Identifier of the minimum log level persisted in [Journal].log_config, shared by the applications that read it.
        /// Null keeps the level in memory only.
        /// </summary>
        string LevelId => null;

        /// <summary>
        /// Additional route (GET and POST) for the minimum log level, for hosts where <see cref="Url"/> is behind authentication
        /// but the applications must read the level anonymously. Null or empty disables it.
        /// </summary>
        string LevelPath => null;
    }
}