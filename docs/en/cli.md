# Command-line interface

**Languages:** English | [Português (Brasil)](../pt-BR/cli.md)

The CLI is the delivery boundary for running DotNetRepoInspector locally or from automation. It delegates repository analysis to `DotNetRepoInspector.Engine` and serializes the resulting `InspectionReport` with the versioned Core JSON contract.

DotNetRepoInspector is packaged as a .NET Tool with package ID `DotNetRepoInspector` and tool command `dotnet-repo-inspect`. Because the command uses the `dotnet-` prefix, the supported public invocation is:

```bash
dotnet repo-inspect .
```

The direct command `dotnet-repo-inspect .` is also valid for a globally installed tool.

> The repository is configured and CI-validated for .NET Tool packaging, but the package is not published to NuGet.org yet. Until a release is published, use the local-package flow below when validating distribution.

## Install as a .NET Tool

The current tool targets .NET 10 and therefore requires a compatible .NET runtime/SDK on the machine that runs it.

### Global installation

After the package is published to a NuGet feed:

```bash
dotnet tool install --global DotNetRepoInspector
dotnet repo-inspect --help
```

Update a global installation with:

```bash
dotnet tool update --global DotNetRepoInspector
```

Remove it with:

```bash
dotnet tool uninstall --global DotNetRepoInspector
```

### Local installation

A repository can pin the tool in a tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install DotNetRepoInspector
dotnet repo-inspect .
```

When a manifest already exists, do not recreate it. Update the pinned local tool from the directory covered by the manifest:

```bash
dotnet tool update DotNetRepoInspector
```

Restore tools declared by an existing manifest with:

```bash
dotnet tool restore
```

### Build and install from this repository

A package version can be supplied at pack time without editing the project file:

```bash
dotnet pack ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
  --configuration Release \
  --output ./artifacts/packages \
  -p:Version=0.0.0-local
```

Install that exact package globally from the local feed:

```bash
dotnet tool install --global DotNetRepoInspector \
  --version 0.0.0-local \
  --add-source ./artifacts/packages
dotnet repo-inspect --version
```

For a local manifest, run from the manifest directory:

```bash
dotnet tool install DotNetRepoInspector \
  --version 0.0.0-local \
  --add-source ./artifacts/packages
dotnet repo-inspect --help
```

CI uses an isolated local NuGet source and validates package metadata, package contents, global installation, local installation, `--help`, `--version`, and a real fixture inspection before the required `Build, test and quality` job can pass.

## Run from source

Contributors can still execute the CLI project directly without packing it:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- .
```

The first positional argument is the repository path. When omitted, the current directory is inspected.

## Options

```text
-o, --output <file>          Write the inspection JSON to a file instead of stdout.
    --config <file>          Use a repository-relative configuration file.
    --no-config              Ignore the default .dotnetrepoinspector.json file.
    --exclude <path>         Exclude a repository-relative directory or project. Repeatable.
    --classify <path>=<kind> Override one project classification. Repeatable.
    --sink http              Persist a snapshot through the built-in HTTP/webhook sink.
    --sink-url <url>         HTTP/HTTPS endpoint used by the selected sink.
    --sink-timeout-seconds   Overall persistence timeout in seconds. Default: 15.
    --sink-failure-mode      Persistence failure mode: non-fatal or fatal. Default: non-fatal.
    --sink-max-attempts      Maximum HTTP attempts for transient failures. Default: 3.
-v, --verbose                Emit verbose operational logs to stderr.
    --debug                  Emit debug operational logs to stderr.
-h, --help                   Show help.
    --version                Show the CLI/package version.
```

Supported classification kinds are `web`, `worker`, `console`, `library`, `test`, and `unknown`.

Only one repository path may be supplied. The CLI is non-interactive and does not prompt for missing values, making its behavior suitable for CI.

## Project discovery contract

Project discovery is case-insensitive for supported project extensions. Files ending in `.csproj`, `.CSPROJ`, or any equivalent casing are treated as the same supported project format, while the original filesystem casing is preserved in the reported project path.

Automation and fleet-inventory consumers should treat the Inspector report's `projects` collection as the canonical discovery result. Do not use a separate case-sensitive pre-scan such as `find ... -name '*.csproj'` as the source of truth, because it can disagree with the Inspector on case-sensitive filesystems. If a pre-scan is unavoidable for optimization, it must use case-insensitive extension matching (for example, `find ... -iname '*.csproj'`) and remain advisory rather than authoritative.

## Repository configuration

When `.dotnetrepoinspector.json` exists at the inspected repository root, it is loaded automatically. The file can define repository-relative exclusions and explicit classification overrides. It is completely optional; when absent, the existing zero-configuration behavior is preserved.

