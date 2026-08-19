# Command-line interface

The CLI is the delivery boundary for running DotNetRepoInspector locally or from automation. It delegates repository analysis to `DotNetRepoInspector.Engine` and serializes the resulting `InspectionReport` with the versioned Core JSON contract.

> The CLI is implemented but is not yet packaged as a .NET Tool. Tool packaging and the final public `repo-inspect` command are tracked separately. During development, run the CLI project directly.

## Run from source

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
    --version         Show the CLI version.
```

Only one repository path may be supplied. The CLI is non-interactive and does not prompt for missing values, making its behavior suitable for CI.

## Output streams

For a normal inspection without `--output`, **stdout contains only the inspection JSON**. Operational logs, warnings, and errors are written to **stderr**. This keeps pipelines such as the following safe:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- . > inspection.json
```

With `--output`, the JSON is written to the requested UTF-8 file and stdout remains empty:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- . --output artifacts/inspection.json
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
dotnet run --project ./src/DotNetRepoInspector.Cli -- . | jq '.projects[].classification.kind'
```

Inspect another repository and save the report:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- ../service --output inspection.json
```

Enable operational detail without contaminating JSON stdout:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- . --verbose > inspection.json
```
