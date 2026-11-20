using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace SejilSQL.Diagnostics
{
    /// <summary>Puts <see cref="SlowTraceMiddleware"/> in front of the pipeline of the application.</summary>
    internal sealed class SlowTraceStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(SlowTraceMiddleware.InvokeAsync);
                next(app);
            };
    }

    /// <summary>
    /// ASP.NET Core records the span of a request without saying which request it is: the name is "Microsoft.AspNetCore.Hosting.HttpRequestIn"
    /// and there are no tags (the OpenTelemetry instrumentation is what adds them). This middleware, outermost, fills what is missing:
    /// the name of the request when it starts (for the list of requests in flight), then the route, the status code and the failure
    /// status of a 5xx when it ends. The span is stopped after the middleware returns, so the listener sees all of it.
    /// </summary>
    internal static class SlowTraceMiddleware
    {
        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            var activity = context.Features.Get<IHttpActivityFeature>()?.Activity;
            if (activity == null)
            {
                await next();
                return;
            }

            // the raw path, no query string (it can hold secrets); the route pattern is not known yet
            activity.SetTag(InFlightTracker.RequestTag, $"{context.Request.Method} {context.Request.Path}");

            try
            {
                await next();
            }
            finally
            {
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                activity.DisplayName = $"{context.Request.Method} {route ?? context.Request.Path.ToString()}";

                if (route != null)
                {
                    activity.SetTag("http.route", route);
                }

                var status = context.Response.StatusCode;
                activity.SetTag("http.response.status_code", status);

                // A 5xx the application returned on purpose is not an exception, so ASP.NET Core does not mark the span as failed.
                if (status >= 500 && activity.Status == ActivityStatusCode.Unset)
                {
                    activity.SetStatus(ActivityStatusCode.Error, "HTTP " + status);
                }
            }
        }
    }
}
