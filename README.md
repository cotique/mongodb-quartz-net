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
| MongoDB.Driver | 2.4.2 | 2.22.0 |
| Quartz | 3.0.4 | 3.3.3 |
| Target framework | net452; net462; netstandard2.0 | netstandard2.0 |

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
