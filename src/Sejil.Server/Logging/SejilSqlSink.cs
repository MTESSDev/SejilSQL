// Copyright (C) 2017 Alaa Masoud
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SejilSQL.Service;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace SejilSQL.Logging
{
    /// <summary>
    /// Writes the events straight to the SejilSQL database from the application, with no API in between.
    /// Emit never blocks and never throws: events wait in a bounded queue, a background task writes them in batches
    /// (one round trip each), a batch that cannot be written is kept and tried again, and when the queue is full
    /// the newest events are dropped (and counted in the Serilog SelfLog) rather than slowing the application down.
    /// </summary>
    internal sealed class SejilSqlSink : ILogEventSink, IDisposable
    {
        private readonly Channel<LogEvent> _queue;
        private readonly Func<IList<Event>, Task<bool>> _write;
        private readonly int _batchSize;
        private readonly TimeSpan _period;
        private readonly TimeSpan _retryDelay;
        private readonly TimeSpan _flushTimeout;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Task _worker;
        private long _dropped;
        private volatile bool _closing;

        /// <summary>Disposed with the sink (the watcher of the shared minimum level).</summary>
        public IDisposable Companion { get; set; }

        public SejilSqlSink(Func<IList<Event>, Task<bool>> write, int batchSize, TimeSpan period, int queueLimit, TimeSpan retryDelay, TimeSpan flushTimeout)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _batchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));
            _period = period;
            _retryDelay = retryDelay;
            _flushTimeout = flushTimeout;
            _queue = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(queueLimit > 0 ? queueLimit : throw new ArgumentOutOfRangeException(nameof(queueLimit)))
            {
                FullMode = BoundedChannelFullMode.Wait, // TryWrite then returns false when full: that is how drops are counted
                SingleReader = true,
            });
            _worker = Task.Run(RunAsync);
        }

        public void Emit(LogEvent logEvent)
        {
            if (!_queue.Writer.TryWrite(logEvent))
            {
                Interlocked.Increment(ref _dropped);
            }
        }

        private async Task RunAsync()
        {
            var reader = _queue.Reader;
            var batch = new List<Event>(_batchSize);

            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (batch.Count < _batchSize && reader.TryRead(out var logEvent))
                    {
                        batch.Add(LogEventConverter.ToEvent(logEvent));
                    }

                    while (!await TryWriteAsync(batch).ConfigureAwait(false))
                    {
                        // At shutdown the batch is still retried, until the flush timeout of Dispose cancels the wait.
                        if (_stop.IsCancellationRequested)
                        {
                            SelfLog.WriteLine("SejilSQL: {0} log events could not be written and were lost at shutdown.", batch.Count);
                            return;
                        }

                        await DelayAsync(_retryDelay).ConfigureAwait(false);
                    }

                    batch.Clear();
                    ReportDropped();

                    if (!_closing)
                    {
                        await DelayAsync(_period).ConfigureAwait(false); // lets a burst of events gather into one batch
                    }
                }
            }
            catch (Exception e)
            {
                SelfLog.WriteLine("SejilSQL: the writer stopped: {0}", e);
            }
        }

        private async Task<bool> TryWriteAsync(IList<Event> batch)
        {
            try
            {
                return await _write(batch).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                SelfLog.WriteLine("SejilSQL: could not write {0} log events: {1}", batch.Count, e.Message);
                return false;
            }
        }

        private async Task DelayAsync(TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        private void ReportDropped()
        {
            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                SelfLog.WriteLine("SejilSQL: {0} log events were dropped because the queue was full.", dropped);
            }
        }

        public void Dispose()
        {
            _closing = true;
            _queue.Writer.TryComplete();

            // Flush what is queued; if the database stays unreachable, stop waiting after the timeout.
            if (!_worker.Wait(_flushTimeout))
            {
                _stop.Cancel();
                _worker.Wait(TimeSpan.FromSeconds(1));
            }

            ReportDropped();
            Companion?.Dispose();
            _stop.Dispose();
        }
    }
}
