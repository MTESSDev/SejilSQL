// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using SejilSQL.Data.Internal;
using SejilSQL.Routing.Internal;
using SejilSQL.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Hosting
{
    public static class WebHostBuilderExtensions
    {
        /// <summary>
        /// Adds Sejil services.
        /// </summary>
        /// <param name="builder">The web host builder.</param>
        /// <param name="url">The URL at which Sejil should be available.</param>
        /// <param name="minLogLevel">The minimum log level.</param>
        /// <returns></returns>
        public static IWebHostBuilder AddSejil(this IWebHostBuilder builder, ISejilSettings settings)
        {
            return builder
              .ConfigureServices(services =>
              {
                  services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                  services.AddSingleton(settings);
                  services.AddScoped<ISejilRepository, SejilRepository>();
                  services.AddScoped<ISejilSqlProvider, SejilSqlProvider>();
                  services.AddScoped<ISejilController, SejilController>();
              });
        }

    }

    public static class SejilTools
    {
        public static LogEventLevel MapSerilogLogLevel(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
            {
                throw new InvalidOperationException("Minimum log level cannot be set to None.");
            }

            return (LogEventLevel)((int)logLevel);
        }
    }
}