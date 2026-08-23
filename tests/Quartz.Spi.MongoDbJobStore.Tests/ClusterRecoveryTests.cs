using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Quartz.Impl;
using Quartz.Spi.MongoDbJobStore.Tests.Jobs;
using Xunit;

namespace Quartz.Spi.MongoDbJobStore.Tests
{
    /// <summary>
    ///     What happens to another instance's work when that instance stops reporting itself alive.
    /// </summary>
    /// <remarks>
    ///     None of this is visible with one instance running, which is why it is worth a file of its
    ///     own. The dead instance is written straight into the database rather than started and
    ///     killed: a process killed for real is what the container stand is for, and inside one test
    ///     process there is no way to stop a store's check-in loop without also running its shutdown,
    ///     which is precisely the path that does not happen when a pod is killed.
    /// </remarks>
    public class ClusterRecoveryTests : BaseStoreTests, IAsyncLifetime
    {
        private const string SurvivorId = "survivor";
        private const string DeadId = "dead-node";
        private const string InstanceName = "QUARTZ_CLUSTER_TEST";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private readonly IMongoDatabase _database;
        private IScheduler _scheduler;

        public ClusterRecoveryTests()
        {
            var url = new MongoUrl(ConnectionString);
            _database = new MongoClient(ConnectionString).GetDatabase(url.DatabaseName);
        }

        private IMongoCollection<BsonDocument> Schedulers => _database.GetCollection<BsonDocument>("prefix.schedulers");

        private IMongoCollection<BsonDocument> FiredTriggers =>
            _database.GetCollection<BsonDocument>("prefix.firedTriggers");

        private IMongoCollection<BsonDocument> Triggers => _database.GetCollection<BsonDocument>("prefix.triggers");

        public async Task InitializeAsync()
        {
            // A second a check-in, so a test does not have to sit through the fifteen a deployment
            // would use.
            var properties = new NameValueCollection
            {
                [$"{StdSchedulerFactory.PropertyJobStorePrefix}.clusterCheckinInterval"] = "1000"
            };

            _scheduler = await CreateScheduler(InstanceName, SurvivorId, properties);
            await _scheduler.Clear();
            await FiredTriggers.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            await Schedulers.DeleteManyAsync(Builders<BsonDocument>.Filter.Ne("_id._id", SurvivorId));
            await _scheduler.Start();
        }

        public async Task DisposeAsync()
        {
            await _scheduler.Shutdown();
            await FiredTriggers.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            await Schedulers.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        }

        [Fact]
        public async Task ReleasesATriggerBlockedByADeadInstance()
        {
            var (job, trigger) = await ScheduleDistantJob("blocked-by-dead");

            // The shape a kernel kill leaves behind: the execution is still recorded as running,
            // and because the job disallows concurrent execution the trigger stays blocked against
            // an execution that no longer exists.
            await WriteInstance(DeadId, DateTime.UtcNow.AddMinutes(-5));
            await WriteFiredTrigger(DeadId, job, trigger, "Executing", concurrentExecutionDisallowed: true);
            await SetTriggerState(trigger, "Blocked");

            await Eventually(async () => await ReadTriggerState(trigger) == "Waiting",
                "the trigger to be released");

            (await CountFiredTriggers(DeadId)).Should().Be(0, "the orphaned execution record is removed");
            (await CountInstances(DeadId)).Should().Be(0, "the dead instance's row is removed with it");
        }

        [Fact]
        public async Task ReturnsAnAcquiredButUnstartedTriggerToWaiting()
        {
            var (job, trigger) = await ScheduleDistantJob("acquired-by-dead");

            await WriteInstance(DeadId, DateTime.UtcNow.AddMinutes(-5));
            await WriteFiredTrigger(DeadId, job, trigger, "Acquired", concurrentExecutionDisallowed: false);
            await SetTriggerState(trigger, "Acquired");

            await Eventually(async () => await ReadTriggerState(trigger) == "Waiting",
                "the acquired trigger to be handed back");

            (await CountFiredTriggers(DeadId)).Should().Be(0);
        }

        [Fact]
        public async Task LeavesAnInstanceThatIsStillCheckingInAlone()
        {
            var (job, trigger) = await ScheduleDistantJob("held-by-live");

            // Same records, one difference: this instance keeps reporting itself. Reclaiming it
            // would hand a running execution to a second instance and run the job twice.
            await WriteInstance("live-node", DateTime.UtcNow);
            await WriteFiredTrigger("live-node", job, trigger, "Executing", concurrentExecutionDisallowed: true);
            await SetTriggerState(trigger, "Blocked");

            // Reported on the same cadence a running instance would, and for long enough to pass
            // the point where a silent one is declared dead. A single stamp written once would not
            // prove anything: an instance that never reports again is dead by definition, and this
            // test would then be asserting that recovery does not work.
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                await TouchInstance("live-node");
            }

