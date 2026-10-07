using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SejilSQL.Configuration;
using SejilSQL.Service;
using System;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension to conf
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers what a host needs to receive logs and keep the table small: <see cref="SejilService"/>
        /// (used by the ingest route) and the hosted service that deletes events older than <c>LogRetentionDays</c>.
        /// Safe to call when the host already registered <see cref="SejilService"/> itself.
        /// </summary>
        public static IServiceCollection AddSejilServices(this IServiceCollection services)
        {
            services.TryAddSingleton<SejilService>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SejilCleanupService>());
            return services;
        }

        /// <summary>
        /// Configure Sejil
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="setupAction">Delegate to configure the settings.</param>		
        public static void ConfigureSejil(this IServiceCollection services, Action<ISejilSettings> setupAction)
        {
            if (setupAction == null)
            {
                throw new ArgumentNullException(nameof(setupAction));
            }

            var settings = services.BuildServiceProvider().GetService<ISejilSettings>();

            setupAction(settings);
        }
    }
}
