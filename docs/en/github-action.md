# GitHub Action

**Languages:** English | [Português (Brasil)](../pt-BR/github-action.md)

DotNetRepoInspector provides a reusable composite GitHub Action that runs the same .NET Tool and inspection engine used by the CLI. The Action is a delivery adapter only: project discovery, MSBuild evaluation, classification, diagnostics, JSON serialization, and exit semantics remain owned by the existing Inspector.

> The Action implementation is present and validated in repository CI, but a public `v1` release is not published yet. `uses: rodri-oliveira-dev/DotNetRepoInspector@v1` becomes available after the release workflow publishes the matching `DotNetRepoInspector` package and creates the Action tags.

## Minimal usage

The Action does not check out source code implicitly. A workflow should check out the repository first:

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

The Action intentionally does not expose an `inspector-version` input. Each released Action revision pins one exact .NET Tool version so a specific Action ref remains reproducible.

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
steps:
  - name: Checkout
    uses: actions/checkout@v7

  - name: Inspect .NET repository
    id: inspect
    uses: rodri-oliveira-dev/DotNetRepoInspector@v1
    with:
      path: .
      output: artifacts/inspection.json
      verbosity: verbose

  - name: Use inspection metadata
    shell: pwsh
    env:
      REPORT_PATH: ${{ steps.inspect.outputs.report-path }}
      SCHEMA_VERSION: ${{ steps.inspect.outputs.schema-version }}
      INSPECTOR_VERSION: ${{ steps.inspect.outputs.inspector-version }}
    run: |
      Write-Host "Schema: $env:SCHEMA_VERSION"
      Write-Host "Inspector: $env:INSPECTOR_VERSION"
      $report = Get-Content -LiteralPath $env:REPORT_PATH -Raw | ConvertFrom-Json
      Write-Host "Projects: $(@($report.projects).Count)"
```

## Exit behavior

The Action preserves the CLI exit codes instead of translating them into a separate Action-specific policy:

| Code | Meaning |
| ---: | --- |
| `0` | Inspection completed without error diagnostics. |
| `1` | A report was produced but contains one or more `error` diagnostics. |
| `2` | Invalid CLI/Action arguments reached the CLI boundary. |
| `3` | Fatal inspection failure prevented a normal report. |
| `4` | The report could not be written. |
| `130` | Inspection was cancelled. |

When the CLI returns a non-zero code, the Action publishes outputs that are still available and then fails with the same code. A workflow that intentionally wants to inspect those outputs after a failure can use normal GitHub Actions failure-control mechanisms such as `continue-on-error` and an `if: always()` follow-up step.

## Runtime and repository SDKs

The composite Action bootstraps the .NET 10 SDK required to execute the Inspector by using `actions/setup-dotnet` pinned to a full commit SHA in `action.yml`.

That bootstrap is intentionally separate from SDKs required by the repository being inspected. If a repository's `global.json` requires an SDK that is not already available on the runner, the workflow must install that SDK before invoking the Inspector.

For example, a repository that requires .NET 8 and is inspected by the .NET 10-based Inspector may install both SDK families side by side.

## Tool bootstrap and package-source isolation

The Action installs `DotNetRepoInspector` into an invocation-specific directory under `RUNNER_TEMP` by using `dotnet tool install --tool-path`.

It does not:

- install the tool globally;
- modify the inspected repository's tool manifest;
- trust repository or machine `NuGet.config` package sources for the Inspector package;
- select `latest`, a wildcard, or an arbitrary caller-provided Inspector version.

Instead, the Action creates a temporary NuGet configuration with inherited package sources cleared and resolves the exact pinned package version from NuGet.org. This isolation applies only to bootstrapping the Inspector; it does not change package configuration used by the inspected repository.

Repository CI has a narrowly scoped self-test hook that can replace only the package source with the locally packed `1.0.0` package. The hook is rejected outside `rodri-oliveira-dev/DotNetRepoInspector` and does not change the pinned version or the public Action inputs.

## Permissions and trust boundary

Inspection of an already checked-out repository needs no GitHub API access, token, secret, or write permission. Consumers remain responsible for permissions used by their own checkout and subsequent workflow steps.

MSBuild evaluation is not a sandbox. The Inspector evaluates repository-controlled MSBuild configuration according to [ADR 0001](decisions/0001-msbuild-evaluation-strategy.md), so untrusted repositories must not be inspected in a privileged, secrets-bearing workflow without an explicit trust review.

## Versioning

The Action follows [ADR 0002](decisions/0002-github-action-distribution-strategy.md):

- an immutable release tag such as `v1.2.3` pins `DotNetRepoInspector` package `1.2.3` exactly;
- moving aliases such as `v1` and optionally `v1.2` may move only to compatible releases;
- Action major version `v1` must remain compatible with inspection schema major version `1`;
- package publication and Action tag movement are release concerns and are not performed by pull-request validation.

## CI validation

Repository CI exercises the composite Action itself with `uses: ./` on Ubuntu, Windows, and macOS. The smoke test packs the exact Action version locally, installs it through the same isolated bootstrap path, runs a real fixture inspection, and validates `report-path`, `schema-version`, `inspector-version`, and `exit-code`.

An additional Ubuntu scenario inspects the missing-SDK fixture and verifies that CLI exit code `1`, the partial JSON report, and diagnostic `DRI1002` are preserved by the Action wrapper.
