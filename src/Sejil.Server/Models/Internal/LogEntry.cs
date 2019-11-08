// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Dapper.Contrib.Extensions;

namespace SejilSQL.Models.Internal
{
    [Table("Journal.LogEntry")]
    public class LogEntry
    {
        public long Id { get; set; }
        public string Message { get; set; }
        public string SourceApp { get; set; }
        public string Level { get; set; }
        public DateTime Timestamp { get; set; }
        public string Exception { get; set; }
        public List<LogEntryProperty> Properties { get; set; } = new List<LogEntryProperty>();
    }
}
