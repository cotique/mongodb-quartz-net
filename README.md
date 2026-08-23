MongoDB Job Store for Quartz.NET
================================

A fork of [glucaci/mongodb-quartz-net](https://github.com/glucaci/mongodb-quartz-net), kept on
current dependency versions. Published to NuGet as `cotique.Quartz.Spi.MongoDbJobStore`.

## Why this fork exists

Upstream's stable line stopped in January 2021. Both `master` and `v3` there still point at
commit `1a5ac3c` (2021-01-19), and the newest stable `Quartz.Spi.MongoDbJobStore` on NuGet is
3.1.0, published the same day. Development did continue on upstream's `v4` branch, last commit
2025-02-10, but it has only ever shipped `4.0.0-preview.*`; there is no stable 4.x.

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

There is no `NameValueCollection` on this path, but the keys are the same, and `SetProperty`
takes them. Add the `Quartz.Extensions.Hosting` package:

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

Note `$"{StdSchedulerFactory.PropertyObjectSerializer}.type"` rather than the constant alone.
See below for why. `UseSystemTextJsonSerializer()` is not the shortcut it looks like on this path
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
  path, not the path this store is configured through, on either the `NameValueCollection` or
  the `AddQuartz` side. Set the property.
- **`StdSchedulerFactory.PropertyObjectSerializer` is `"quartz.serializer"`, not
  `"quartz.serializer.type"`.** Assigning that constant on its own is not silently ignored. It
  writes a key Quartz never reads, and startup then fails with the error above, byte for byte the
  same as having configured nothing. That identical text is what sends people looking in the wrong
  place. Either use the literal key, or append the suffix:
  `$"{StdSchedulerFactory.PropertyObjectSerializer}.type"`.

Of the two documented aliases, neither is what you want: `json` resolves to
`Quartz.Simpl.JsonObjectSerializer, Quartz.Serialization.Json`, the Newtonsoft-based package,
which this fork does not reference; `binary` resolves to a `BinaryFormatter` serializer that does
not run on .NET 8. The undocumented `stj` alias is the right one: it expands to exactly the type
the examples above pass, so either form works.

## Running more than one instance

Several instances can share one database. Each one takes a distributed lock in the `locks`
collection before touching triggers, so acquisition is serialised across processes, and each one
reports itself alive on an interval. An instance that stops reporting has its work reclaimed by
whichever instance notices first.

Give every instance its own `quartz.scheduler.instanceId`, and prefer one that stays the same
across restarts of the same process:

```cs
properties[StdSchedulerFactory.PropertySchedulerInstanceId] =
    Environment.GetEnvironmentVariable("POD_NAME") ?? Environment.MachineName;
```

`AUTO` does not work here. The store reports `Clustered => false`, so Quartz resolves `AUTO` to the
literal `NON_CLUSTERED` and every instance ends up sharing one identity. One identity across several
processes is exactly what recovery has to be able to tell apart. Setting
`quartz.jobStore.clustered = true` is not the alternative: the property has no setter, so the
factory throws `JobStore type '...' props could not be configured` rather than ignoring it.

The `Guid` in the examples further up is fine for a single instance. With several, a fresh id every
start still works, but a killed instance then leaves behind a row nobody will reuse, and its work
waits out the failure window instead of being cleaned up by the restarted process on startup.

### What recovery does

On every check-in the store stamps its row in `schedulers` and looks at everyone else's. An
instance whose stamp is older than the failure window is reclaimed, under the `TriggerAccess` lock:

- a trigger it had acquired but not started goes back to `Waiting`;
- a trigger it was executing is unblocked, and rescheduled if the job asks for recovery;
- its `firedTriggers` records are deleted, and its `schedulers` row last of all.

The interval is 15 seconds by default, the same as Quartz's own ADO store, and it is settable in
milliseconds:

```cs
properties[$"{StdSchedulerFactory.PropertyJobStorePrefix}.clusterCheckinInterval"] = "15000";
```

The failure window is not the interval. It is the interval, or however long this instance's own
check-in loop has been stalled if that is longer, plus 7.5 seconds. That second clause is the one
that matters under load: if a garbage collection pause or a slow database froze this loop, every
other instance's stamp looks equally stale through no fault of its own, and declaring them all
failed on that basis would hand running work out to be run a second time.

This is what the absence of it looked like. An instance killed mid-execution stopped the schedule
for good: its `firedTriggers` record stayed in `Executing`, a job marked
`[DisallowConcurrentExecution]` kept its trigger `Blocked` against an execution that no longer
existed, and no surviving instance had a path to it. Restarting an instance cleared the block but
left the record behind, and they piled up.

### What it does not promise

**An instance that freezes is indistinguishable from one that died.** A long garbage collection, a
suspended host, a stalled disk, `docker pause`: from the database they all look the same, which is
no check-in. If the pause outlasts the failure window the work is reclaimed, and then the frozen
instance can come back and carry on from where it was.

The store protects the state it owns. It refuses to fire a trigger whose claim has been reclaimed,
and it refuses to write trigger state for an execution that is no longer its own, logging both.
What it cannot do is stop a job already running inside another process. So for a job that requests
recovery, one slot can run twice: once on the instance that was reclaimed, once on the instance that
took over.

That last gap is the job's to close, not the store's. Anything scheduled here should be safe to run
twice, and any record it writes should key off the scheduled fire time rather than a fresh
identifier per attempt. A key generated per attempt never collides, so a retry writes a second row
instead of hitting the unique index.

**Do not run a mixed upgrade.** `2.1.0-rc.1` and everything before it never check in, so their rows
keep whatever stamp they got at startup. A newer instance reads that, finds it minutes or hours old,
and reclaims work the old instance is still doing. Take the old instances down before bringing new
ones up, or expect the overlap to run some slots twice.

### Installing the schedule from more than one instance

Declaring jobs and triggers inside `AddQuartz` does not survive several instances starting at once
against an empty database. Quartz's initialisation asks whether each job exists and then calls
`ScheduleJob(job, trigger)` for the ones that did not; two processes both get "does not exist",
both call it, and the second is answered with:

```
Quartz.ObjectAlreadyExistsException: Unable to store Job: 'DEFAULT.tick', because one already
exists with this identification.
   at Quartz.Spi.MongoDbJobStore.MongoDbJobStore.StoreJobInternal(...)
```

The store is keeping its contract there, since `IJobStore.StoreJobAndTrigger` has no "replace"
parameter and is defined to refuse duplicates. But the unhandled exception takes the host down, so
a cold start of a fresh deployment loses an instance. `OverWriteExistingData` does not help: it is
already true by default, and it only covers the case where the duplicate is visible at the moment
of the check.

Install the schedule after the scheduler is running instead, idempotently:

```cs
await scheduler.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true, ct);

if (await scheduler.CheckExists(trigger.Key, ct))
    await scheduler.RescheduleJob(trigger.Key, trigger, ct);
else
    await scheduler.ScheduleJob(trigger, ct);
```

`AddJob` with `replace: true` is an upsert in this store, so it is safe to race. Wrap the pair in a
short retry that catches `ObjectAlreadyExistsException`: the trigger half still has a window, and
on the retry the other instance's write is visible and the `RescheduleJob` branch takes it.

## NuGet

```
Install-Package cotique.Quartz.Spi.MongoDbJobStore
```

## Credits

Originally written by [@chrisdrobison](https://github.com/chrisdrobison/mongodb-quartz-net) and
handed over to [@glucaci](https://github.com/glucaci/mongodb-quartz-net). MIT licensed; see
`LICENSE.txt`.
