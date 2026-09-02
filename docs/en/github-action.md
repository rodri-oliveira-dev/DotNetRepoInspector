# GitHub Action

**Languages:** English | [Português (Brasil)](../pt-BR/github-action.md)

DotNetRepoInspector provides a reusable composite GitHub Action that runs the same .NET Tool and inspection engine used by the CLI. The Action is a delivery adapter only: project discovery, MSBuild evaluation, classification, configuration, diagnostics, JSON serialization, optional persistence, and exit semantics remain owned by the existing Inspector components.

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

The Action itself does not require a GitHub token or write permission to inspect an already checked-out repository. Without `sink-url`, no persistence endpoint is contacted.

## Inputs

| Input | Required | Default | Description |
| --- | --- | --- | --- |
| `path` | No | `.` | Repository directory to inspect. Relative paths are resolved from `GITHUB_WORKSPACE`. |
| `output` | No | Action-owned temporary file | Destination for the JSON report. Relative paths are resolved from `GITHUB_WORKSPACE`. Parent directories are created when needed. |
| `verbosity` | No | `normal` | Operational logging level: `normal`, `verbose`, or `debug`. |
| `config` | No | empty | Repository-relative configuration file. When omitted, `.dotnetrepoinspector.json` is loaded automatically if present. |
| `no-config` | No | `false` | Set to `true` to ignore the default repository configuration file. Cannot be combined with `config`. |
| `exclude` | No | empty | Newline-separated repository-relative directory or exact project paths to exclude. |
| `exclude-repositories` | No | empty | Newline-separated full `owner/repository` identifiers to skip before inspection in fleet inventory workflows. |
| `classify` | No | empty | Newline-separated `<project-path>=<kind>` explicit classification overrides. |
| `sink-url` | No | empty | Absolute HTTP/HTTPS endpoint. A non-empty value enables the built-in HTTP snapshot sink. |
| `sink-token` | No | empty | Optional Bearer token for the HTTP sink. Supply it from a GitHub Actions secret. |
| `sink-timeout-seconds` | No | `15` | Overall persistence timeout when `sink-url` is configured. Supported range is `1..300`. |
| `sink-failure-mode` | No | `non-fatal` | Persistence failure policy: `non-fatal` or `fatal`. |
| `sink-max-attempts` | No | `3` | Maximum HTTP attempts for transient failures. Supported range is `1..5`. |

Supported classification kinds are `web`, `worker`, `console`, `library`, `test`, and `unknown`.

`exclude` values are additive to exclusions from the configuration file. A direct `classify` input wins over a file override for the same project. The Action passes these values to the same CLI/Engine configuration contract; it does not implement independent classification logic. See [`configuration.md`](configuration.md).

`exclude-repositories` is intentionally different from project-path `exclude`. It belongs to aggregated fleet inventory workflows that decide which repositories are part of the population before invoking inspection. Each value must be the complete GitHub repository identifier, such as `rodri-oliveira-dev/DotNetRepoInspector`; partial names, substrings, and path fragments are rejected. When the current `github.repository` matches, the Action exits successfully with `repository-excluded=true`, no report path, no schema version, and no Inspector invocation.

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

## Fleet inventory exclusions

Direct/manual inspection and aggregated fleet inventory have different responsibilities. Running `dotnet repo-inspect .` or using the Action without `exclude-repositories` still inspects the checked-out repository, including DotNetRepoInspector itself when requested directly.

Central inventory workflows should exclude repositories before calculating planned/processed counts and before invoking the Inspector. For the Inspector repository itself, configure the full repository identifier:

```yaml
- name: Inspect .NET repository
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    exclude-repositories: |
      rodri-oliveira-dev/DotNetRepoInspector
```

DotNetRepoInspector contains internal test projects, synthetic fixtures, and deliberately invalid repositories used to validate diagnostic behavior. Those assets are useful product tests, but they are not applications or libraries in the governed fleet, so including them would distort consolidated project totals and warning/error metrics.

## Optional HTTP snapshot persistence

Set `sink-url` to enable the built-in HTTP/webhook sink. The Action invokes the same CLI persistence path and therefore uses the same `InspectionSnapshot`, idempotency key, retry classification, timeout, cancellation, and failure-mode semantics.

```yaml
- name: Inspect and persist architecture evidence
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
    sink-url: https://evidence.example/api/snapshots
    sink-token: ${{ secrets.INSPECTOR_EVIDENCE_TOKEN }}
    sink-failure-mode: fatal
```

