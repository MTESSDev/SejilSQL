using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SejilSQL.Diagnostics
{
    /// <summary>
    /// Settings of <c>AddSejilSlowTraces</c>: which slow or failed operations become log events, and when the thread pool is reported as starved.
    /// Nothing is kept for the operations that are fast and succeed.
    /// </summary>
    public sealed class SejilTraceOptions
    {
        /// <summary>An operation (HTTP request, outgoing HTTP call...) that lasts this long or more is logged as a Warning. 1 second by default.</summary>
        public TimeSpan SlowThreshold { get; set; } = TimeSpan.FromMilliseconds(1000);

        /// <summary>
        /// The most lines of the tree of steps written with a slow request (<c>Trace</c> property): the request itself, then its calls
        /// and what they called. Beyond it, "… N more". 40 by default.
        /// </summary>
        public int TraceTreeMaxLines { get; set; } = 40;

        /// <summary>Also log the operations that ended in error (Error level), whatever their duration. True by default.</summary>
        public bool LogErrors { get; set; } = true;

        /// <summary>
        /// The <see cref="ActivitySource"/> names listened to. By default ASP.NET Core (incoming requests) and HttpClient (outgoing calls).
        /// Add the name of your own sources, or of a library that emits spans (for example Microsoft.Data.SqlClient or an OpenTelemetry instrumentation).
        /// </summary>
        public ISet<string> ActivitySources { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "Microsoft.AspNetCore",
            "System.Net.Http",
        };

        /// <summary>
        /// The tags of a span copied as properties of the log event. A short list on purpose: tags can carry personal data
        /// (full URLs, SQL text...). Add the ones you want.
        /// </summary>
        public ISet<string> Tags { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "http.request.method",
            "http.route",
            "url.path",
            "http.response.status_code",
            "server.address",
            "error.type",
        };

        /// <summary>
        /// Return false to ignore a span. Use it to keep the logs from feeding themselves: if the logs are sent by <c>Serilog.Sinks.Http</c>,
        /// ignore the calls to the log site (for example <c>a => a.GetTagItem("url.path") as string == "/log"</c>).
        /// </summary>
        public Func<Activity, bool> Filter { get; set; }

        /// <summary>Watch the thread pool and log when it is starved. True by default.</summary>
        public bool ThreadPoolWatcher { get; set; } = true;

        /// <summary>How often the thread pool is probed. 5 seconds by default.</summary>
        public TimeSpan ThreadPoolSamplePeriod { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A work item queued to the thread pool that waits this long before it starts counts as a symptom of starvation.
        /// A healthy pool starts it within a few milliseconds. 250 ms by default.
        /// </summary>
        public TimeSpan ThreadPoolLatencyThreshold { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>A queue of this many work items or more waiting for a thread also counts as a symptom. 50 by default.</summary>
        public int ThreadPoolPendingThreshold { get; set; } = 50;

        /// <summary>Number of consecutive probes with a symptom before a warning is logged, so that a brief spike is ignored. 2 by default.</summary>
        public int ThreadPoolConsecutiveSamples { get; set; } = 2;

        /// <summary>While the starvation lasts, the warning is repeated at most this often. 1 minute by default.</summary>
        public TimeSpan ThreadPoolRepeatEvery { get; set; } = TimeSpan.FromMinutes(1);
    }
}
