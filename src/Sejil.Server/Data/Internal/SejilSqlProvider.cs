// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using SejilSQL.Configuration;
using SejilSQL.Models.Internal;
using Serilog.Events;

namespace SejilSQL.Data.Internal
{
    public class SejilSqlProvider : ISejilSqlProvider
    {
        private readonly ISejilSettings _settings;

        public SejilSqlProvider(ISejilSettings settings) => _settings = settings;

        public string GetSavedQueriesSql()
            => "SELECT * FROM [Journal].log_query with (nolock)";

        public string InsertLogQuerySql()
            => "INSERT INTO [Journal].log_query (name, query) VALUES (@name, @query)";

        public string DeleteQuerySql()
            => "DELETE FROM [Journal].log_query WHERE name = @name";

        public string GetLogLevelSql()
            => "SELECT value FROM [Journal].log_config WHERE id = @id";

        public string SetLogLevelSql()
            => "UPDATE [Journal].log_config SET value = @value WHERE id = @id " +
               "IF @@ROWCOUNT = 0 INSERT INTO [Journal].log_config (id, value) VALUES (@id, @value)";

        public string GetPagedLogEntriesSql(int page, int pageSize, DateTime? startingTimestamp, LogQueryFilter queryFilter, DynamicParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (page <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(page), "Argument must be greater than zero.");
            }

            if (pageSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize), "Argument must be greater than zero.");
            }

            var timestampWhereClause = TimestampWhereClause();
            var queryWhereClause = QueryWhereClause();

            return
