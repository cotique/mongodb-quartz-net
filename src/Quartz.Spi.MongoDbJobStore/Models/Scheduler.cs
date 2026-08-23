using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Quartz.Spi.MongoDbJobStore.Models.Id;

namespace Quartz.Spi.MongoDbJobStore.Models
{
    internal enum SchedulerState
    {
        Started,
        Running,
        Paused,
        Resumed
    }

    internal class Scheduler
    {
        [BsonId]
        public SchedulerId Id { get; set; }

        [BsonRepresentation(BsonType.String)]
        public SchedulerState State { get; set; }

        /// <summary>
        ///     When this instance last reported itself alive. Stated as UTC rather than left to
        ///     the driver's default, because the cluster scan compares it against instances that
        ///     may be running in other time zones.
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime? LastCheckIn { get; set; }
    }
}