// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using SejilSQL.Service;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace SejilSQL.Logging
{
    /// <summary>
    /// Turns a Serilog event into what the receiving route stores for the same event sent through Serilog.Sinks.Http,
    /// so that both ways of writing leave identical rows: property values as the text the sender wrote
    /// (a string as is, anything else as its JSON text, null as NULL), whatever the culture of the machine.
    /// </summary>
    internal static class LogEventConverter
    {
        private static readonly JsonValueFormatter Json = new JsonValueFormatter(typeTagName: "$type");

        public static Event ToEvent(LogEvent logEvent)
        {
            Dictionary<string, string> properties = null;
            if (logEvent.Properties.Count > 0)
            {
                properties = new Dictionary<string, string>(logEvent.Properties.Count, StringComparer.Ordinal);
                foreach (var property in logEvent.Properties)
                {
                    properties[property.Key] = ToText(property.Value);
                }
            }

            return new Event
            {
                Timestamp = logEvent.Timestamp,
                Level = logEvent.Level,
                RenderedMessage = logEvent.RenderMessage(CultureInfo.InvariantCulture),
                Exception = logEvent.Exception?.ToString(),
                Properties = properties,
            };
        }

        public static string ToText(LogEventPropertyValue value)
        {
            var writer = new StringWriter(CultureInfo.InvariantCulture);
            Json.Format(value, writer);
            var json = writer.ToString();

            if (json == "null")
            {
                return null;
            }

            return json.Length > 0 && json[0] == '"' ? JsonSerializer.Deserialize<string>(json) : json;
        }
    }
}
