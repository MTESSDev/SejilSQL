// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using SejilSQL.Configuration;
using SejilSQL.Models.Internal;
using SejilSQL.Routing.Internal;
using Serilog.Events;
using Xunit;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using System.Threading;
using SejilSQL.Data.Internal;

namespace SejilSQL.Test
{
    public class WebHostBuilderExtensionsTests
    {
        [Theory]
        [InlineData(LogLevel.Trace, LogEventLevel.Verbose)]
        [InlineData(LogLevel.Debug, LogEventLevel.Debug)]
        [InlineData(LogLevel.Information, LogEventLevel.Information)]
        [InlineData(LogLevel.Warning, LogEventLevel.Warning)]
        [InlineData(LogLevel.Error, LogEventLevel.Error)]
        [InlineData(LogLevel.Critical, LogEventLevel.Fatal)]
        public void AddSejil_sets_intial_settings(LogLevel logLevel, LogEventLevel expectedMappedLogLevel)
        {
            // Arrange
            var url = "/sejil";
            var webhostBuilder = new WebHostBuilder()
                .Configure(app => { })
                .ConfigureServices(services => services.AddSingleton<IServer>(Mock.Of<IServer>()));

            // Act
            webhostBuilder.AddSejil(new SejilSettings(url, SejilTools.MapSerilogLogLevel(logLevel)));

            // Assert
            var settings = webhostBuilder.Build().Services.GetRequiredService<ISejilSettings>();
            Assert.Equal(url, settings.Url);
            Assert.Equal(expectedMappedLogLevel, settings.LoggingLevelSwitch.MinimumLevel);
        }


        [Fact]
        public void AddSejil_registeres_required_services()
        {
            // Arrange
            var webhostBuilder = new WebHostBuilder()
                .Configure(app => { })
                .ConfigureServices(services =>
                    services.AddSingleton<IServer>(Mock.Of<IServer>()));

            // Act
            webhostBuilder.AddSejil(new SejilSettings("/sejil", SejilTools.MapSerilogLogLevel(LogLevel.Debug)));

            // Assert
            var webhost = webhostBuilder.Build();

            using var scope = webhost.Services.CreateScope();
            var settings = scope.ServiceProvider.GetService(typeof(ISejilSettings));
            var repository = scope.ServiceProvider.GetService(typeof(ISejilRepository));
            var sqlProvider = scope.ServiceProvider.GetService(typeof(ISejilSqlProvider));
            var controller = scope.ServiceProvider.GetService(typeof(ISejilController));

            Assert.NotNull(settings);
            Assert.NotNull(repository);
            Assert.NotNull(sqlProvider);
            Assert.NotNull(controller);
        }

        [Fact]
        public void AddSejil_throws_when_none_is_passed_for_log_level()
        {
            // Arrange
            var webhostBuilder = new WebHostBuilder()
                .Configure(app => { })
                .ConfigureServices(services => services.AddSingleton<IServer>(Mock.Of<IServer>()));

            // Act & assert
            var ex = Assert.Throws<InvalidOperationException>(() => webhostBuilder.AddSejil(new SejilSettings("/sejil", SejilTools.MapSerilogLogLevel(LogLevel.None))));
            Assert.Equal("Minimum log level cannot be set to None.", ex.Message);
        }
    }
}