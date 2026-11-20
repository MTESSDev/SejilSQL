using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SejilSQL.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SejilTraceServiceCollectionExtensions
    {
        /// <summary>
        /// Logs the slow or failed operations of the application (incoming requests, outgoing HTTP calls) with their trace id,
        /// and a warning when the thread pool is starved. Fast operations leave nothing behind.
        /// <para>
        /// The events go through <c>ILogger</c>, so they reach the sinks already configured (the SejilSQL sink included).
        /// For them to be written, the minimum level of the logger must let Warning through.
        /// </para>
        /// </summary>
        /// <example>
        /// <code>services.AddSejilSlowTraces(o => o.SlowThreshold = TimeSpan.FromSeconds(1));</code>
        /// </example>
        public static IServiceCollection AddSejilSlowTraces(this IServiceCollection services, Action<SejilTraceOptions> configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var options = new SejilTraceOptions();
            configure?.Invoke(options);

            services.Replace(ServiceDescriptor.Singleton(options));
            services.TryAddSingleton<InFlightTracker>();
            // names the span of each request ("GET /orders/{id}"), and marks a 5xx as failed
            services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, SlowTraceStartupFilter>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SlowTraceService>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ThreadPoolWatcher>());

            return services;
        }
    }
}
