// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Text.Json;
using SejilSQL.Service;
using Serilog.Events;
using Xunit;

namespace SejilSQL.Test.Service
{
    public class SejilEventReaderTests
    {
        [Fact]
        public void Read_reads_every_field_of_an_event()
        {
            var events = Read(@"[{
                ""Timestamp"":""2026-10-07T21:48:54.8749510Z"", ""Level"":""Error"", ""MessageTemplate"":""ignored {X}"",
                ""RenderedMessage"":""Step failed"", ""Exception"":""System.TimeoutException: timeout"", ""Properties"":{""X"":""y""}}]");

            var e = Assert.Single(events);
            Assert.Equal(new DateTimeOffset(2026, 10, 7, 21, 48, 54, TimeSpan.Zero).AddTicks(8749510), e.Timestamp);
            Assert.Equal(LogEventLevel.Error, e.Level);
            Assert.Equal("Step failed", e.RenderedMessage);
            Assert.Equal("System.TimeoutException: timeout", e.Exception);
            Assert.Equal("y", e.Properties["X"]);
        }

        [Fact]
        public void Read_keeps_the_values_as_the_sender_wrote_them_whatever_the_culture()
        {
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("fr-CA");   // decimal comma
            try
            {
                var e = Assert.Single(Read(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":""Information"",""Properties"":{
                    ""Text"":""a text"", ""Int"":3, ""Decimal"":12.5, ""Bool"":true, ""Date"":""2026-10-07T21:48:00Z"",
                    ""Object"":{""a"":1,""b"":[1,2,3],""c"":""x""}, ""Array"":[1,2]}}]"));

                Assert.Equal("a text", e.Properties["Text"]);
                Assert.Equal("3", e.Properties["Int"]);
                Assert.Equal("12.5", e.Properties["Decimal"]);
                Assert.Equal("true", e.Properties["Bool"]);
                Assert.Equal("2026-10-07T21:48:00Z", e.Properties["Date"]);
                Assert.Equal(@"{""a"":1,""b"":[1,2,3],""c"":""x""}", e.Properties["Object"]);
                Assert.Equal("[1,2]", e.Properties["Array"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [Fact]
        public void Read_stores_a_json_null_property_as_null()
        {
            var e = Assert.Single(Read(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":""Information"",""Properties"":{""Nothing"":null}}]"));

            Assert.True(e.Properties.ContainsKey("Nothing"));
            Assert.Null(e.Properties["Nothing"]);
        }

        [Fact]
        public void Read_matches_names_without_regard_to_case()
        {
            var e = Assert.Single(Read(@"[{""timestamp"":""2026-10-07T21:48:54Z"",""level"":""warning"",""renderedMessage"":""m"",""properties"":{""P"":""v""}}]"));

            Assert.Equal(LogEventLevel.Warning, e.Level);
            Assert.Equal("m", e.RenderedMessage);
            Assert.Equal("v", e.Properties["P"]);
        }

        [Theory]
        [InlineData("3", LogEventLevel.Warning)]
        [InlineData("\"Fatal\"", LogEventLevel.Fatal)]
        public void Read_accepts_a_level_name_or_number(string level, LogEventLevel expected)
        {
            var e = Assert.Single(Read(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":" + level + "}]"));

            Assert.Equal(expected, e.Level);
        }

        [Fact]
        public void Read_leaves_properties_null_when_there_are_none()
        {
            var e = Assert.Single(Read(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":""Debug""}]"));

            Assert.Null(e.Properties);
            Assert.Null(e.Exception);
        }

        [Theory]
        [InlineData(@"[1]")]
        [InlineData(@"[{""Level"":""Debug""}]")]
        [InlineData(@"[{""Timestamp"":""yesterday"",""Level"":""Debug""}]")]
        [InlineData(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":""Loud""}]")]
        [InlineData(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":42}]")]
        [InlineData(@"[{""Timestamp"":""2026-10-07T21:48:54Z"",""Level"":""Debug"",""Properties"":[1]}]")]
        public void Read_throws_FormatException_for_a_malformed_event(string json)
        {
            Assert.Throws<FormatException>(() => Read(json));
        }

        private static System.Collections.Generic.List<Event> Read(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                return SejilEventReader.Read(document.RootElement);
            }
        }
    }
}
