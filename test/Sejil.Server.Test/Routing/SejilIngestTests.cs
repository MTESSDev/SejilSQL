// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SejilSQL.Routing.Internal;
using Xunit;

namespace SejilSQL.Test.Routing
{
    public class SejilIngestTests
    {
        [Fact]
        public async Task HandleAsync_returns_400_when_sourceApp_is_missing()
        {
            var context = CreateContext("/log", "[]");

            await SejilIngest.HandleAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [Fact]
        public async Task HandleAsync_returns_400_when_body_is_not_valid_json()
        {
            var context = CreateContext("/log?sourceApp=App", "{not json");

            await SejilIngest.HandleAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("null")]
        public async Task HandleAsync_returns_200_without_touching_the_database_when_batch_is_empty(string body)
        {
            // No SejilService is registered: an empty batch must be answered before it is needed.
            var context = CreateContext("/log?sourceApp=App", body);

            await SejilIngest.HandleAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("\"text\"")]
        [InlineData("[1]")]
        [InlineData(@"[{""Level"":""Debug""}]")]
        [InlineData(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":""Loud""}]")]
        [InlineData("")]
        public async Task HandleAsync_returns_400_when_the_body_is_not_an_array_of_valid_events(string body)
        {
            var context = CreateContext("/log?sourceApp=App", body);

            await SejilIngest.HandleAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        private static DefaultHttpContext CreateContext(string pathAndQuery, string body)
        {
            var context = new DefaultHttpContext();
            var index = pathAndQuery.IndexOf('?');
            context.Request.Path = index < 0 ? pathAndQuery : pathAndQuery.Substring(0, index);
            context.Request.QueryString = new QueryString(index < 0 ? string.Empty : pathAndQuery.Substring(index));
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Response.Body = new MemoryStream();
            return context;
        }
    }
}
