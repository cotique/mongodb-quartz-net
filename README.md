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
there is nowhere to inject a factory; assign `JobStoreLogging.LoggerFactory` (it lives in
`Quartz.Spi.MongoDbJobStore.Util`) before starting the scheduler, or the store stays quiet.

## Basic usage

```cs
var properties = new NameValueCollection();
// Required. See "The serializer setting" below; leaving this out is a startup failure.
// SystemTextJsonObjectSerializer is in Quartz.Simpl, in the Quartz.Serialization.SystemTextJson
// assembly this package already depends on
properties["quartz.serializer.type"] = typeof (SystemTextJsonObjectSerializer).AssemblyQualifiedName;
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

## With Quartz.Extensions.Hosting

There is no `NameValueCollection` on this path, but the keys are the same — `SetProperty` takes
them. Add the `Quartz.Extensions.Hosting` package:

```cs
builder.Services.AddQuartz(q =>
{
    q.SetProperty(StdSchedulerFactory.PropertySchedulerInstanceName, "my-scheduler");
    // Explicit, and distinct per instance. AUTO resolves to the literal NON_CLUSTERED here
    q.SetProperty(StdSchedulerFactory.PropertySchedulerInstanceId,
        $"{Environment.MachineName}-{Guid.NewGuid()}");
    q.SetProperty(StdSchedulerFactory.PropertyJobStoreType,
        typeof(MongoDbJobStore).AssemblyQualifiedName!);
    q.SetProperty($"{StdSchedulerFactory.PropertyJobStorePrefix}.{StdSchedulerFactory.PropertyDataSourceConnectionString}",
        "mongodb://localhost/quartz");
    q.SetProperty($"{StdSchedulerFactory.PropertyObjectSerializer}.type",
        typeof(SystemTextJsonObjectSerializer).AssemblyQualifiedName!);
});

// Without this the scheduler is registered but never started
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var host = builder.Build();

// Assign before the host starts; the store resolves its loggers on each call, so this is in
// time, but anything logged during Initialize is lost if it comes later
JobStoreLogging.LoggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

await host.RunAsync();
```

Note `$"{StdSchedulerFactory.PropertyObjectSerializer}.type"` rather than the constant alone —
see below for why. `UseSystemTextJsonSerializer()` is not the shortcut it looks like on this path
either: its only overload extends `SchedulerBuilder.PersistentStoreOptions`, reached through
`UsePersistentStore`, which is not how this store is configured.

## The serializer setting

`quartz.serializer.type` is not optional. This store reports `SupportsPersistence`, and Quartz
refuses to start any persistent store without an object serializer:

```
You must define object serializer using configuration key 'quartz.serializer.type' when using
other than RAMJobStore. Out of the box supported values are 'json' and 'binary'.
```

Three things that message does not tell you:

- **The dependency is not the configuration.** This package references
  `Quartz.Serialization.SystemTextJson`, so the assembly is already in your output directory and
  the store uses it internally for calendars and job data maps. Quartz's own setting is separate,
  and the error above is what you get until you set it. The message never mentions
  System.Text.Json at all, which makes an already-present package look like an already-configured
  one.
- **`UseSystemTextJsonSerializer()` does not apply here.** Its one overload extends
  `SchedulerBuilder.PersistentStoreOptions`, which belongs to the fluent `UsePersistentStore`
  path — not the path this store is configured through, on either the `NameValueCollection` or
  the `AddQuartz` side. Set the property.
- **`StdSchedulerFactory.PropertyObjectSerializer` is `"quartz.serializer"`, not
  `"quartz.serializer.type"`.** Assigning that constant on its own is not silently ignored — it
  writes a key Quartz never reads, and startup then fails with the error above, byte for byte the
  same as having configured nothing. That identical text is what sends people looking in the wrong
  place. Either use the literal key, or append the suffix:
  `$"{StdSchedulerFactory.PropertyObjectSerializer}.type"`.

Of the two documented aliases, neither is what you want: `json` resolves to
`Quartz.Simpl.JsonObjectSerializer, Quartz.Serialization.Json`, the Newtonsoft-based package,
which this fork does not reference; `binary` resolves to a `BinaryFormatter` serializer that does
not run on .NET 8. The undocumented `stj` alias is the right one — it expands to exactly the type
the examples above pass, so either form works.

## NuGet

```
Install-Package cotique.Quartz.Spi.MongoDbJobStore
```

## Credits

Originally written by [@chrisdrobison](https://github.com/chrisdrobison/mongodb-quartz-net) and
handed over to [@glucaci](https://github.com/glucaci/mongodb-quartz-net). MIT licensed; see
`LICENSE.txt`.