`sink-token` is mapped directly to `DOTNET_REPO_INSPECTOR_HTTP_TOKEN` for the tool process. It is not passed as a command-line argument. Do not embed credentials in `sink-url` or commit them into repository configuration.

When the sink is enabled, the Action also supplies generic execution provenance to the snapshot factory:

- execution ID: `github.run_id:github.run_attempt`;
- provider: `github-actions`;
- ref: `github.ref`.

These values are generic snapshot metadata; the persistence contract does not depend on GitHub-specific model types.

The HTTP sink retries only transport/timeouts and HTTP `408`, `429`, `500`, `502`, `503`, and `504`, with bounded attempts/backoff. Authentication failures and other non-transient client failures are not retried. See [`persistence.md`](persistence.md) for the complete delivery contract.

## Outputs

| Output | Description |
| --- | --- |
| `report-path` | Absolute path to the generated inspection JSON when a report exists. |
| `schema-version` | `schemaVersion` read from the generated report when available. |
| `inspector-version` | Exact `DotNetRepoInspector` .NET Tool version pinned by the Action revision. |
| `exit-code` | Exit code returned by the CLI. |
| `repository-excluded` | `true` when the current repository matched `exclude-repositories` and inspection was skipped; otherwise `false`. |

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
| `5` | Optional snapshot persistence failed while `sink-failure-mode` is `fatal`. |
| `130` | Inspection or persistence was cancelled. |

When the CLI returns a non-zero code, the Action publishes outputs that are still available and then fails with the same code. A workflow that intentionally wants to inspect those outputs after a failure can use `continue-on-error` and an `if: always()` follow-up step.

A persistence exit code `5` does not mean the report was lost: inspection output is produced before the persistence attempt. With the default `non-fatal` mode, persistence failure is logged but does not replace the normal inspection exit code.

## Runtime and repository SDKs

The composite Action bootstraps the .NET 10 SDK required to execute the Inspector by using `actions/setup-dotnet` pinned to a full commit SHA in `action.yml`.

That bootstrap is separate from SDKs required by the repository being inspected. If a repository's `global.json` requires an SDK that is not already available on the runner, the workflow must install that SDK before invoking the Inspector.

## Tool bootstrap and package-source isolation

The Action installs `DotNetRepoInspector` into an invocation-specific directory under `RUNNER_TEMP` by using `dotnet tool install --tool-path`.

It does not install the tool globally, modify the inspected repository's tool manifest, trust repository/machine `NuGet.config` sources for the Inspector package, or select a floating/caller-provided Inspector version. Instead, it creates a temporary NuGet configuration with inherited package sources cleared and resolves the exact pinned package version from NuGet.org.

Repository CI has a narrowly scoped self-test hook that can replace only the package source with the locally packed `1.0.0` package. The hook is rejected outside `rodri-oliveira-dev/DotNetRepoInspector` and does not change the pinned version or the public Action inputs.

## Permissions, secrets, and trust boundary

Inspection of an already checked-out repository needs no GitHub API access, token, secret, or write permission. Consumers remain responsible for permissions used by their own checkout and subsequent workflow steps.

HTTP persistence is a separate, explicit network action. If `sink-token` is used, reference a GitHub Actions secret and scope that credential only to the external evidence endpoint. The Inspector does not copy it into the inspection JSON, snapshot payload, normal logs, or CLI arguments.

MSBuild evaluation is not a sandbox. The Inspector evaluates repository-controlled MSBuild configuration according to [ADR 0001](decisions/0001-msbuild-evaluation-strategy.md), so untrusted repositories must not be inspected in a privileged, secrets-bearing workflow without an explicit trust review. This is especially important when a persistence token is available to the same job.

## Versioning

The Action follows [ADR 0002](decisions/0002-github-action-distribution-strategy.md): immutable release tags pin the same exact Inspector package version, moving major/minor aliases may move only within compatibility boundaries, and Action `v1` stays within inspection schema major `1`.

## CI validation

Repository CI exercises the composite Action itself with `uses: ./` on Ubuntu, Windows, and macOS. The smoke test packs the exact Action version locally, installs it through the same isolated bootstrap path, runs a real fixture inspection, validates outputs, and exercises `exclude` plus `classify` inputs. An additional Ubuntu scenario verifies propagation of a non-zero Inspector result and its partial report.