Use `--config` to select another repository-relative file, or `--no-config` to skip automatic loading of the default file. `--config` and `--no-config` are mutually exclusive.

Direct `--exclude` values are additive to file exclusions. A direct `--classify` entry has precedence over a file classification override for the same project. See [`configuration.md`](configuration.md) for the versioned file format, path rules, override provenance, diagnostics, and full precedence policy.

Examples:

```bash
dotnet repo-inspect . \
  --exclude generated \
  --exclude samples/Legacy.csproj \
  --classify src/App/App.csproj=web

dotnet repo-inspect . --config config/inspector.json
dotnet repo-inspect . --no-config --classify src/App/App.csproj=web
```

Malformed CLI option values are rejected before inspection and return exit code `2`. Invalid repository configuration discovered by the Engine produces a normal JSON report containing `DRI1013/error` and returns exit code `1`.

## Optional HTTP snapshot persistence

Persistence is disabled unless `--sink http` is selected. A normal invocation continues to inspect locally without contacting a persistence endpoint.

Minimal example:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

The HTTP adapter posts the canonical `InspectionSnapshot` envelope after the inspection report has been produced. It sends the snapshot idempotency key in the `Idempotency-Key` header and retries only failures classified as transient.

The endpoint must be an absolute HTTP or HTTPS URL and must not contain embedded credentials. `--sink-timeout-seconds` accepts `1..300`; `--sink-max-attempts` accepts `1..5`.

Bearer credentials are deliberately not accepted as command-line arguments. Provide them only through `DOTNET_REPO_INSPECTOR_HTTP_TOKEN` or the host's equivalent secret facility:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Do not place sink credentials in `.dotnetrepoinspector.json`, the endpoint URL, committed scripts, or inspection JSON.

`--sink-failure-mode non-fatal` is the default. A failed persistence attempt is logged to stderr while preserving the inspection's normal exit semantics. With `--sink-failure-mode fatal`, a persistence failure returns exit code `5` after the report has already been produced.

See [`persistence.md`](persistence.md) for snapshot provenance, idempotency, retry classification, timeout, cancellation, and security semantics.

## Output streams

For a normal inspection without `--output`, **stdout contains only the inspection JSON**. Operational logs, warnings, and errors are written to **stderr**. This keeps pipelines such as the following safe:

```bash
dotnet repo-inspect . > inspection.json
```

With `--output`, the JSON is written to the requested UTF-8 file and stdout remains empty:

```bash
dotnet repo-inspect . --output artifacts/inspection.json
```

Persistence does not change the inspection JSON contract. The JSON is produced by `InspectionJsonSerializer` and follows the same versioned and deterministic contract documented under [`schema/inspection-v1.md`](schema/inspection-v1.md).

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Inspection completed and no error-severity diagnostics were produced. |
| `1` | A report was produced, but it contains one or more error-severity diagnostics, including invalid repository configuration. |
| `2` | Command-line arguments are invalid. |
| `3` | A fatal inspection or serialization failure prevented a usable report. |
| `4` | The report could not be written to stdout or the requested file. |
| `5` | Snapshot persistence failed while configured with `--sink-failure-mode fatal`. |
| `130` | The operation was cancelled, including inspection, persistence, or process interruption such as Ctrl+C. |

A code of `1` is intentionally different from a fatal failure: the JSON report still exists and contains the structured diagnostics that explain the partial inspection result. Code `5` also occurs after the inspection report has been produced; it represents failure to deliver the optional snapshot, not a mutation of inspection diagnostics.

Exit code `1` is aggregate: it is returned when an `error` diagnostic exists either in top-level `diagnostics` or in any `projects[].diagnostics`. It is not a per-project status. Consumers must derive each project's health only from that project's own `diagnostics` collection and must use top-level `diagnostics` independently for repository/inspection health. See [`diagnostics.md`](diagnostics.md) for the aggregation rules and examples.

## Cancellation

The process handles Ctrl+C cooperatively. The cancellation token is propagated through the CLI into the inspection engine and, when enabled, snapshot publication and the HTTP request. A cancelled invocation exits with code `130` and does not start an interactive recovery flow.

## Examples

Inspect the current repository and pipe JSON to another process:

```bash
dotnet repo-inspect . | jq '.projects[].classification.kind'
```

Inspect another repository and save the report:

```bash
dotnet repo-inspect ../service --output inspection.json
```

Enable operational detail without contaminating JSON stdout:

```bash
dotnet repo-inspect . --verbose > inspection.json
```

Inspect, save the local report, and require snapshot persistence:

```bash
dotnet repo-inspect . \
  --output artifacts/inspection.json \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-failure-mode fatal
```