$@"SELECT l.*, p.* from 
(
    SELECT * FROM [Journal].log with (nolock)
    {timestampWhereClause}
    {queryWhereClause}{FiltersWhereClause()}
    ORDER BY timestamp DESC, id DESC
    OFFSET {(long)(page - 1) * pageSize} ROWS FETCH NEXT {pageSize} ROWS ONLY
) l
LEFT JOIN [Journal].log_property p with (nolock) ON l.timestamp = p.timestamp AND l.id = p.logId
ORDER BY l.timestamp DESC, l.id DESC
OPTION (LOOP JOIN)";
            // Never sort by property name here: the rows carry message and exception (nvarchar(max)), so that sort asks for a huge
            // memory grant, and on a busy server the request waits about 25 seconds before it is granted. The properties of an
            // event are sorted by name in SejilRepository instead. LOOP JOIN keeps the order of the page, with no sort at all.

            string TimestampWhereClause()
            {
                var hasStartingTimestampConstraint = startingTimestamp.HasValue;
                var hasDateFilter = queryFilter?.DateFilter != null || queryFilter?.DateRangeFilter != null;

                var sql = new StringBuilder();

                if (hasStartingTimestampConstraint || hasDateFilter)
                {
                    sql.Append("WHERE (");
                }

                if (hasStartingTimestampConstraint)
                {
                    sql.Append($@"timestamp < '{startingTimestamp.Value.ToString("yyyy-MM-dd HH:mm:ss.fff")}'");
                }

                if (hasStartingTimestampConstraint && hasDateFilter)
                {
                    sql.Append(" AND ");
                }

                if (hasDateFilter)
                {
                    sql.Append(BuildDateFilter(queryFilter));
                }

                if (hasStartingTimestampConstraint || hasDateFilter)
                {
                    sql.Append(")");
                }

                return sql.ToString();
            }

            string QueryWhereClause() =>
                String.IsNullOrWhiteSpace(queryFilter?.QueryText)
                    ? ""
                    : timestampWhereClause.Length > 0
                        ? $"AND ({BuildPredicate(queryFilter.QueryText, _settings.NonPropertyColumns, parameters)})"
                        : $"WHERE ({BuildPredicate(queryFilter.QueryText, _settings.NonPropertyColumns, parameters)})";

            string FiltersWhereClause() =>
                String.IsNullOrWhiteSpace(queryFilter?.LevelFilter) && (!queryFilter?.ExceptionsOnly ?? true)
                    ? ""
                    : timestampWhereClause.Length > 0 || queryWhereClause.Length > 0
                        ? $" AND ({BuildFilterWhereClause(queryFilter.LevelFilter, queryFilter.ExceptionsOnly)})"
                        : $"WHERE ({BuildFilterWhereClause(queryFilter.LevelFilter, queryFilter.ExceptionsOnly)})";
        }

        private static string BuildFilterWhereClause(string levelFilter, bool exceptionsOnly)
        {
            var sp = new StringBuilder();

            if (!String.IsNullOrWhiteSpace(levelFilter))
            {
                sp.AppendFormat("level = {0}", (int)Enum.Parse(typeof(LogEventLevel), levelFilter));
            }

            if (exceptionsOnly && sp.Length > 0)
            {
                sp.Append(" AND ");
            }

            if (exceptionsOnly)
            {
                sp.Append("exception is not null");
            }

            return sp.ToString();
        }

        // User supplied values are never concatenated into the SQL: they are added to
        // the parameters and only their placeholder (@q0, @q1, ...) is emitted.
        private static string BuildPredicate(string filterQuery, string[] nonPropertyColumns, DynamicParameters parameters)
        {
            var sb = new StringBuilder();
            BuildPredicateCore(filterQuery, sb);
            return sb.ToString();

            void BuildPredicateCore(string query, StringBuilder sql)
            {
                // and, or and like are whole words (a name such as "formulaire" contains "or"), whatever their case.
                // (...) && (...)  -or-  (...) and (...)
                // (...) || (...)  -or-  (...) or (...)
                var split = Regex.Split(query, @"(\(.+\))\s*(\|\||&&|\band\b|\bor\b)\s*(\(.+\))", RegexOptions.IgnoreCase).Where(p => !String.IsNullOrWhiteSpace(p)).ToArray();
                if (split.Length != 3)
                {
                    // ... && (...)  -or-  ... and (...)
                    // ... || (...)  -or-  ... or (...)
                    split = Regex.Split(query, @"(.+)\s*(\|\||&&|\band\b|\bor\b)\s*(\(.+\))", RegexOptions.IgnoreCase).Where(p => !String.IsNullOrWhiteSpace(p)).ToArray();
                    if (split.Length != 3)
                    {
                        // (...) && ...  -or-  (...) and ..
                        // (...) || ...  -or-  (...) or ..
                        split = Regex.Split(query, @"(\(.+\))\s*(\|\||&&|\band\b|\bor\b)\s*(.+)", RegexOptions.IgnoreCase).Where(p => !String.IsNullOrWhiteSpace(p)).ToArray();
                        if (split.Length != 3)
                        {
                            // ... && ...  -or-  ... and ...
                            // ... || ...  -or-  ... or ...
                            split = Regex.Split(query, @"(.+)\s*(\|\||&&|\band\b|\bor\b)\s*(.+)", RegexOptions.IgnoreCase).Where(p => !String.IsNullOrWhiteSpace(p)).ToArray();
                            if (split.Length != 3)
                            {
                                // name = value
                                // name != value
                                // name like 'value'
                                // name not like 'value'
                                split = Regex.Split(query, @"(\w+)\s*(=|!=|\blike\b|\bnot like\b)\s*(.+)", RegexOptions.IgnoreCase).Where(p => !String.IsNullOrWhiteSpace(p)).ToArray();
                                if (split.Length == 3)
                                {
                                    if (nonPropertyColumns.Contains(split[0].ToLower()))
                                    {
                                        // Column name is safe here: it matched \w+ and is whitelisted by nonPropertyColumns.
                                        sql.AppendFormat("{0} {1} {2}",
                                            split[0], split[1].ToUpper().Trim(), AddParameter(parameters, ColumnValue(split[0], split[2].Trim('"', ' ', '\''))));
                                    }
                                    else
                                    {
                                        sql.AppendFormat("id {0} (SELECT logId FROM [Journal].log_property with (nolock) WHERE name = {1} AND value {2} {3})",
                                            GetInclusionOperator(split[1].Trim().ToLower()), AddParameter(parameters, split[0]), NegateIfNonInclusion(split[1].Trim().ToLower()), AddParameter(parameters, StripQuotes(split[2].Trim())));
                                    }
                                }
                                else if (split.Length == 1)
                                {
                                    // If we get here, then we received just a string. We will search the message column, exception column and all props for matches
                                    var param = AddParameter(parameters, $"%{split[0].Trim()}%");
                                    sql.AppendFormat(
                                        "(message LIKE {0} OR exception LIKE {0} OR " +
                                        "id in (SELECT logId FROM [Journal].log_property with (nolock) WHERE value LIKE {0}))",
                                        param);
                                }
                            }
                            else
                            {
                                BuildPredicateCore(split[0], sql);

                                sql.Append(GetLogicalOperator(split[1].Trim().ToLower()));

                                BuildPredicateCore(split[2], sql);
                            }
                        }
                        else
                        {
                            // Remove leading and trainling parenthesis from left side and add sql parenthesis
                            sql.Append("(");
                            BuildPredicateCore(split[0].Substring(1, split[0].Length - 2), sql);
                            sql.Append(")");

                            sql.Append(GetLogicalOperator(split[1].Trim().ToLower()));

                            BuildPredicateCore(split[2], sql);
                        }
                    }
                    else
                    {
                        BuildPredicateCore(split[0], sql);

                        sql.Append(GetLogicalOperator(split[1].Trim().ToLower()));

                        // Remove leading and trainling parenthesis from right side and add sql parenthesis
                        sql.Append("(");
                        BuildPredicateCore(split[2].Substring(1, split[2].Length - 2), sql);
                        sql.Append(")");
                    }
                }
                else
                {
                    // Remove leading and trainling parenthesis from left side and add sql parenthesis
                    sql.Append("(");
                    BuildPredicateCore(split[0].Substring(1, split[0].Length - 2), sql);
                    sql.Append(")");

                    sql.Append(GetLogicalOperator(split[1].Trim().ToLower()));

                    // Remove leading and trainling parenthesis from right side and add sql parenthesis
                    sql.Append("(");
                    BuildPredicateCore(split[2].Substring(1, split[2].Length - 2), sql);
                    sql.Append(")");
                }
            }
        }

        private static string BuildDateFilter(LogQueryFilter queryFilter)
        {
            if (queryFilter.DateFilter != null)
            {
                switch (queryFilter.DateFilter)
                {
                    case "5m":
                        return "timestamp >= DATEADD(minute, -5, GETDATE())";
                    case "15m":
                        return "timestamp >= DATEADD(minute, -15, GETDATE())";
                    case "1h":
                        return "timestamp >= DATEADD(hour, -1, GETDATE())";
                    case "6h":
                        return "timestamp >= DATEADD(hour, -6, GETDATE())";
                    case "12h":
                        return "timestamp >= DATEADD(hour, -12, GETDATE())";
                    case "24h":
                        return "timestamp >= DATEADD(hour, -24, GETDATE())";
                    case "2d":
                        return "timestamp >= DATEADD(day, -2, GETDATE())";
                    case "5d":
                        return "timestamp >= DATEADD(day, -5, GETDATE())";
                }
            }
            else if (queryFilter.DateRangeFilter != null)
            {
                return $"timestamp >= '{queryFilter.DateRangeFilter[0].ToString("yyyy-MM-dd")}' and timestamp < '{queryFilter.DateRangeFilter[1].ToString("yyyy-MM-dd")}'";
            }

            return "";
        }

        private static string GetLogicalOperator(string op)
            => op == "&&" || op == "and"
                ? " AND "
                : op == "||" || op == "or"
                    ? " OR "
                    // Below condition will never be reached
                    : throw new Exception("Invalid logical operator");

        private static string GetInclusionOperator(string op)
            => op == "=" || op == "like"
                ? "IN"
                : op == "!=" || op == "not like"
                    ? "NOT IN"
                    // Below condition will never be reached
                    : throw new Exception("Invalid logical operator");

        private static string NegateIfNonInclusion(string op)
            => op == "!="
                ? "="
                : op == "not like"
                    ? "LIKE"
                    : op.ToUpper();

        // "..."  -->  ...
        // '...'  -->  ...
        //  ...   -->  ...
        private static string StripQuotes(string value)
            => value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0]
                ? value.Substring(1, value.Length - 2)
                : value;

        // The level column holds a number: let a query say "level = Error" (or Critical, or Trace) as well as "level = 4".
        private static object ColumnValue(string column, string value)
        {
            if (!column.Equals("level", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            switch (value.ToLowerInvariant())
            {
                case "trace": return (int)LogEventLevel.Verbose;
                case "critical": return (int)LogEventLevel.Fatal;
            }

            return Enum.TryParse(value, true, out LogEventLevel level) && Enum.IsDefined(typeof(LogEventLevel), level)
                ? (object)(int)level
                : value;
        }

        private static string AddParameter(DynamicParameters parameters, object value)
        {
            var name = $"@q{parameters.ParameterNames.Count()}";
            parameters.Add(name, value);
            return name;
        }

    }
}