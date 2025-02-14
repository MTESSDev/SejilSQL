// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SejilSQL.Models.Internal;

namespace SejilSQL.Data.Internal
{
    public interface ISejilRepository
    {
        Task<bool> SaveQueryAsync(LogQuery logQuery);
        Task<IEnumerable<LogQuery>> GetSavedQueriesAsync();
        Task<IEnumerable<LogEntry>> GetEventsPageAsync(int page, DateTime? startingTimestamp, LogQueryFilter queryFilter, int? pageSize);
        Task<bool> DeleteQueryAsync(string queryName);
    }
}