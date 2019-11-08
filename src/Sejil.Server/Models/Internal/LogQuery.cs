// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using Dapper.Contrib.Extensions;
using System;
using System.Collections.Generic;

namespace SejilSQL.Models.Internal
{
    [Table("Journal.Log_Query")]
    public class LogQuery
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Query { get; set; }
    }
}
