// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using SejilSQL.Configuration;
using SejilSQL.Data.Internal;
using SejilSQL.Models.Internal;
using System.Text.Json;

namespace SejilSQL.Routing.Internal
{
    public class SejilController : ISejilController
    {
        private readonly ISejilRepository _repository;
        private readonly ISejilSettings _settings;
        private HttpContext _context { get; set; }

        public SejilController(IHttpContextAccessor contextAccessor, ISejilRepository repository, ISejilSettings settings)
            => (_context, _repository, _settings) = (contextAccessor.HttpContext, repository, settings);

        public async Task GetIndexAsync()
        {
            if (!String.IsNullOrWhiteSpace(_settings.AuthenticationScheme) && !_context.User.Identity.IsAuthenticated)
            {
                await _context.ChallengeAsync(_settings.AuthenticationScheme);
            }
            else
            {
                _context.Response.ContentType = "text/html";
                await _context.Response.WriteAsync(_settings.SejilAppHtml);
            }
        }

        public async Task GetEventsAsync(int page, int? pageSize, DateTime? startingTs, LogQueryFilter queryFilter)
        {
            if (pageSize <= 0)
            {
                pageSize = _settings.PageSize;
            }
            var events = await _repository.GetEventsPageAsync(page == 0 ? 1 : page, startingTs, queryFilter, pageSize);

            _context.Response.ContentType = "application/json";
            await _context.Response.WriteAsync(JsonSerializer.Serialize(events, ApplicationBuilderExtensions._camelCaseJson));
        }

        public async Task SaveQueryAsync(LogQuery logQuery) => 
            _context.Response.StatusCode = await _repository.SaveQueryAsync(logQuery)
                ? StatusCodes.Status201Created
                : StatusCodes.Status500InternalServerError;

        public async Task GetQueriesAsync()
        {
            var logQueryList = await _repository.GetSavedQueriesAsync();
            _context.Response.ContentType = "application/json";
            await _context.Response.WriteAsync(JsonSerializer.Serialize(logQueryList, ApplicationBuilderExtensions._camelCaseJson));
        }

        public async Task GetMinimumLogLevelAsync()
        {
            var response = new
            {
                MinimumLogLevel = _settings.LoggingLevelSwitch.MinimumLevel.ToString()
            };
            _context.Response.ContentType = "application/json";
            await _context.Response.WriteAsync(JsonSerializer.Serialize(response, ApplicationBuilderExtensions._camelCaseJson));
        }

        public async Task GetUserNameAsync()
        {
            var response = new
            {
                UserName = !String.IsNullOrWhiteSpace(_settings.AuthenticationScheme) && _context.User.Identity.IsAuthenticated
                    ? _context.User.Identity.Name
                    : ""
            };

            _context.Response.ContentType = "application/json";
            await _context.Response.WriteAsync(JsonSerializer.Serialize(response, ApplicationBuilderExtensions._camelCaseJson));
        }

        public void SetMinimumLogLevel(string minLogLevel) => 
            _context.Response.StatusCode = _settings.TrySetMinimumLogLevel(minLogLevel)
                ? StatusCodes.Status200OK
                : StatusCodes.Status400BadRequest;

        public async Task DeleteQueryAsync(string queryName)
            => await _repository.DeleteQueryAsync(queryName);

        public async Task GetTitleAsync()
        {
            var response = new
            {
                _settings.Title
            };
            _context.Response.ContentType = "application/json";
            await _context.Response.WriteAsync(JsonSerializer.Serialize(response, ApplicationBuilderExtensions._camelCaseJson));
        }
    }
}