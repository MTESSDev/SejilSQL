using Microsoft.Extensions.Hosting;
using Sejil.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sejil.Service
{
  public  class SejilCleanupService : BackgroundService
    {
        private readonly string _connectionString;
        private readonly SejilService _sejilService;

        public SejilCleanupService(ISejilSettings settings, SejilService sejilService)
        {
            _connectionString = $"DataSource={settings.SqliteDbPath}";
            _sejilService = sejilService;
            //InitializeDatabase();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _sejilService.CleanupDb();
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
