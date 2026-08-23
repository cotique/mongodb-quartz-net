using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Quartz.Spi.MongoDbJobStore.Models;
using Quartz.Spi.MongoDbJobStore.Models.Id;

namespace Quartz.Spi.MongoDbJobStore.Repositories
{
    [CollectionName("schedulers")]
    internal class SchedulerRepository : BaseRepository<Scheduler>
    {
        public SchedulerRepository(IMongoDatabase database, string instanceName, string collectionPrefix = null)
            : base(database, instanceName, collectionPrefix)
        {
        }

        public async Task<List<Scheduler>> GetAll()
        {
            return await Collection.Find(sch => sch.Id.InstanceName == InstanceName).ToListAsync().ConfigureAwait(false);
        }

        public async Task AddScheduler(Scheduler scheduler)
        {
            await Collection.ReplaceOneAsync(sch => sch.Id == scheduler.Id,
                scheduler, new ReplaceOptions
                {
                    IsUpsert = true
                }).ConfigureAwait(false);
        }

        public async Task DeleteScheduler(string id)
        {
            await Collection.DeleteOneAsync(sch => sch.Id == new SchedulerId(id, InstanceName)).ConfigureAwait(false);
        }

        /// <summary>
        ///     Reports this instance alive. Returns false when the row is gone, which means another
        ///     instance decided this one was dead and recovered its work.
        /// </summary>
        public async Task<bool> UpdateLastCheckIn(string id, DateTime lastCheckIn)
        {
            var result = await Collection.UpdateOneAsync(sch => sch.Id == new SchedulerId(id, InstanceName),
                UpdateBuilder.Set(sch => sch.LastCheckIn, lastCheckIn)).ConfigureAwait(false);
            return result.MatchedCount > 0;
        }

        public async Task UpdateState(string id, SchedulerState state)
        {
            await Collection.UpdateOneAsync(sch => sch.Id == new SchedulerId(id, InstanceName),
                UpdateBuilder.Set(sch => sch.State, state)).ConfigureAwait(false);
        }
    }
}