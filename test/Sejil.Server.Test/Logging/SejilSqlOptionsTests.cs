// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using SejilSQL.Logging;
using Xunit;

namespace SejilSQL.Test.Logging
{
    public class SejilSqlOptionsTests
    {
        private static IConfiguration Config(params (string Key, string Value)[] values)
        {
            var data = new Dictionary<string, string>();
            foreach (var (key, value) in values)
            {
                data[key] = value;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        private static SejilSqlOptions Resolve(IConfiguration configuration, string section = null, string connectionString = null, string sourceApp = null, string levelId = null,
            int batchSize = -1, int queueLimit = -1, int periodSeconds = -1, int levelPollSeconds = -1, int flushTimeoutSeconds = -1)
            => SejilSqlOptions.Resolve(configuration, section, connectionString, sourceApp, levelId, batchSize, queueLimit, periodSeconds, levelPollSeconds, flushTimeoutSeconds);

        // ---------- la chaîne de connexion ----------

        [Fact]
        public void A_connection_string_passed_as_is_is_used_without_any_configuration()
        {
            var options = Resolve(null, connectionString: "Server=.;Database=Journal;Integrated Security=True");

            Assert.Equal("Server=.;Database=Journal;Integrated Security=True", options.ConnectionString);
        }

        [Fact]
        public void A_name_passed_is_looked_up_in_ConnectionStrings_like_the_sql_server_sink_does()
        {
            var options = Resolve(Config(("ConnectionStrings:Journal", "Server=a;Database=J")), connectionString: "Journal");

            Assert.Equal("Server=a;Database=J", options.ConnectionString);
        }

        [Fact]
        public void An_unknown_name_says_which_key_was_looked_for()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Resolve(Config(("ConnectionStrings:Other", "x=y")), connectionString: "Journal"));

            Assert.Contains("ConnectionStrings:Journal", e.Message);
        }

        [Fact]
        public void With_nothing_passed_the_connection_string_is_read_from_the_SejilSql_section()
        {
            var options = Resolve(Config(("SejilSql:ConnectionString", "Server=s;Database=J")));

            Assert.Equal("Server=s;Database=J", options.ConnectionString);
        }

        [Fact]
        public void With_nothing_passed_it_falls_back_to_the_native_ConnectionStrings_entry()
        {
            var options = Resolve(Config(("ConnectionStrings:SejilSql", "Server=n;Database=J")));

            Assert.Equal("Server=n;Database=J", options.ConnectionString);
        }

        [Fact]
        public void The_section_wins_over_the_native_entry()
        {
            var options = Resolve(Config(("SejilSql:ConnectionString", "Server=section"), ("ConnectionStrings:SejilSql", "Server=native")));

            Assert.Equal("Server=section", options.ConnectionString);
        }

        [Fact]
        public void With_nothing_configured_the_error_lists_both_keys()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Resolve(Config(("Other:Key", "x"))));

            Assert.Contains("SejilSql:ConnectionString", e.Message);
            Assert.Contains("ConnectionStrings:SejilSql", e.Message);
        }

        [Fact]
        public void Without_a_connection_string_nor_a_configuration_the_error_says_how_to_give_one()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Resolve(null));

            Assert.Contains("configuration", e.Message);
        }

        [Fact]
        public void Another_section_can_be_named_to_reuse_keys_an_application_already_has()
        {
            var options = Resolve(
                Config(("FRW:Logs:ConnectionString", "Server=frw;Database=J"), ("FRW:Logs:LevelId", "LOGLVLPOSTE"), ("FRW:Logs:SourceApp", "FRW.Proxy")),
                section: "FRW:Logs");

            Assert.Equal("Server=frw;Database=J", options.ConnectionString);
            Assert.Equal("LOGLVLPOSTE", options.LevelId);
            Assert.Equal("FRW.Proxy", options.SourceApp);
        }

        // ---------- le reste : explicite, puis configuration, puis défaut ----------

        [Fact]
        public void Defaults_apply_when_nothing_is_configured()
        {
            var options = Resolve(Config(("SejilSql:ConnectionString", "Server=s")));

            Assert.Equal(200, options.BatchSize);
            Assert.Equal(10000, options.QueueLimit);
            Assert.Equal(2, options.PeriodSeconds);
            Assert.Equal(60, options.LevelPollSeconds);
            Assert.Equal(10, options.FlushTimeoutSeconds);
            Assert.Null(options.LevelId);
            Assert.False(string.IsNullOrWhiteSpace(options.SourceApp));
        }

        [Fact]
        public void Optional_keys_are_read_from_the_section()
        {
            var options = Resolve(Config(
                ("SejilSql:ConnectionString", "Server=s"), ("SejilSql:SourceApp", "MyApp"), ("SejilSql:LevelId", "MyApp"), ("SejilSql:BatchSize", "50"),
                ("SejilSql:QueueLimit", "500"), ("SejilSql:PeriodSeconds", "0"), ("SejilSql:LevelPollSeconds", "5"), ("SejilSql:FlushTimeoutSeconds", "3")));

            Assert.Equal("MyApp", options.SourceApp);
            Assert.Equal("MyApp", options.LevelId);
            Assert.Equal(50, options.BatchSize);
            Assert.Equal(500, options.QueueLimit);
            Assert.Equal(0, options.PeriodSeconds);
            Assert.Equal(5, options.LevelPollSeconds);
            Assert.Equal(3, options.FlushTimeoutSeconds);
        }

        [Fact]
        public void What_is_passed_explicitly_wins_over_the_configuration()
        {
            var options = Resolve(
                Config(("SejilSql:ConnectionString", "Server=config"), ("SejilSql:SourceApp", "FromConfig"), ("SejilSql:BatchSize", "50"), ("SejilSql:PeriodSeconds", "9")),
                connectionString: "Server=code;Database=J", sourceApp: "FromCode", batchSize: 10, periodSeconds: 0);

            Assert.Equal("Server=code;Database=J", options.ConnectionString);
            Assert.Equal("FromCode", options.SourceApp);
            Assert.Equal(10, options.BatchSize);
            Assert.Equal(0, options.PeriodSeconds); // an explicit 0 is a value, not "not passed"
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("-5")]
        [InlineData("")]
        public void A_number_that_cannot_be_used_is_ignored_in_favour_of_the_default(string configured)
        {
            var options = Resolve(Config(("SejilSql:ConnectionString", "Server=s"), ("SejilSql:BatchSize", configured)));

            Assert.Equal(200, options.BatchSize);
        }
    }
}
