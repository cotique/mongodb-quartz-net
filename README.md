MongoDB Job Store for Quartz.NET
================================

A fork of [glucaci/mongodb-quartz-net](https://github.com/glucaci/mongodb-quartz-net), kept on
current dependency versions. Published to NuGet as `cotique.Quartz.Spi.MongoDbJobStore`.

## Why this fork exists

Upstream's stable line stopped in January 2021. Both `master` and `v3` there still point at
commit `1a5ac3c` (2021-01-19), and the newest stable `Quartz.Spi.MongoDbJobStore` on NuGet is
3.1.0, published the same day. Development did continue on upstream's `v4` branch — last commit
2025-02-10 — but it has only ever shipped `4.0.0-preview.*`; there is no stable 4.x.

This fork stays on the v3 code and moves the dependencies forward.

| | upstream stable (3.1.0) | this fork |
| --- | --- | --- |
| MongoDB.Driver | 2.4.2 | 3.11.0 |
| Quartz | 3.0.4 | 3.19.1 |
| Target framework | net452; net462; netstandard2.0 | net8.0 |

MongoDB.Driver 3.x ships no `netstandard2.0` assembly, so the move to it means .NET
Framework and .NET Standard consumers stay on the 1.x line of this package.

Logging goes through `Microsoft.Extensions.Logging`. Quartz builds the job store itself, so
there is nowhere to inject a factory; assign `JobStoreLogging.LoggerFactory` before starting
the scheduler, or the store stays quiet.

## Basic usage

```cs
var properties = new NameValueCollection();
properties[StdSchedulerFactory.PropertySchedulerInstanceName] = instanceName;
properties[StdSchedulerFactory.PropertySchedulerInstanceId] = $"{Environment.MachineName}-{Guid.NewGuid()}";
properties[StdSchedulerFactory.PropertyJobStoreType] = typeof (MongoDbJobStore).AssemblyQualifiedName;
// The database named in the connection string is the one that gets used
properties[$"{StdSchedulerFactory.PropertyJobStorePrefix}.{StdSchedulerFactory.PropertyDataSourceConnectionString}"] = "mongodb://localhost/quartz";
// The prefix is optional
properties[$"{StdSchedulerFactory.PropertyJobStorePrefix}.collectionPrefix"] = "prefix";

var scheduler = new StdSchedulerFactory(properties);
return scheduler.GetScheduler();
```

## NuGet

```
Install-Package cotique.Quartz.Spi.MongoDbJobStore
```

## Credits

Originally written by [@chrisdrobison](https://github.com/chrisdrobison/mongodb-quartz-net) and
handed over to [@glucaci](https://github.com/glucaci/mongodb-quartz-net). MIT licensed; see
`LICENSE.txt`.
