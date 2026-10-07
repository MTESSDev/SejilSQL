// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Serilog.Events;

namespace SejilSQL.Service
{
    /// <summary>
    /// Reads the JSON array sent by Serilog.Sinks.Http. Property names are matched without regard to case.
    /// Property values are kept as the sender wrote them (see <see cref="Event.Properties"/>), independently of the
    /// culture of the server.
    /// </summary>
    internal static class SejilEventReader
    {
        /// <exception cref="FormatException">An event is not shaped as expected.</exception>
        public static List<Event> Read(JsonElement array)
        {
            var events = new List<Event>(array.GetArrayLength());

            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("Each event must be a JSON object.");
                }

                var logEvent = new Event();
                var hasTimestamp = false;

                foreach (var member in element.EnumerateObject())
                {
                    var name = member.Name;

                    if (Is(name, "Timestamp"))
                    {
                        logEvent.Timestamp = ReadTimestamp(member.Value);
                        hasTimestamp = true;
                    }
                    else if (Is(name, "Level"))
                    {
                        logEvent.Level = ReadLevel(member.Value);
                    }
                    else if (Is(name, "RenderedMessage"))
                    {
                        logEvent.RenderedMessage = ReadText(member.Value);
                    }
                    else if (Is(name, "Exception"))
                    {
                        logEvent.Exception = ReadText(member.Value);
                    }
                    else if (Is(name, "Properties"))
                    {
                        logEvent.Properties = ReadProperties(member.Value);
                    }
                }

                if (!hasTimestamp)
                {
                    throw new FormatException("Each event needs a Timestamp.");
                }

                events.Add(logEvent);
            }

            return events;
        }

        private static bool Is(string name, string expected) => name.Equals(expected, StringComparison.OrdinalIgnoreCase);

        private static DateTimeOffset ReadTimestamp(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var timestamp))
            {
                return timestamp;
            }

            throw new FormatException("Timestamp must be an ISO 8601 date and time with an offset.");
        }

        private static LogEventLevel ReadLevel(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String && Enum.TryParse(value.GetString(), true, out LogEventLevel byName))
            {
                return byName;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && Enum.IsDefined(typeof(LogEventLevel), number))
            {
                return (LogEventLevel)number;
            }

            throw new FormatException("Level must be a Serilog level name (Verbose, Debug, Information, Warning, Error, Fatal).");
        }

        private static string ReadText(JsonElement value)
            => value.ValueKind == JsonValueKind.String ? value.GetString()
             : value.ValueKind == JsonValueKind.Null ? null
             : value.GetRawText();

        private static IDictionary<string, string> ReadProperties(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Properties must be a JSON object.");
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                properties[property.Name] = ReadText(property.Value);
            }
            return properties;
        }
    }
}
