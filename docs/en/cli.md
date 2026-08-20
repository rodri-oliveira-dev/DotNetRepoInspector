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
-o, --output <file>   Write the inspection JSON to a file instead of stdout.
-v, --verbose         Emit verbose operational logs to stderr.
    --debug           Emit debug operational logs to stderr.
-h, --help            Show help.
    --version         Show the CLI/package version.
```

Only one repository path may be supplied. The CLI is non-interactive and does not prompt for missing values, making its behavior suitable for CI.

## Output streams

For a normal inspection without `--output`, **stdout contains only the inspection JSON**. Operational logs, warnings, and errors are written to **stderr**. This keeps pipelines such as the following safe:

```bash
dotnet repo-inspect . > inspection.json
```

With `--output`, the JSON is written to the requested UTF-8 file and stdout remains empty:

```bash
dotnet repo-inspect . --output artifacts/inspection.json
```

The JSON is produced by `InspectionJsonSerializer` and therefore follows the same versioned and deterministic contract documented under [`schema/inspection-v1.md`](schema/inspection-v1.md).

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Inspection completed and no error-severity diagnostics were produced. |
| `1` | A report was produced, but it contains one or more error-severity diagnostics. |
| `2` | Command-line arguments are invalid. |
| `3` | A fatal inspection or serialization failure prevented a usable report. |
| `4` | The report could not be written to stdout or the requested file. |
| `130` | The operation was cancelled, including process interruption such as Ctrl+C. |

A code of `1` is intentionally different from a fatal failure: the JSON report still exists and contains the structured diagnostics that explain the partial inspection result.

## Cancellation

The process handles Ctrl+C cooperatively. The cancellation token is propagated through the CLI into the inspection engine and its long-running adapters. A cancelled invocation exits with code `130` and does not start an interactive recovery flow.

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
