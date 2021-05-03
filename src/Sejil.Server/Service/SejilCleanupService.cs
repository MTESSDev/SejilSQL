using Microsoft.Extensions.Hosting;
using SejilSQL.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SejilSQL.Service
{
    public class SejilCleanupService : BackgroundService
    {
        private readonly string _connectionString;
        private readonly SejilService _sejilService;

        public SejilCleanupService(ISejilSettings settings, SejilService sejilService)
        {
            _connectionString = settings.ConnectionString;
            _sejilService = sejilService;
            //InitializeDatabase();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _sejilService.CleanupDb();
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
