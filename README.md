# Sejil

Sejil is a library that enables you to capture, view and filter your ASP.net core app's log events right from your app. It supports structured logging, querying as well as saving log event queries.

This version let you centralize all your applications logs in one place.

## Quick Links

- [Getting started](#getting-started)
- [Features and Screenshots](#features-and-screenshots)
- [Building](#building)
- [License](#license)

## Getting started

1. Installing [Sejil](https://github.com/TrucsPES/Sejil/releases) package


2. Adding code
    Add below code to **Program.cs**:

    ```csharp
    public static IWebHost BuildWebHost(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
            .AddSejil(new SejilSettings("/sejil", LogEventLevel.Debug));
            // ...
    ```

    Add below code to **Startup.cs**

    ```csharp
    using Sejil;

    public class Startup
    {    
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseSejil();
            // ...
        }
    }
    ```

    (Optional) To require authentication for viewing logs:

    ```csharp
        public void ConfigureServices(IServiceCollection services)
        {
            services.ConfigureSejil(options =>
            {
                options.AuthenticationScheme = /* Your authentication scheme */
            });
        }
    ```

    (Optional) To change the logs page title (Defaults to *Sejil* if not set):

    ```csharp
        public void ConfigureServices(IServiceCollection services)
        {
            services.ConfigureSejil(options =>
            {
                options.Title = "My title";
            });
        }
    ```
    
    (Optional) Hosted Service to cleanup logs DB:

    ```csharp
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<SejilService>();
            services.AddHostedService<SejilCleanupService>();
            services.ConfigureSejil(options =>
            {
                options.Title = "Logs";
                options.LogRetentionDays = 30;
            });
        }
    ```

3. Navigate to *http://your-app-url/sejil* to view your app's logs.

## Upgrading from 2.x

3.0 targets **.NET 10** only (`net10.0`, using the ASP.NET Core shared framework instead of the 2.2 packages) and no longer depends on Newtonsoft.Json.

- **Property values are stored exactly as the sender wrote them**, whatever the culture of the server: `12.5` (not `12,5`), `true` (not `True`), `2026-10-07T21:48:00Z` (not `2026-10-07 21:48:00`), an object as its JSON text, and a JSON `null` as `NULL`. Rows written by 2.x keep their old text, so a search on a decimal or a date will not match both.
- `Event.Properties` is now `IDictionary<string, string>` (it was an `ExpandoObject`), `Renderings` and the types around it are gone, and `SejilService.TryEmitBatchAsync` is new. If you receive the logs yourself (a page or controller calling `EmitBatchAsync`), remove it: call `services.AddSejilServices()` and let `POST /log` do it.
- `ISejilController.SetMinimumLogLevel` became `SetMinimumLogLevelAsync`, and `ISejilSettings` gained `IngestPath`, `LevelId` and `LevelPath` (default interface members, nothing to implement).
- Run [db.sql](./src/Sejil.Server/db.sql) again: it adds `[Journal].log_config`.

## Database

Run [db.sql](./src/Sejil.Server/db.sql) once on the database named in `ConnectionString`. It is idempotent and creates the `[Journal]` schema with `log`, `log_property`, `log_query` and `log_config`. The grouped insert below needs compatibility level 130 (SQL Server 2016) or higher; on a lower level the batch is written row by row, in one transaction.

## Receiving the logs of other applications

Call `services.AddSejilServices()` (registers `SejilService` and the hosted service that deletes events older than `LogRetentionDays`). The site then answers `POST /log?sourceApp=<name>` with the batches sent by [Serilog.Sinks.Http](https://github.com/FantasticFiasco/serilog-sinks-http) (a JSON array, its default format):

```csharp
// in the sending application
.WriteTo.Http(requestUri: "https://logs-site/log?sourceApp=MyApp", queueLimitBytes: 1_048_576)
```

- The route is anonymous (the senders have no identity to present); change it with `IngestPath` on the settings, or set it to `null` to turn it off.
- Timestamps sent in UTC are stored in local time, like the date filters and the cleanup.
- A batch is written atomically in a single round trip (`OPENJSON` + `MERGE ... OUTPUT`: 1000 events with 10 properties each take about half a second over the network, against minutes one row at a time). When it cannot be written the answer is `503`, so the sender keeps the batch and retries without creating duplicates.

## Shared minimum log level

Set `LevelId` on the settings to keep the minimum log level in `[Journal].log_config` instead of in memory. `GET {Url}/min-log-level` then returns `{"minimumLogLevel":"Warning"}` for that identifier, and the level chosen in the page is saved under it. Applications read it periodically and apply it to a `LoggingLevelSwitch`. These two routes are anonymous. If `Url` is behind authentication, expose them elsewhere with `LevelPath` (GET and POST).

## Features and Screenshots

- View your app's logs

    <img src="./assets/001-screenshot-main_opt.jpg" width="800">

- View properties specific to a certain log entry

    <img src="./assets/002-screenshot-properties_opt.jpg" width="800">

- Query your logs

    <img src="./assets/003-screenshot-query_opt.jpg" width="800">

- Mix multiple filters with your query to further limit the results

    <img src="./assets/004-screenshot-query-and-filter_opt.jpg" width="800">

- Save your queries for later use

    <img src="./assets/005-screenshot-save-query_opt.jpg" width="800">

- Load your saved queries

    <img src="./assets/006-screenshot-load-query_opt.jpg" width="800">

## Building

To build the project, you just need to clone the repo then run the build command:

```powershell
git clone https://github.com/alaatm/Sejil.git
cd ./Sejil
./build.ps1  # If running Windows
./build.sh   # If running Linux/OSX
```

You can run one of the sample apps afterwards:

```powershell
cd ./sample/SampleBasic
dotnet run
```

## License

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

Copyright &copy; Alaa Masoud.

This project is provided as-is under the Apache 2.0 license. For more information see the [LICENSE file](https://github.com/alaatm/Sejil/blob/master/LICENSE).
