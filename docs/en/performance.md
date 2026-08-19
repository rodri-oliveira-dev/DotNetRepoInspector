# Performance and scalability

**Languages:** English | [Português (Brasil)](../pt-BR/performance.md)

DotNetRepoInspector uses a reproducible synthetic repository to measure inspection cost before introducing performance optimizations. The goal is to detect material regressions while keeping correctness, SDK-resolution semantics, and deterministic output ahead of micro-optimizations.

## Reference scenario

The performance harness lives in `benchmarks/DotNetRepoInspector.Performance` and creates the repository in a temporary directory before the timed region begins.

The versioned reference scenario is `synthetic-100-projects`:

- 100 SDK-style projects under `src/`;
- `Directory.Build.props` supplies `net8.0`, nullable, and implicit-usings settings;
- a repository `global.json` requests .NET SDK `10.0.100` with `latestFeature` roll-forward;
- every project after the first references the previous project, producing a 100-project dependency chain;
- projects contain no application source code, keeping the measurement focused on repository inspection and MSBuild evaluation rather than compilation.

The harness supports other project counts through `--project-count`, but the CI regression baseline is intentionally tied to 100 projects so comparisons use the same workload.

## What is measured

One cold inspection records:

- project discovery time;
- aggregate `IMsBuildProjectFactsEvaluator` time;
- JSON serialization time;
- remaining inspection overhead, including repository/SDK metadata and normalization work;
- inspection and end-to-end elapsed time;
- managed bytes allocated by the Inspector process during the timed region;
- peak working set of the Inspector process;
- discovered-project and project-evaluation counts;
- serialized JSON size.

Synthetic repository creation, restore, and build of the benchmark harness are outside the measured region.

Memory numbers are approximate. Managed allocation and peak-working-set measurements describe the main Inspector process. They do **not** aggregate the memory of child `dotnet`/MSBuild processes, so they are suitable for regression detection in the Inspector itself, not as a statement of total machine memory required by an inspection.

## Baseline observed on 2026-08-19

The baseline was established on GitHub-hosted Ubuntu 24.04 runners with .NET SDK `10.0.400`. Two equivalent runs were taken before setting a threshold.

| Metric | Run 1 | Run 2 |
| --- | ---: | ---: |
| Projects discovered / evaluated | 100 / 100 | 100 / 100 |
| Discovery | 9.87 ms | 8.39 ms |
| MSBuild evaluation | 49,442.25 ms | 56,995.26 ms |
| Serialization | 35.06 ms | 41.33 ms |
| Other inspection overhead | 210.28 ms | 204.34 ms |
| End to end | 49,697.46 ms | 57,249.32 ms |
| Managed allocations | 18.16 MiB | 18.17 MiB |
| Peak working set | 79.36 MiB | 79.14 MiB |

The slower run is recorded in `.github/performance-baseline.json` as the conservative observed reference. End-to-end time varied by about 15% between the two hosted runners, while managed allocations and working set were effectively stable.

## Hotspot evidence

MSBuild project-fact evaluation accounts for more than 99.5% of the measured inspection time in both runs. Discovery and serialization are each measured in tens of milliseconds and are not current optimization targets.

The harness also verifies that exactly 100 project-fact evaluations occur for 100 discovered projects. There is therefore no duplicate `IMsBuildProjectFactsEvaluator` invocation in the inspection engine for this scenario.

Each project-fact evaluation still follows ADR 0001: an SDK-resolution preflight (`dotnet --version`) followed by the evaluated `dotnet msbuild` query. Child-process startup and MSBuild evaluation are consequently the dominant scalability cost. That behavior is deliberate because SDK selection depends on the project working directory and its applicable `global.json`.

## Cache and parallelism decisions

No cache or parallel project evaluation is introduced by the performance work.

A repository-wide SDK-resolution cache keyed only by repository root would be incorrect when nested `global.json` files select different SDKs. A safe cache would need to be scoped to one inspection and keyed by the effective SDK-resolution context, with fixtures proving nested configuration behavior before measuring the gain.

Likewise, unbounded parallel evaluation could create many simultaneous `dotnet`/MSBuild processes, increasing CPU and memory pressure and potentially making CI less predictable. There is currently no comparative benchmark proving a safe net benefit, so the engine remains sequential. A future optimization should use bounded concurrency only after correctness tests and before/after measurements justify it.

## Regression guard

`Validate performance` runs on Ubuntu when performance-relevant source, the harness, its workflow, or the baseline changes. It can also be started manually with `workflow_dispatch`.

The versioned limits are deliberately wider than the observed hosted-runner variance:

| Guard | Limit |
| --- | ---: |
| Aggregate MSBuild evaluation | 85,000 ms |
| End to end | 90,000 ms |
| Managed allocations | 32 MiB |
| Inspector peak working set | 128 MiB |

These are **regression limits, not performance promises**. They are intended to catch large accidental slowdowns, duplicate evaluations, or substantial Inspector-memory growth without making CI flaky because of normal hosted-runner noise. A baseline increase must be reviewed with new measurements; the workflow never updates it automatically.

The performance job has a 10-minute GitHub Actions timeout, while the benchmark itself cancels after 300 seconds. The normal CLI already propagates cancellation (including Ctrl+C) to inspection and child processes. No universal CLI wall-clock timeout is imposed because valid repository sizes and MSBuild workloads vary widely; callers can apply an environment-specific CI timeout.

## Running locally

Build once, then run the fixed baseline scenario:

```bash
dotnet restore ./benchmarks/DotNetRepoInspector.Performance/DotNetRepoInspector.Performance.csproj
dotnet build ./benchmarks/DotNetRepoInspector.Performance/DotNetRepoInspector.Performance.csproj --configuration Release --no-restore

dotnet run \
  --project ./benchmarks/DotNetRepoInspector.Performance/DotNetRepoInspector.Performance.csproj \
  --configuration Release \
  --no-build \
  -- \
  --project-count 100 \
  --timeout-seconds 300 \
  --output ./artifacts/performance/metrics.json \
  --summary ./artifacts/performance/summary.md \
  --baseline ./.github/performance-baseline.json
```

Run without `--baseline` when collecting exploratory measurements for another project count. Such measurements are useful for investigation but are not directly comparable to the versioned 100-project CI baseline.