            (await CountFiredTriggers("live-node")).Should().Be(1, "a live instance keeps its execution");
            (await CountInstances("live-node")).Should().Be(1, "and keeps its row");
            (await ReadTriggerState(trigger)).Should().Be("Blocked", "and keeps its block");
        }

        [Fact]
        public async Task KeepsItsOwnCheckInCurrent()
        {
            var first = await ReadCheckIn(SurvivorId);
            first.Should().NotBeNull("the running instance registers itself");

            await Eventually(async () => await ReadCheckIn(SurvivorId) > first,
                "the running instance to report itself again");
        }

        private async Task<(JobKey job, TriggerKey trigger)> ScheduleDistantJob(string name)
        {
            // An hour out, so the surviving scheduler never acquires it and the only thing that
            // moves the trigger is recovery.
            var job = JobBuilder.Create<SimpleJob>().WithIdentity(name).Build();
            var trigger = TriggerBuilder.Create()
                .WithIdentity(name)
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();

            await _scheduler.ScheduleJob(job, trigger);
            return (job.Key, trigger.Key);
        }

        private Task WriteInstance(string instanceId, DateTime lastCheckIn)
        {
            return Schedulers.InsertOneAsync(new BsonDocument
            {
                { "_id", new BsonDocument { { "InstanceName", InstanceName }, { "_id", instanceId } } },
                { "State", "Started" },
                { "LastCheckIn", lastCheckIn }
            });
        }

        private Task TouchInstance(string instanceId)
        {
            return Schedulers.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id._id", instanceId),
                Builders<BsonDocument>.Update.Set("LastCheckIn", DateTime.UtcNow));
        }

        private Task WriteFiredTrigger(string instanceId, JobKey job, TriggerKey trigger, string state,
            bool concurrentExecutionDisallowed)
        {
            return FiredTriggers.InsertOneAsync(new BsonDocument
            {
                {
                    "_id",
                    new BsonDocument
                    {
                        { "InstanceName", InstanceName },
                        { "FiredInstanceId", $"{instanceId}-{Guid.NewGuid()}" }
                    }
                },
                { "TriggerKey", new BsonDocument { { "Name", trigger.Name }, { "Group", trigger.Group } } },
                { "JobKey", new BsonDocument { { "Name", job.Name }, { "Group", job.Group } } },
                { "InstanceId", instanceId },
                { "Fired", DateTime.UtcNow.AddMinutes(-5) },
                { "Scheduled", DateTime.UtcNow.AddMinutes(-5) },
                { "Priority", 5 },
                { "State", state },
                { "ConcurrentExecutionDisallowed", concurrentExecutionDisallowed },
                { "RequestsRecovery", false }
            });
        }

        private Task SetTriggerState(TriggerKey trigger, string state)
        {
            return Triggers.UpdateOneAsync(TriggerFilter(trigger), Builders<BsonDocument>.Update.Set("State", state));
        }

        private async Task<string> ReadTriggerState(TriggerKey trigger)
        {
            var document = await Triggers.Find(TriggerFilter(trigger)).FirstOrDefaultAsync();
            return document?["State"].AsString;
        }

        private static FilterDefinition<BsonDocument> TriggerFilter(TriggerKey trigger)
        {
            return Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id.Name", trigger.Name),
                Builders<BsonDocument>.Filter.Eq("_id.Group", trigger.Group));
        }

        private async Task<DateTime?> ReadCheckIn(string instanceId)
        {
            var document = await Schedulers.Find(Builders<BsonDocument>.Filter.Eq("_id._id", instanceId))
                .FirstOrDefaultAsync();
            return document?["LastCheckIn"].ToUniversalTime();
        }

        private Task<long> CountFiredTriggers(string instanceId)
        {
            return FiredTriggers.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("InstanceId", instanceId));
        }

        private Task<long> CountInstances(string instanceId)
        {
            return Schedulers.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("_id._id", instanceId));
        }

        /// <summary>
        ///     Polls until the condition holds. Recovery runs on a background thread on its own clock,
        ///     so a fixed delay would either be flaky or slower than it needs to be.
        /// </summary>
        private static async Task Eventually(Func<Task<bool>> condition, string what)
        {
            var deadline = DateTime.UtcNow.Add(Patience);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            throw new TimeoutException($"Waited {Patience.TotalSeconds:0}s for {what} and it did not happen.");
        }
    }
}
