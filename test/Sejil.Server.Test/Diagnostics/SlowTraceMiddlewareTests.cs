using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using SejilSQL.Diagnostics;
using Xunit;

namespace SejilSQL.Test.Diagnostics
{
    public class SlowTraceMiddlewareTests : IDisposable
    {
        private sealed class ActivityFeature : IHttpActivityFeature
        {
            public Activity Activity { get; set; }
        }

        private readonly ActivitySource _source = new ActivitySource("SejilTest.Middleware." + Guid.NewGuid());
        private readonly ActivityListener _listener;

        public SlowTraceMiddlewareTests()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == _source.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose()
        {
            _listener.Dispose();
            _source.Dispose();
        }

        private (DefaultHttpContext Context, Activity Activity) Request(string method, string path, string route = null)
        {
            var activity = _source.StartActivity("Microsoft.AspNetCore.Hosting.HttpRequestIn", ActivityKind.Server);
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.QueryString = new QueryString("?token=secret");
            context.Features.Set<IHttpActivityFeature>(new ActivityFeature { Activity = activity });

            if (route != null)
            {
                context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0, EndpointMetadataCollection.Empty, "test"));
            }

            return (context, activity);
        }

        [Fact]
        public async Task The_span_is_named_after_the_route_and_carries_the_status_code()
        {
            var (context, activity) = Request("GET", "/orders/42", route: "/orders/{id}");

            await SlowTraceMiddleware.InvokeAsync(context, () =>
            {
                context.Response.StatusCode = 200;
                return Task.CompletedTask;
            });

            Assert.Equal("GET /orders/{id}", activity.DisplayName);   // not "Microsoft.AspNetCore.Hosting.HttpRequestIn"
            Assert.Equal("/orders/{id}", activity.GetTagItem("http.route"));
            Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
            Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        }

        [Fact]
        public async Task Without_a_route_the_path_names_the_request()
        {
            var (context, activity) = Request("POST", "/unknown");

            await SlowTraceMiddleware.InvokeAsync(context, () => Task.CompletedTask);

            Assert.Equal("POST /unknown", activity.DisplayName);
            Assert.Null(activity.GetTagItem("http.route"));
        }

        [Fact]
        public async Task The_request_is_named_as_soon_as_it_starts_without_its_query_string()
        {
            var (context, activity) = Request("GET", "/forms/bail");
            object seenWhileRunning = null;

            await SlowTraceMiddleware.InvokeAsync(context, () =>
            {
                seenWhileRunning = activity.GetTagItem(InFlightTracker.RequestTag);
                return Task.CompletedTask;
            });

            Assert.Equal("GET /forms/bail", seenWhileRunning);   // for the list of requests in flight; no "?token=secret"
        }

        [Theory]
        [InlineData(500)]
        [InlineData(503)]
        public async Task A_5xx_that_the_application_returned_marks_the_span_as_failed(int status)
        {
            var (context, activity) = Request("GET", "/fail");

            await SlowTraceMiddleware.InvokeAsync(context, () =>
            {
                context.Response.StatusCode = status;
                return Task.CompletedTask;
            });

            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Equal("HTTP " + status, activity.StatusDescription);
        }

        [Theory]
        [InlineData(200)]
        [InlineData(404)]
        [InlineData(499)]
        public async Task Other_statuses_are_not_failures(int status)
        {
            var (context, activity) = Request("GET", "/x");

            await SlowTraceMiddleware.InvokeAsync(context, () =>
            {
                context.Response.StatusCode = status;
                return Task.CompletedTask;
            });

            Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        }

        [Fact]
        public async Task An_exception_still_gets_the_span_named_and_goes_on_its_way()
        {
            var (context, activity) = Request("GET", "/boom", route: "/boom");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SlowTraceMiddleware.InvokeAsync(context, () => throw new InvalidOperationException("boom")));

            Assert.Equal("GET /boom", activity.DisplayName);
        }

        [Fact]
        public async Task Without_a_span_the_request_just_goes_through()
        {
            var context = new DefaultHttpContext();
            var called = false;

            await SlowTraceMiddleware.InvokeAsync(context, () =>
            {
                called = true;
                return Task.CompletedTask;
            });

            Assert.True(called);
        }
    }
}
