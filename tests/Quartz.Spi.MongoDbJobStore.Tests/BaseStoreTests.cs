using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Quartz.Impl;

namespace Quartz.Spi.MongoDbJobStore.Tests
{
    public abstract class BaseStoreTests
    {
        public const string Barrier = "BARRIER";
        public const string DateStamps = "DATE_STAMPS";
        public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(125);

        /// <summary>
        /// Where the tests expect to find MongoDB. CI points this at its own container;
        /// locally it falls back to a plain local server.
        /// </summary>
        protected static string ConnectionString =>
            Environment.GetEnvironmentVariable("QUARTZ_MONGO_CONNECTION_STRING") ??
            "mongodb://localhost/quartz";

        protected async Task<IScheduler> CreateScheduler(string instanceName = "QUARTZ_TEST",
            string instanceId = null, NameValueCollection extraProperties = null)
        {
            var properties = new NameValueCollection
            {
                // Spelled out rather than using the "json" alias, which resolves to the
                // Newtonsoft-based Quartz.Serialization.Json package instead of this one.
                ["quartz.serializer.type"] =
                    "Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson",
                [StdSchedulerFactory.PropertySchedulerInstanceName] = instanceName,
                [StdSchedulerFactory.PropertySchedulerInstanceId] =
                    instanceId ?? $"{Environment.MachineName}-{Guid.NewGuid()}",
                [StdSchedulerFactory.PropertyJobStoreType] = typeof(MongoDbJobStore).AssemblyQualifiedName,
                [$"{StdSchedulerFactory.PropertyJobStorePrefix}.{StdSchedulerFactory.PropertyDataSourceConnectionString}"]
                    = ConnectionString,
                [$"{StdSchedulerFactory.PropertyJobStorePrefix}.collectionPrefix"] = "prefix"
            };

            if (extraProperties != null)
            {
                properties.Add(extraProperties);
            }

            var scheduler = new StdSchedulerFactory(properties);
            return await scheduler.GetScheduler();
        }
    }
}