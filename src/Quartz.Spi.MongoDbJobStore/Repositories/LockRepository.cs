using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz.Spi.MongoDbJobStore.Models;
using Quartz.Spi.MongoDbJobStore.Models.Id;
using Quartz.Spi.MongoDbJobStore.Util;

namespace Quartz.Spi.MongoDbJobStore.Repositories
{
    [CollectionName("locks")]
    internal class LockRepository : BaseRepository<Lock>
    {
        private static ILogger Log => JobStoreLogging.For<LockRepository>();

        public LockRepository(IMongoDatabase database, string instanceName, string collectionPrefix = null)
            : base(database, instanceName, collectionPrefix)
        {
        }

        public async Task<bool> TryAcquireLock(LockType lockType, string instanceId)
        {
            var lockId = new LockId(lockType, InstanceName);
            Log.LogTrace($"Trying to acquire lock {lockId} on {instanceId}");
            try
            {
                await Collection.InsertOneAsync(new Lock
                {
                    Id = lockId,
                    InstanceId = instanceId,
                    AquiredAt = DateTime.Now
                }).ConfigureAwait(false);
                Log.LogTrace($"Acquired lock {lockId} on {instanceId}");
                return true;
            }
            catch (MongoWriteException)
            {
                Log.LogTrace($"Failed to acquire lock {lockId} on {instanceId}");
                return false;
            }
        }

        public async Task<bool> ReleaseLock(LockType lockType, string instanceId)
        {
            var lockId = new LockId(lockType, InstanceName);
            Log.LogTrace($"Releasing lock {lockId} on {instanceId}");
            var result =
                await Collection.DeleteOneAsync(
                    FilterBuilder.Where(@lock => @lock.Id == lockId && @lock.InstanceId == instanceId)).ConfigureAwait(false);
            if (result.DeletedCount > 0)
            {
                Log.LogTrace($"Released lock {lockId} on {instanceId}");
                return true;
            }
            else
            {
                Log.LogWarning($"Failed to release lock {lockId} on {instanceId}. You do not own the lock.");
                return false;
            }
        }

        public override async Task EnsureIndex()
        {
            await Collection.Indexes.CreateOneAsync(
                new CreateIndexModel<Lock>(IndexBuilder.Ascending(@lock => @lock.AquiredAt),
                    new CreateIndexOptions {ExpireAfter = TimeSpan.FromSeconds(30)})).ConfigureAwait(false);
        }
    }
}