// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SejilSQL.Service;

namespace SejilSQL.Routing.Internal
{
    /// <summary>
    /// Receives the batches posted by Serilog.Sinks.Http: a JSON array of events, with the
    /// application name in <c>?sourceApp=</c>.
    /// </summary>
    internal static class SejilIngest
    {
        public static async Task HandleAsync(HttpContext context)
        {
            string sourceApp = context.Request.Query["sourceApp"];
            if (String.IsNullOrWhiteSpace(sourceApp))
            {
                await BadRequestAsync(context, "The sourceApp query parameter is required.");
                return;
            }

            List<Event> events;
            try
            {
                using (var document = await JsonDocument.ParseAsync(context.Request.Body))
                {
                    var root = document.RootElement;

                    if (root.ValueKind == JsonValueKind.Null)
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        return;
                    }

                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        await BadRequestAsync(context, "A JSON array of events is expected.");
                        return;
                    }

                    events = SejilEventReader.Read(root);
                }
            }
            catch (Exception e) when (e is JsonException || e is FormatException)
            {
                await BadRequestAsync(context, $"Invalid events: {e.Message}");
                return;
            }

            if (events.Count == 0)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            // Serilog sends UTC timestamps while the log tables, the date filters (GETDATE()) and the cleanup work in local time.
            foreach (var e in events)
            {
                e.Timestamp = e.Timestamp.ToLocalTime();
            }

            var service = context.RequestServices.GetService(typeof(SejilService)) as SejilService
                ?? throw new InvalidOperationException("SejilService is not registered: call services.AddSejilServices().");

            // 503 when the batch could not be written: the sender keeps it and retries.
            context.Response.StatusCode = await service.TryEmitBatchAsync(events, sourceApp)
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;
        }

        private static async Task BadRequestAsync(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(message);
        }
    }
}
