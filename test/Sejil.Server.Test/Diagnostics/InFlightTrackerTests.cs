using System;
using System.Diagnostics;
using SejilSQL.Diagnostics;
using Xunit;

namespace SejilSQL.Test.Diagnostics
{
    public class InFlightTrackerTests : IDisposable
    {
        private readonly ActivitySource _source = new ActivitySource("SejilTest.InFlight." + Guid.NewGuid());
        private readonly ActivityListener _listener;
        private readonly InFlightTracker _tracker = new InFlightTracker();

        public InFlightTrackerTests()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == _source.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose()
        {
            _listener.Dispose();
            _source.Dispose();
        }

        private Activity Begin(string request, TimeSpan age, ActivityKind kind = ActivityKind.Server)
        {
            var activity = _source.CreateActivity("Microsoft.AspNetCore.Hosting.HttpRequestIn", kind);
            activity.SetStartTime(DateTime.UtcNow - age);
            activity.SetTag(InFlightTracker.RequestTag, request);
            activity.Start();
            _tracker.Started(activity);
            return activity;
        }

        [Fact]
        public void Nothing_in_flight_says_nothing()
        {
            Assert.Null(_tracker.Summary());
            Assert.Equal(0, _tracker.Count);
        }

        [Fact]
        public void The_routes_with_the_most_requests_come_first_with_their_oldest()
        {
            for (var i = 0; i < 3; i++) Begin("GET /block", TimeSpan.FromSeconds(7 + i));
            Begin("GET /slow", TimeSpan.FromSeconds(1.2));

            Assert.Equal("3× GET /block (oldest 9 s); 1× GET /slow (oldest 1.2 s)", _tracker.Summary());
            Assert.Equal(4, _tracker.Count);
        }

        [Fact]
        public void A_request_that_ends_is_no_longer_in_flight()
        {
            var done = Begin("GET /fast", TimeSpan.Zero);
            Begin("GET /block", TimeSpan.FromSeconds(3));

            _tracker.Stopped(done);

            Assert.Equal(1, _tracker.Count);
            Assert.DoesNotContain("/fast", _tracker.Summary());
        }

        [Fact]
        public void Only_incoming_requests_count_not_the_calls_they_make()
        {
            Begin("GET /orders", TimeSpan.FromSeconds(1));
            Begin("GET http://inventory/", TimeSpan.FromSeconds(1), ActivityKind.Client);

            Assert.Equal(1, _tracker.Count);
        }

        [Fact]
        public void Only_the_busiest_routes_are_listed_and_the_rest_counted()
        {
            for (var route = 0; route < 8; route++) Begin($"GET /r{route}", TimeSpan.FromSeconds(1));

            var summary = _tracker.Summary(top: 3);

            Assert.Equal(3, summary.Split(';').Length - 1);   // three routes, then the note
            Assert.EndsWith("… 5 more routes", summary);
        }
    }
}
