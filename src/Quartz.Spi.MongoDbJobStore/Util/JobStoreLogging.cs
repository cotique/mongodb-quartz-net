using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quartz.Spi.MongoDbJobStore.Util
{
    /// <summary>
    /// Quartz constructs the job store itself, so there is no container to inject a logger
    /// factory through. Assign <see cref="LoggerFactory" /> before starting the scheduler to
    /// get logs out of the store; until then logging is a no-op.
    /// </summary>
    public static class JobStoreLogging
    {
        private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

        public static ILoggerFactory LoggerFactory
        {
            get => _loggerFactory;
            set => _loggerFactory = value ?? NullLoggerFactory.Instance;
        }

        /// <remarks>
        /// Resolved per call rather than cached in a static field. The factory is normally
        /// assigned after this type is first touched, and a cached no-op logger would swallow
        /// everything from that point on. <see cref="ILoggerFactory" /> caches internally.
        /// </remarks>
        internal static ILogger For<T>() => _loggerFactory.CreateLogger(typeof(T).FullName!);

        internal static ILogger For(Type type) => _loggerFactory.CreateLogger(type.FullName!);
    }
}
