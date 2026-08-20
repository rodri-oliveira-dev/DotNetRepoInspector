# GitHub Action

**Languages:** English | [Português (Brasil)](../pt-BR/github-action.md)

DotNetRepoInspector provides a reusable composite GitHub Action that runs the same .NET Tool and inspection engine used by the CLI. The Action is a delivery adapter only: project discovery, MSBuild evaluation, classification, configuration, diagnostics, JSON serialization, and exit semantics remain owned by the existing Inspector.

> The Action implementation is present and validated in repository CI, but a public `v1` release is not published yet. `uses: rodri-oliveira-dev/DotNetRepoInspector@v1` becomes available after the release workflow publishes the matching `DotNetRepoInspector` package and creates the Action tags.

## Minimal usage

The Action does not check out source code implicitly:

```yaml
steps:
  - name: Checkout
    uses: actions/checkout@v7

  - name: Inspect .NET repository
    id: inspect
    uses: rodri-oliveira-dev/DotNetRepoInspector@v1
    with:
      path: .
```

The Action itself does not require a GitHub token or write permission to inspect an already checked-out repository.

## Inputs

| Input | Required | Default | Description |
| --- | --- | --- | --- |
| `path` | No | `.` | Repository directory to inspect. Relative paths are resolved from `GITHUB_WORKSPACE`. |
| `output` | No | Action-owned temporary file | Destination for the JSON report. Relative paths are resolved from `GITHUB_WORKSPACE`. Parent directories are created when needed. |
| `verbosity` | No | `normal` | Operational logging level: `normal`, `verbose`, or `debug`. |
| `config` | No | empty | Repository-relative configuration file. When omitted, `.dotnetrepoinspector.json` is loaded automatically if present. |
| `no-config` | No | `false` | Set to `true` to ignore the default repository configuration file. Cannot be combined with `config`. |
| `exclude` | No | empty | Newline-separated repository-relative directory or exact project paths to exclude. |
| `classify` | No | empty | Newline-separated `<project-path>=<kind>` explicit classification overrides. |

Supported classification kinds are `web`, `worker`, `console`, `library`, `test`, and `unknown`.

`exclude` values are additive to exclusions from the configuration file. A direct `classify` input wins over a file override for the same project. The Action passes these values to the same CLI/Engine configuration contract; it does not implement independent classification logic. See [`configuration.md`](configuration.md).

The Action intentionally does not expose an `inspector-version` input. Each released Action revision pins one exact .NET Tool version so a specific Action ref remains reproducible.

## Configure exclusions and overrides

```yaml
- name: Inspect .NET repository
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
    exclude: |
      generated
      samples/Legacy.csproj
    classify: |
      src/App/App.csproj=web
```

A classification override changes only the effective classification interpretation. MSBuild facts remain untouched. Schema `1.3` exposes `classification.source` and `classification.automaticKind` when an override is active so downstream automation can distinguish the configured result from the automatic result.

## Outputs

| Output | Description |
| --- | --- |
| `report-path` | Absolute path to the generated inspection JSON when a report exists. |
| `schema-version` | `schemaVersion` read from the generated report when available. |
| `inspector-version` | Exact `DotNetRepoInspector` .NET Tool version pinned by the Action revision. |
| `exit-code` | Exit code returned by the CLI. |

The complete JSON report is intentionally kept in a file instead of being copied into `$GITHUB_OUTPUT`.

## Save and consume the report

```yaml
- name: Use inspection metadata
  shell: pwsh
  env:
    REPORT_PATH: ${{ steps.inspect.outputs.report-path }}
    SCHEMA_VERSION: ${{ steps.inspect.outputs.schema-version }}
  run: |
    Write-Host "Schema: $env:SCHEMA_VERSION"
    $report = Get-Content -LiteralPath $env:REPORT_PATH -Raw | ConvertFrom-Json
    Write-Host "Projects: $(@($report.projects).Count)"
```

## Exit behavior

The Action preserves the CLI exit codes:

| Code | Meaning |
| ---: | --- |
| `0` | Inspection completed without error diagnostics. |
| `1` | A report was produced but contains one or more `error` diagnostics, including invalid repository configuration (`DRI1013`). |
| `2` | Invalid CLI/Action arguments reached the CLI boundary. |
| `3` | Fatal inspection failure prevented a normal report. |
| `4` | The report could not be written. |
| `130` | Inspection was cancelled. |

When the CLI returns a non-zero code, the Action publishes outputs that are still available and then fails with the same code. A workflow that intentionally wants to inspect those outputs after a failure can use `continue-on-error` and an `if: always()` follow-up step.

## Runtime and repository SDKs

The composite Action bootstraps the .NET 10 SDK required to execute the Inspector by using `actions/setup-dotnet` pinned to a full commit SHA in `action.yml`.

That bootstrap is separate from SDKs required by the repository being inspected. If a repository's `global.json` requires an SDK that is not already available on the runner, the workflow must install that SDK before invoking the Inspector.

## Tool bootstrap and package-source isolation

The Action installs `DotNetRepoInspector` into an invocation-specific directory under `RUNNER_TEMP` by using `dotnet tool install --tool-path`.

It does not install the tool globally, modify the inspected repository's tool manifest, trust repository/machine `NuGet.config` sources for the Inspector package, or select a floating/caller-provided Inspector version. Instead, it creates a temporary NuGet configuration with inherited package sources cleared and resolves the exact pinned package version from NuGet.org.

Repository CI has a narrowly scoped self-test hook that can replace only the package source with the locally packed `1.0.0` package. The hook is rejected outside `rodri-oliveira-dev/DotNetRepoInspector` and does not change the pinned version or the public Action inputs.

## Permissions and trust boundary

Inspection of an already checked-out repository needs no GitHub API access, token, secret, or write permission. Consumers remain responsible for permissions used by their own checkout and subsequent workflow steps.

MSBuild evaluation is not a sandbox. The Inspector evaluates repository-controlled MSBuild configuration according to [ADR 0001](decisions/0001-msbuild-evaluation-strategy.md), so untrusted repositories must not be inspected in a privileged, secrets-bearing workflow without an explicit trust review.

## Versioning

The Action follows [ADR 0002](decisions/0002-github-action-distribution-strategy.md): immutable release tags pin the same exact Inspector package version, moving major/minor aliases may move only within compatibility boundaries, and Action `v1` stays within inspection schema major `1`.

## CI validation

Repository CI exercises the composite Action itself with `uses: ./` on Ubuntu, Windows, and macOS. The smoke test packs the exact Action version locally, installs it through the same isolated bootstrap path, runs a real fixture inspection, validates outputs, and exercises `exclude` plus `classify` inputs. An additional Ubuntu scenario verifies propagation of a non-zero Inspector result and its partial report.
