using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Quartz.Impl.AdoJobStore;
using Quartz.Spi.MongoDbJobStore.Util;

namespace Quartz.Spi.MongoDbJobStore
{
    internal class MisfireHandler : QuartzThread
    {
        private static ILogger Log => JobStoreLogging.For<MisfireHandler>();

        private readonly MongoDbJobStore _jobStore;
        private volatile bool _shutdown;
        private int _numFails;

        public MisfireHandler(MongoDbJobStore jobStore)
        {
            _jobStore = jobStore;
            Name = $"QuartzScheduler_{jobStore.InstanceName}-{jobStore.InstanceId}_MisfireHandler";
            IsBackground = true;
        }

        public void Shutdown()
        {
            _shutdown = true;
            Interrupt();
        }

        public override void Run()
        {
            while (!_shutdown)
            {
                var now = DateTime.UtcNow;
                var recoverResult = Manage();
                if (recoverResult.ProcessedMisfiredTriggerCount > 0)
                {
                    _jobStore.SignalSchedulingChangeImmediately(recoverResult.EarliestNewTime);
                }

                if (!_shutdown)
                {
                    var timeToSleep = TimeSpan.FromMilliseconds(50);
                    if (!recoverResult.HasMoreMisfiredTriggers)
                    {
                        timeToSleep = _jobStore.MisfireThreshold - (DateTime.UtcNow - now);
                        if (timeToSleep <= TimeSpan.Zero)
                        {
                            timeToSleep = TimeSpan.FromMilliseconds(50);
                        }

                        if (_numFails > 0)
                        {
                            timeToSleep = _jobStore.DbRetryInterval > timeToSleep
                                ? _jobStore.DbRetryInterval
                                : timeToSleep;
                        }
                    }

                    try
                    {
                        Thread.Sleep(timeToSleep);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }
            }
        }

        private RecoverMisfiredJobsResult Manage()
        {
            try
            {
                Log.LogDebug("Scanning for misfires...");
                var result = _jobStore.DoRecoverMisfires().Result;
                _numFails = 0;
                return result;
            }
            catch (Exception ex) when (_shutdown && IsInterruption(ex))
            {
                // Shutdown() interrupts this thread on purpose, so the interruption is the
                // expected exit path rather than a misfire-handling failure.
                Log.LogDebug("Misfire scan interrupted by shutdown");
            }
            catch (Exception ex)
            {
                if (_numFails%_jobStore.RetryableActionErrorLogThreshold == 0)
                {
                    Log.LogError(ex, "Error handling misfires");
                }
                _numFails++;
            }

            return RecoverMisfiredJobsResult.NoOp;
        }

        /// <summary>
        ///     Detects a <see cref="ThreadInterruptedException" />, either thrown directly at the point
        ///     where this thread blocks, or captured into the awaited task and surfaced wrapped in an
        ///     <see cref="AggregateException" />.
        /// </summary>
        private static bool IsInterruption(Exception exception)
        {
            if (exception is ThreadInterruptedException)
            {
                return true;
            }

            return exception is AggregateException aggregateException &&
                   aggregateException.Flatten().InnerExceptions.Any(inner => inner is ThreadInterruptedException);
        }
    }
}
