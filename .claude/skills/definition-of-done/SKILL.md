---
name: definition-of-done
description: Pre-commit gate for mongodb-quartz-net. Verifies build, the warning budget, that the integration tests actually ran, and package sanity before any commit, and states what may not be done without asking. Activates on "definition of done", "DoD", "ready to commit", "is this done", or before proposing any commit in this repository.
version: 1.0.0
---

# Definition of Done — mongodb-quartz-net

## Scope

A change here is **not ready to commit** until this passes. This repository publishes a
NuGet package consumed by other people, so a bad commit does not stop at the repo.

Nothing here is enforced by a hook. The discipline-pack hooks are deliberately generic and
know nothing about `dotnet`; this file is the project-specific part.

## Baseline before you change anything

Measure first, on the merge-base, and write the numbers down:

```bash
dotnet build -c Release --no-incremental --nologo 2>&1 | grep -cE "warning [A-Z]+[0-9]+"
dotnet test  -c Release --no-build 2>&1 | tail -3
```

Without a baseline, "those warnings were already there" and "those tests were already
failing" are guesses. Both were used as excuses in this repository and both were wrong at
least once.

## Checklist

| # | Check | Command | Failure mode |
|---|-------|---------|--------------|
| 1 | Builds clean | `dotnet build -c Release --no-incremental --nologo` | Any error. Never commit through a compile error, including in tests. |
| 2 | Warning count has not grown | compare the count against the baseline above | A higher count means this change added warnings. Fix them, do not average them away against inherited ones. |
| 3 | No unaccounted warning in a file this change touches | `dotnet build ... 2>&1 \| grep "warning"` filtered to the files in `git diff --name-only` | For each one, either fix it or say out loud, in the commit or PR, that it is inherited and why it stays. Silence is what let three `CS8613` signature mismatches ship. |
| 4 | Tests pass **and actually ran** | `dotnet test -c Release --no-build` | `Skipped` must be 0. A green run of skipped tests is what let a broken serializer reach a published package. Read the counts, not the colour. |
| 4a | A failing run is identified, not re-rolled | capture the failing test **name** on the first red run, before running again | These are timing-sensitive integration tests and at least one is flaky: a run failed 1/15 and three re-runs were clean, with nothing recorded about which test it was. Grepping only the summary line throws away the one piece of information that matters. A single red run is a finding to name, not noise to average out. |
| 5 | MongoDB was reachable for the run | check the run really executed the integration tests | The tests need a server. Locally: the MongoDB service on `localhost:27017`, or `QUARTZ_MONGO_CONNECTION_STRING`. In CI: the `mongo:8.0` service container. No server means no verification, not a pass. |
| 6 | Package still builds and carries no advisory | `dotnet pack src/Quartz.Spi.MongoDbJobStore/Quartz.Spi.MongoDbJobStore.csproj -c Release --no-build -o artifacts` | Any `NU1901`–`NU1904` is a vulnerable dependency. Treat it as blocking; it is what shipped `System.Text.Json 8.0.1` with two advisories. |
| 7 | Package metadata still correct, when `*.csproj` or `Directory.Build.props` changed | unzip the `.nupkg` and read the `.nuspec` | `PackageId`, `PackageLicenseExpression`, TFMs, dependency list and the SourceLink `repository` commit. Reading the produced nuspec is the only way to know; the csproj is an intention, not a result. |
| 8 | Stored-format changes are called out | judgement | The serializers under `Serializers/` and `Models/Calendar.cs` decide how documents are written. Changing them breaks existing collections. Say so in the commit and the release notes, and say whether the old format can still be read. |

## What may not be done without being asked

These are not checks, they are limits. An instruction to write code is not an instruction to
release it.

- **Do not merge a pull request.** Open it and stop. `gh pr merge` is invisible to the
  branch-protection hook, so nothing will stop you.
- **Do not push a tag.** `v*` triggers publication to nuget.org, and a published version
  cannot be deleted, only unlisted.
- **Do not create a GitHub release**, and do not publish to nuget.org by any other route.
- **Do not commit on `master` or `develop`.** Branch, then open a PR.

Every one of these was done unasked in this repository once already. If a plan mentions a
later step, that is a plan, not permission for the step.

## Notes

- Commit messages here carry no `Co-Authored-By` trailer.
- The library targets `net8.0` only. MongoDB.Driver 3.x ships no `netstandard2.0` assembly,
  so restoring that target means abandoning the current driver.
- `<Nullable>enable</Nullable>` is set but the code was never annotated. Roughly a hundred
  `CS86xx` warnings are inherited. That is the reason for checks 2 and 3: a flat count
  hides a new one, so compare, and account for anything in a file you touched.
