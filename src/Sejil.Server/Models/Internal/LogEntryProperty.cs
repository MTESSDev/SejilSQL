// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using Dapper.Contrib.Extensions;
using System;

namespace SejilSQL.Models.Internal
{
    [Table("Journal.LogEntryProperty")]
    public class LogEntryProperty
    {
        public long LogId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
