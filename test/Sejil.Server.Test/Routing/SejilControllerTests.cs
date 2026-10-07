// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using SejilSQL.Configuration;
using SejilSQL.Data.Internal;
using SejilSQL.Models.Internal;
using SejilSQL.Routing.Internal;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace SejilSQL.Test.Routing
{
    public class SejilControllerTests
    {
        internal static readonly JsonSerializerOptions _camelCaseJson = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        [Fact]
        public async Task GetIndexAsync_writes_app_html_to_response_stream()
        {
            // Arrange
            var appHtml = "<html>...</html>";

            var settingsMoq = new Mock<ISejilSettings>();
            settingsMoq.SetupGet(p => p.SejilAppHtml).Returns(appHtml).Verifiable();
            var (contextMoq, responseMoq, bodyMoq) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, Mock.Of<ISejilRepository>(), settingsMoq.Object);

            // Act
            await controller.GetIndexAsync();

            // Assert
            settingsMoq.VerifyAll();
            responseMoq.VerifySet(p => p.ContentType = "text/html");
            var data = Encoding.UTF8.GetBytes(appHtml);
            bodyMoq.Verify(p => p.WriteAsync(data, 0, data.Length, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEventsAsync_retreives_paged_events_from_repository()
        {
            // Arrange
            var page = 1;
            var startingTimestamp = DateTime.Now;

            var repositoryMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.GetEventsAsync(page, null, startingTimestamp, new LogQueryFilter());

            // Assert
            repositoryMoq.Verify(p => p.GetEventsPageAsync(page, startingTimestamp, It.IsAny<LogQueryFilter>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task GetEventsAsync_sets_page_to_1_when_zero_then_retreives_paged_events_from_repository()
        {
            // Arrange
            var page = 0;
            var startingTimestamp = DateTime.Now;

            var repositoryMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.GetEventsAsync(page, null, startingTimestamp, new LogQueryFilter());

            // Assert
            repositoryMoq.Verify(p => p.GetEventsPageAsync(page + 1, startingTimestamp, It.IsAny<LogQueryFilter>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task GetEventsAsync_passes_query_filter_to_repository()
        {
            // Arrange
            var qf = new LogQueryFilter
            {
                QueryText = "p=v",
                DateFilter = "5m",
                DateRangeFilter = new List<DateTime>() { DateTime.Now, DateTime.Now }
            };
            var repositoryMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.GetEventsAsync(1, null, null, qf);

            // Assert
            repositoryMoq.Verify(p => p.GetEventsPageAsync(1, null, It.Is<LogQueryFilter>(f => f == qf), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task GetEventsAsync_writes_events_json_to_response_stream()
        {
            // Arrange
            var (logEntries, logEntriesJson) = GetTestLogEntries();
            var repositoryMoq = new Mock<ISejilRepository>();
            repositoryMoq.Setup(p => p.GetEventsPageAsync(1, null, null, It.IsAny<int?>())).ReturnsAsync(logEntries);
            var (contextMoq, responseMoq, bodyMoq) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.GetEventsAsync(1, null, null, null);

            // Assert
            responseMoq.VerifySet(p => p.ContentType = "application/json");
            var data = Encoding.UTF8.GetBytes(logEntriesJson);
            bodyMoq.Verify(p => p.WriteAsync(data, 0, data.Length, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveQueryAsync_calls_repository_SaveQueryAsync()
        {
            // Arrange
            var logQuery = new LogQuery
            {
                Name = "test",
                Query = "query"
            };
            var repositoryMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.SaveQueryAsync(logQuery);

            // Assert
            repositoryMoq.Verify(p => p.SaveQueryAsync(logQuery), Times.Once);
        }

        [Theory]
        [InlineData(true, StatusCodes.Status201Created)]
        [InlineData(false, StatusCodes.Status500InternalServerError)]
        public async Task SaveQueryAsync_sets_status_code_based_on_save_operation_success(bool saveOperationResult, int expectedStatusCode)
        {
            // Arrange
            var repositoryMoq = new Mock<ISejilRepository>();
            repositoryMoq.Setup(p => p.SaveQueryAsync(It.IsAny<LogQuery>())).ReturnsAsync(saveOperationResult);
            var (contextMoq, responseMoq, _) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.SaveQueryAsync(new LogQuery());

            // Assert
            responseMoq.VerifySet(p => p.StatusCode = expectedStatusCode);
        }

        [Fact]
        public async Task GetQueriesAsync_retreives_saved_queries_from_repository()
        {
            // Arrange
            var repositoryMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.GetQueriesAsync();

            // Assert
            repositoryMoq.Verify(p => p.GetSavedQueriesAsync(), Times.Once);
        }

        [Fact]
        public async Task GetQueriesAsync_writes_queries_json_to_response_stream()
        {
            // Arrange
            var (logQueries, logQueriesJson) = GetTestLogQueries();
            var repositoryMoq = new Mock<ISejilRepository>();
            repositoryMoq.Setup(p => p.GetSavedQueriesAsync()).ReturnsAsync(logQueries);
            var (contextMoq, responseMoq, bodyMoq) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.GetQueriesAsync();

            // Assert
            responseMoq.VerifySet(p => p.ContentType = "application/json");
            var data = Encoding.UTF8.GetBytes(logQueriesJson);
            bodyMoq.Verify(p => p.WriteAsync(data, 0, data.Length, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetMinimumLogLevelAsync_retreives_minimumLogLevel_from_settings()
        {
            // Arrange
            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);
            var settingsMoq = new Mock<ISejilSettings>();
            settingsMoq.SetupGet(p => p.LoggingLevelSwitch).Returns(levelSwitch);
            var controller = CreateController(CreateContextMoq().contextMoq.Object, Mock.Of<ISejilRepository>(), settingsMoq.Object);

            // Act
            await controller.GetMinimumLogLevelAsync();

            // Assert
            settingsMoq.VerifyGet(p => p.LoggingLevelSwitch, Times.Once);
        }

        [Fact]
        public async Task GetMinimumLogLevelAsync_writes_response_json_to_response_stream()
        {
            // Arrange
            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);
            var settingsMoq = new Mock<ISejilSettings>();
            settingsMoq.SetupGet(p => p.LoggingLevelSwitch).Returns(levelSwitch);

            var responseJson = JsonSerializer.Serialize(new
            {
                MinimumLogLevel = levelSwitch.MinimumLevel.ToString()
            }, _camelCaseJson);


            var (contextMoq, responseMoq, bodyMoq) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, Mock.Of<ISejilRepository>(), settingsMoq.Object);

            // Act
            await controller.GetMinimumLogLevelAsync();

            // Assert
            responseMoq.VerifySet(p => p.ContentType = "application/json");
            var data = Encoding.UTF8.GetBytes(responseJson);
            bodyMoq.Verify(p => p.WriteAsync(data, 0, data.Length, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetMinimumLogLevelAsync_calls_settings_TrySetMinimumLogLevel()
        {
            // Arrange
            var logLevel = "info";
            var settingsMoq = new Mock<ISejilSettings>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, Mock.Of<ISejilRepository>(), settingsMoq.Object);

            // Act
            await controller.SetMinimumLogLevelAsync(logLevel);

            // Assert
            settingsMoq.Verify(p => p.TrySetMinimumLogLevel(logLevel), Times.Once);
        }

        [Theory]
        [InlineData(true, StatusCodes.Status200OK)]
        [InlineData(false, StatusCodes.Status400BadRequest)]
        public async Task SetMinimumLogLevelAsync_sets_status_code_based_on_save_operation_success(bool saveOperationResult, int expectedStatusCode)
        {
            // Arrange
            var settingsMoq = new Mock<ISejilSettings>();
            settingsMoq.Setup(p => p.TrySetMinimumLogLevel(It.IsAny<string>())).Returns(saveOperationResult);
            var (contextMoq, responseMoq, _) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, Mock.Of<ISejilRepository>(), settingsMoq.Object);

            // Act
            await controller.SetMinimumLogLevelAsync("info");

            // Assert
            responseMoq.VerifySet(p => p.StatusCode = expectedStatusCode);
        }

        [Fact]
        public async Task SetMinimumLogLevelAsync_rejects_empty_body_without_calling_settings()
        {
            // Arrange
            var settingsMoq = new Mock<ISejilSettings>();
            var (contextMoq, responseMoq, _) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, Mock.Of<ISejilRepository>(), settingsMoq.Object);

            // Act
            await controller.SetMinimumLogLevelAsync(null);

            // Assert
            responseMoq.VerifySet(p => p.StatusCode = StatusCodes.Status400BadRequest);
            settingsMoq.Verify(p => p.TrySetMinimumLogLevel(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SetMinimumLogLevelAsync_persists_the_level_when_LevelId_is_set()
        {
            // Arrange
            var settings = new SejilSettings("/sejil", LogEventLevel.Debug) { LevelId = "OIM" };
            var repoMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repoMoq.Object, settings);

            // Act
            await controller.SetMinimumLogLevelAsync("\"Warning\"");

            // Assert
            repoMoq.Verify(p => p.SetLogLevelAsync("OIM", "Warning"), Times.Once);
        }

        [Fact]
        public async Task SetMinimumLogLevelAsync_does_not_persist_when_LevelId_is_not_set()
        {
            // Arrange
            var settings = new SejilSettings("/sejil", LogEventLevel.Debug);
            var repoMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repoMoq.Object, settings);

            // Act
            await controller.SetMinimumLogLevelAsync("Warning");

            // Assert
            repoMoq.Verify(p => p.SetLogLevelAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Assert.Equal(LogEventLevel.Warning, settings.LoggingLevelSwitch.MinimumLevel);
        }

        [Fact]
        public async Task GetMinimumLogLevelAsync_returns_the_persisted_level_when_LevelId_is_set()
        {
            // Arrange
            var settings = new SejilSettings("/sejil", LogEventLevel.Debug) { LevelId = "OIM" };
            var repoMoq = new Mock<ISejilRepository>();
            repoMoq.Setup(p => p.GetLogLevelAsync("OIM")).ReturnsAsync("Error");
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var controller = CreateController(context, repoMoq.Object, settings);

            // Act
            await controller.GetMinimumLogLevelAsync();

            // Assert
            Assert.Equal("{\"minimumLogLevel\":\"Error\"}", ReadBody(context));
        }

        [Fact]
        public async Task GetMinimumLogLevelAsync_falls_back_to_the_in_memory_level_when_nothing_is_persisted()
        {
            // Arrange
            var settings = new SejilSettings("/sejil", LogEventLevel.Debug) { LevelId = "OIM" };
            var repoMoq = new Mock<ISejilRepository>();
            repoMoq.Setup(p => p.GetLogLevelAsync("OIM")).ReturnsAsync((string)null);
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var controller = CreateController(context, repoMoq.Object, settings);

            // Act
            await controller.GetMinimumLogLevelAsync();

            // Assert
            Assert.Equal("{\"minimumLogLevel\":\"Debug\"}", ReadBody(context));
        }

        [Fact]
        public async Task DeleteQueryAsync_calls_repository_DeleteQueryAsync()
        {
            // Arrange
            var queryName = "query";
            var repositoryMoq = new Mock<ISejilRepository>();
            var controller = CreateController(CreateContextMoq().contextMoq.Object, repositoryMoq.Object, Mock.Of<ISejilSettings>());

            // Act
            await controller.DeleteQueryAsync(queryName);

            // Assert
            repositoryMoq.Verify(p => p.DeleteQueryAsync(queryName), Times.Once);
        }

        [Fact]
        public async Task GetTitleAsync_writes_configured_title_to_response_stream()
        {
            // Arrange
            var title = "Custom Title";
            var settingsMoq = new Mock<ISejilSettings>();
            settingsMoq.SetupGet(p => p.Title).Returns(title);
            var (contextMoq, responseMoq, bodyMoq) = CreateContextMoq();
            var controller = CreateController(contextMoq.Object, Mock.Of<ISejilRepository>(), settingsMoq.Object);
            var responseJson = JsonSerializer.Serialize(new
            {
                Title = title
            }, _camelCaseJson);

            // Act
            await controller.GetTitleAsync();

            // Assert
            responseMoq.VerifySet(p => p.ContentType = "application/json");
            var data = Encoding.UTF8.GetBytes(responseJson);
            bodyMoq.Verify(p => p.WriteAsync(data, 0, data.Length, It.IsAny<CancellationToken>()), Times.Once);
        }

        private static string ReadBody(HttpContext context)
        {
            context.Response.Body.Position = 0;
            return new StreamReader(context.Response.Body).ReadToEnd();
        }

        private ISejilController CreateController(HttpContext context, ISejilRepository repository, ISejilSettings settings)
            => new SejilController(new HttpContextAccessor { HttpContext = context }, repository, settings);

        private (IEnumerable<LogEntry> logEntries, string logEntriesJson) GetTestLogEntries()
        {
            var events = new List<LogEntry>()
            {
                new LogEntry
                {
                    Id = 001,
                    Message = "message",
                    //MessageTemplate = "message template",
                    Properties = new List<LogEntryProperty>
                    {
                        new LogEntryProperty
                        {
                            LogId = 1,
                            Name = "prop1",
                            Value = "value1"
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(events, _camelCaseJson);

            return (events, json);
        }

        private (IEnumerable<LogQuery> logQueries, string logQueriesJson) GetTestLogQueries()
        {
            var queries = new List<LogQuery>
            {
                new LogQuery
                {
                    Name = "q1",
                    Query = "query1"
                },
                new LogQuery
                {
                    Name = "q2",
                    Query = "query2"
                },
            };

            var json = JsonSerializer.Serialize(queries, _camelCaseJson);

            return (queries, json);
        }

        private (Mock<HttpContext> contextMoq, Mock<HttpResponse> responseMoq, Mock<Stream> bodyMoq) CreateContextMoq()
        {
            var bodyMoq = new Mock<Stream>();
            var responseMoq = new Mock<HttpResponse>();
            responseMoq.SetupGet(p => p.Body).Returns(bodyMoq.Object);
            var contextMoq = new Mock<HttpContext>();
            contextMoq.Setup(p => p.Response).Returns(responseMoq.Object);

            return (contextMoq, responseMoq, bodyMoq);
        }
    }
}