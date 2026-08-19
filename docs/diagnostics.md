# Diagnostics and operational logging

DotNetRepoInspector separates **inspection diagnostics** from **operational logs**.

Inspection diagnostics are part of the normalized result and are intended for both people and automation. Operational logs explain how the tool executed and never change the normalized inspection schema.

## Stable diagnostic catalog

| Code | Default severity | Meaning |
| --- | --- | --- |
| `DRI1001` | `error` | A project could not be inspected or its project file could not be read. |
| `DRI1002` | `error` | The required .NET SDK could not be resolved. |
| `DRI1003` | `warning` | A project reference could not be resolved. |
| `DRI1004` | `warning` | An expected project property could not be evaluated. |
| `DRI1005` | `error` | The applicable `global.json` is invalid. |
| `DRI1006` | `error` | MSBuild could not evaluate a project. |
| `DRI1007` | `error` | MSBuild returned an invalid structured result. |
| `DRI1008` | `error` | The .NET host could not be started. |
| `DRI1009` | `error` | The inspection request is invalid. |
| `DRI1010` | `error` | The repository root is unavailable. |
| `DRI1011` | `error` | The applicable `global.json` could not be read. |
| `DRI1012` | `warning` | Repository metadata could not be fully collected. |

Codes are stable identifiers. Existing codes must not be repurposed for a different meaning. Automation should use `code` and `severity`, not message text.

## Diagnostic fields

- `code`: stable `DRIxxxx` identifier.
- `severity`: `info`, `warning`, or `error`.
- `message`: stable human-readable summary owned by DotNetRepoInspector.
- `source`: optional normalized path or component identifier associated with the diagnostic.
- `details`: optional controlled human-readable detail. It must not be the only source of machine semantics.
- `context`: optional structured string map. Keys and values must be non-sensitive and are serialized in deterministic key order.

Infrastructure adapters translate internal MSBuild and SDK failures to this catalog. Raw localized error messages are deliberately not required for classification of the failure.

## Operational logs

Operational logs are emitted to **stderr**. JSON output belongs exclusively on **stdout**. This separation allows callers to pipe or parse stdout as JSON even when verbose logging is enabled.

The CLI supports these verbosity modes:

- normal: informational, warning, and error operational events only;
- `--verbose` or `-v`: also emits verbose execution context;
- `--debug`: emits verbose and debug execution context.

Debug logging must not print raw command-line arguments, environment variables, source-code content, process dumps, credentials, authorization headers, connection strings, tokens, or secrets.

`CliLogger` accepts structured string context and applies defense-in-depth redaction to keys that look sensitive. Callers must still use stable, non-sensitive messages and avoid embedding secrets directly in log messages.
