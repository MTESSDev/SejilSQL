using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace SejilSQL.Diagnostics
{
    /// <summary>
    /// The incoming requests being served right now. When the thread pool is starved this is the first suspect list: a route with
    /// dozens of requests in flight, the oldest several seconds old, is probably holding threads.
    /// <para>
    /// It cannot tell a request that blocks a thread from one that waits for a database without holding any (both are "in flight"):
    /// it names the routes to look at, not the line of code.
    /// </para>
    /// </summary>
    internal sealed class InFlightTracker
    {
        private readonly ConcurrentDictionary<Activity, byte> _active = new ConcurrentDictionary<Activity, byte>();

        /// <summary>The name <see cref="SlowTraceMiddleware"/> puts on the span as soon as the request starts: "GET /orders/42".</summary>
        public const string RequestTag = "sejil.request";

        public int Count => _active.Count;

        public void Started(Activity activity)
        {
            if (activity.Kind == ActivityKind.Server)
            {
                _active[activity] = 0;
            }
        }

        public void Stopped(Activity activity) => _active.TryRemove(activity, out _);

        /// <summary>"78× GET /block (oldest 7.9 s); 3× GET /slow (oldest 1.2 s)", the busiest routes first. Null when nothing is in flight.</summary>
        public string Summary(int top = 5)
        {
            var now = DateTime.UtcNow;

            var groups = _active.Keys
                .Select(a => new { Name = a.GetTagItem(RequestTag) as string ?? a.DisplayName, Age = now - a.StartTimeUtc })
                .GroupBy(r => r.Name)
                .Select(g => new { Name = g.Key, Count = g.Count(), Oldest = g.Max(r => r.Age) })
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g.Oldest)
                .ToList();

            if (groups.Count == 0)
            {
                return null;
            }

            var parts = groups.Take(top).Select(g =>
                $"{g.Count}× {g.Name} (oldest {g.Oldest.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s)");
            var more = groups.Count > top ? $"; … {groups.Count - top} more routes" : "";
            return string.Join("; ", parts) + more;
        }
    }
}
