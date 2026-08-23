using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Quartz.Spi.MongoDbJobStore.Util;

namespace Quartz.Spi.MongoDbJobStore
{
    /// <summary>
    ///     Reports this instance alive on a fixed interval, and reclaims the work of instances that
    ///     stopped reporting.
    /// </summary>
    /// <remarks>
    ///     Quartz leaves this to the job store: the check-in loop and the recovery scan live in
    ///     <c>Quartz.Impl.AdoJobStore</c>, so a store that does not write them gets nothing. Without
    ///     it, a process killed mid-execution leaves its <c>firedTriggers</c> record behind, and a job
    ///     marked <see cref="DisallowConcurrentExecutionAttribute" /> keeps its trigger blocked
    ///     against that execution forever.
    ///     <para>
    ///     Separate from <see cref="MisfireHandler" /> deliberately. That loop paces itself by
    ///     <see cref="MongoDbJobStore.MisfireThreshold" />, a minute by default, and check-ins have to
    ///     be several times more frequent than the interval an instance is declared dead after.
    ///     Sharing the loop would tie the two together and make each one's default wrong for the
    ///     other.
    ///     </para>
    /// </remarks>
    internal class ClusterManager : QuartzThread
    {
        private static ILogger Log => JobStoreLogging.For<ClusterManager>();

        private readonly MongoDbJobStore _jobStore;
        private volatile bool _shutdown;
        private int _numFails;

        public ClusterManager(MongoDbJobStore jobStore)
        {
            _jobStore = jobStore;
            Name = $"QuartzScheduler_{jobStore.InstanceName}-{jobStore.InstanceId}_ClusterManager";
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
                if (Manage())
                {
                    // Something was reclaimed, so there is work that was not there a moment ago.
                    // Left unsaid, the surviving instance would not look again until its next idle
                    // poll, and the whole point of recovering was not to wait.
                    _jobStore.SignalSchedulingChangeImmediately(null);
                }

                if (_shutdown)
                {
                    break;
                }

                var timeToSleep = _jobStore.ClusterCheckinInterval;
                if (_numFails > 0)
                {
                    timeToSleep = _jobStore.DbRetryInterval > timeToSleep ? _jobStore.DbRetryInterval : timeToSleep;
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

        private bool Manage()
        {
            try
            {
                Log.LogDebug("Checking in and scanning for failed instances...");
                var recovered = _jobStore.DoCheckIn().Result;
                _numFails = 0;
                return recovered;
            }
            catch (Exception ex) when (_shutdown && IsInterruption(ex))
            {
                // Shutdown() interrupts this thread on purpose, so the interruption is the expected
                // exit path rather than a check-in failure.
                Log.LogDebug("Check-in interrupted by shutdown");
            }
            catch (Exception ex)
            {
                if (_numFails%_jobStore.RetryableActionErrorLogThreshold == 0)
                {
                    Log.LogError(ex, "Error checking in with the cluster");
                }

                _numFails++;
            }

            return false;
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
