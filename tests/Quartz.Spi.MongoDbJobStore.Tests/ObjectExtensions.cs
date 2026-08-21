using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Quartz.Util
{
    /// <summary>
    /// Generic extension methods for objects.
    /// </summary>
    public static class ObjectExtensions
    {
        /// <summary>
        /// Creates a deep copy of object by round-tripping it through BSON, the
        /// same representation the job store persists documents in.
        /// </summary>
        /// <param name="obj"></param>
        public static T DeepClone<T>(this T obj) where T : class
        {
            if (obj == null)
            {
                return null;
            }

            return BsonSerializer.Deserialize<T>(obj.ToBson());
        }
    }
}
