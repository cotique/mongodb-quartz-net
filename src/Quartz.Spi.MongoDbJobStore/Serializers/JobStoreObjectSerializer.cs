using Quartz.Simpl;

namespace Quartz.Spi.MongoDbJobStore.Serializers
{
    /// <summary>
    /// The serializer used for the payloads this store persists: calendars and job data maps.
    /// </summary>
    /// <remarks>
    /// Quartz's own System.Text.Json serializer is used because it knows how to round-trip
    /// Quartz's own types. A plain JsonSerializer cannot: a calendar comes back out through
    /// <see cref="ICalendar" /> and interfaces are not deserializable, and a JobDataMap loses
    /// the types of its values on the way back in. BinaryFormatter, which this code used
    /// originally, does not run at all on .NET 8.
    /// </remarks>
    internal static class JobStoreObjectSerializer
    {
        internal static IObjectSerializer Instance { get; } = CreateAndInitialize();

        private static IObjectSerializer CreateAndInitialize()
        {
            var serializer = new SystemTextJsonObjectSerializer();
            serializer.Initialize();
            return serializer;
        }
    }
}
