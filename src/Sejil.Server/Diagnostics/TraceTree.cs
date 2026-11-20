using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SejilSQL.Diagnostics
{
    /// <summary>What is kept of a span once it has ended: enough to draw the tree of a slow request.</summary>
    internal sealed class SpanRecord
    {
        public string SpanId { get; set; }
        public string ParentSpanId { get; set; }
        public string Name { get; set; }

        /// <summary>The status code, or the reason it failed ("200", "503", "TimeoutException"). Null when there is nothing to say.</summary>
        public string Detail { get; set; }

        public DateTime StartUtc { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Failed { get; set; }

        public static SpanRecord From(Activity activity)
            => new SpanRecord
            {
                SpanId = activity.SpanId.ToString(),
                ParentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString(),
                Name = NameOf(activity),
                Detail = DetailOf(activity),
                StartUtc = activity.StartTimeUtc,
                Duration = activity.Duration,
                Failed = activity.Status == ActivityStatusCode.Error,
            };

        /// <summary>
        /// A readable name. The span of an incoming request is named by <see cref="SlowTraceMiddleware"/> ("GET /orders/{id}").
        /// An outgoing HTTP call is "GET http://host/path" (without its query string, which can hold secrets).
        /// </summary>
        public static string NameOf(Activity activity)
        {
            if (activity.Kind == ActivityKind.Client && activity.GetTagItem("http.request.method") is string method)
            {
                var url = activity.GetTagItem("url.full") as string;
                var target = !string.IsNullOrEmpty(url)
                    ? url.Split('?')[0]
                    : activity.GetTagItem("server.address") as string;
                return string.IsNullOrEmpty(target) ? method : method + " " + target;
            }

            return activity.DisplayName;
        }

        private static string DetailOf(Activity activity)
        {
            if (activity.GetTagItem("http.response.status_code") is object code)
            {
                return Convert.ToString(code, CultureInfo.InvariantCulture);
            }

            if (activity.GetTagItem("error.type") is string error)
            {
                return error;
            }

            return activity.Status == ActivityStatusCode.Error && !string.IsNullOrEmpty(activity.StatusDescription) ? activity.StatusDescription : null;
        }
    }

    /// <summary>Draws the steps of a request as text, with the time each one took and when it started.</summary>
    internal static class TraceTree
    {
        /// <summary>
        /// <code>
        /// GET /orders/{id}  1243 ms [200]
        /// ├─ GET http://inventory/api/stock  820 ms [200]  (+5 ms)
        /// └─ POST http://payments/charge  310 ms [503]  (+830 ms)
        /// </code>
        /// A span whose parent was not recorded (it comes from a source that is not listened to) hangs from the root.
        /// </summary>
        public static string Render(SpanRecord root, IReadOnlyList<SpanRecord> spans, int maxLines)
        {
            var known = new HashSet<string>(spans.Select(s => s.SpanId)) { root.SpanId };
            var children = spans
                .GroupBy(s => s.ParentSpanId != null && known.Contains(s.ParentSpanId) ? s.ParentSpanId : root.SpanId)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StartUtc).ToList());

            var text = new StringBuilder();
            text.Append(Line(root, root));
            var lines = 1;
            var shown = 0;

            void Walk(string parentId, string prefix)
            {
                if (!children.TryGetValue(parentId, out var kids))
                {
                    return;
                }

                for (var i = 0; i < kids.Count; i++)
                {
                    if (lines >= maxLines)
                    {
                        return;
                    }

                    var last = i == kids.Count - 1;
                    text.Append('\n').Append(prefix).Append(last ? "└─ " : "├─ ").Append(Line(kids[i], root));
                    lines++;
                    shown++;
                    Walk(kids[i].SpanId, prefix + (last ? "   " : "│  "));
                }
            }

            Walk(root.SpanId, "");

            if (shown < spans.Count)
            {
                text.Append("\n… ").Append(spans.Count - shown).Append(" more");
            }

            return text.ToString();
        }

        /// <summary>The step that took the longest, the root excluded; null when there are none.</summary>
        public static SpanRecord Slowest(IReadOnlyList<SpanRecord> spans)
            => spans.OrderByDescending(s => s.Duration).FirstOrDefault();

        private static string Line(SpanRecord span, SpanRecord root)
        {
            var line = new StringBuilder();
            line.Append(span.Name).Append("  ").Append(Math.Round(span.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append(" ms");

            if (span.Detail != null)
            {
                line.Append(" [").Append(span.Detail).Append(']');
            }

            if (span.Failed)
            {
                line.Append(" FAILED");
            }

            if (!ReferenceEquals(span, root))
            {
                var offset = Math.Max(0, Math.Round((span.StartUtc - root.StartUtc).TotalMilliseconds));
                line.Append("  (+").Append(offset.ToString(CultureInfo.InvariantCulture)).Append(" ms)");
            }

            return line.ToString();
        }
    }
}
