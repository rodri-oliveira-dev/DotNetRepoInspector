# MCP server

**Languages:** English | [Portuguese (Brazil)](../../pt-BR/architecture/mcp-server.md)

`DotNetRepoInspector.Mcp` is the local stdio delivery adapter defined by [ADR 0006](../decisions/0006-mcp-adapter-architecture.md). It delegates repository inspection to `IRepositoryInspector`; it does not contain a second discovery, MSBuild evaluation, classification, or Git implementation.

## Specification

### Functional requirements

- Startup requires exactly one existing repository directory through `--root <path>` or `--root=<path>`.
- The normalized absolute root is immutable for the lifetime of the process and is the maximum filesystem scope accepted from tool arguments.
- The server uses stdio only and publishes the name `DotNetRepoInspector.Mcp` and the product assembly version during MCP negotiation.
- The MVP publishes `inspect_repository`, `list_projects`, `get_project_details`, `get_project_reference_graph`, `get_repository_diagnostics`, and `get_sdk_metadata`. Every tool maps options to the existing `RepositoryInspectionRequest`; granular tools only project focused views from the canonical `InspectionReport`.
- Client cancellation propagates to `IRepositoryInspector.InspectAsync`. Closing stdin ends the server gracefully.
- Inspections for the configured root execute one at a time with at most eight queued calls and a five-minute server timeout.

### Non-functional requirements

- stdout is reserved for MCP protocol messages. Generic Host and MCP operational logs are structured JSON on stderr.
- The server and tool are read-only, deterministic for the same repository state/toolchain, model-agnostic, and free of LLM-provider SDKs.
- Absolute paths, empty paths, and relative paths that resolve outside the configured root are rejected before the Engine is called.
- Expected errors expose stable codes and sanitized messages, never exception text, stack traces, environment-variable values, or secrets.
- Git and .NET/MSBuild child processes receive a closed stdin so they cannot consume or retain the MCP protocol stream.

## Plan

The executable host owns startup parsing, root validation, DI, MCP metadata, stdio transport, stderr logging, and process lifetime. `RepositoryInspectionExecutor` centralizes adapter validation and the single call to `IRepositoryInspector`; full and granular handlers map its outcome to their declared response schemas. `RepositoryInspector` remains the only inspection orchestrator and `InspectionJsonSerializer` remains the canonical full-report serialization boundary.

Tests are split into fast parser/handler tests and process-level protocol tests. The protocol tests launch the compiled server through the official C# SDK client, negotiate capabilities, list tools, inspect real fixture repositories, and compare the returned report with a direct Engine result.

## Tasks

1. Create the MCP host and test projects and add them to the solution.
2. Configure the stable official MCP C# SDK package, stdio, DI, metadata, stderr logging, and graceful shutdown.
3. Implement and test the explicit `--root` startup boundary.
4. Implement `inspect_repository`, structured output, sanitized failures, path validation, and cancellation propagation.
5. Prove protocol negotiation, discovery, fixture execution, Engine equivalence, invalid-root behavior, diagnostics, stdout isolation, and CLI regression safety.
6. Add the five granular read-only projections with closed input/output schemas and deterministic empty/partial results.
7. Exercise every MVP tool through a real stdio child process, including invalid inputs, missing projects/SDKs, unresolved references, cancellation, and shutdown.

## Implementation

The host uses `ModelContextProtocol` 2.2.0 and `Microsoft.Extensions.Hosting` 10.0.12 through Central Package Management. The MCP project references Core and Engine, while Core and Engine have no MCP dependency. The protocol suite uses the official SDK client to launch the server executable and requires no network, credentials, or LLM. Framework-dependent NuGet/.NET Tool packaging is implemented and validated against controlled local feeds; public publication and GA approval remain deferred to the protected release process.

Start the unpackaged host from the repository build output with an explicit root:

```text
dotnet DotNetRepoInspector.Mcp.dll --root /absolute/path/to/repository
```

Invalid startup arguments return exit code `2`, write one sanitized error to stderr, and write nothing to stdout. A valid process runs until its cancellation token is cancelled or stdin reaches EOF.

## Tool contract

### `inspect_repository`

The tool is advertised as read-only, non-destructive, idempotent, and closed-world. All input properties are optional because the repository itself is fixed at startup:

```json
{
  "configurationPath": ".dotnetrepoinspector.json",
  "disableConfigurationFile": false,
  "excludedPaths": ["src/Legacy/Legacy.csproj"],
  "classificationOverrides": {
    "src/Web/Web.csproj": "web"
  }
}
```

`configurationPath`, every `excludedPaths` item, and every `classificationOverrides` key must be a non-empty repository-relative path that remains below the configured root. `configurationPath` and `disableConfigurationFile: true` are mutually exclusive. Unknown JSON properties are rejected by the SDK-generated input schema.

A successful result sets MCP `isError` to `false`. `data.report` is the existing `InspectionReport`, including repository metadata, configured/resolved SDK facts, projects, evaluated classifications, references, and repository/project diagnostics:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "report": {
      "schemaVersion": "1.3"
    }
  },
  "error": null
}
```

An expected adapter or fatal Engine failure sets MCP `isError` to `true`:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": false,
  "data": null,
  "error": {
    "code": "path_outside_repository_root",
    "message": "The supplied path resolves outside the configured repository root.",
    "details": {}
  }
}
```

Current tool error codes are `invalid_tool_input`, `path_outside_repository_root`, `path_through_link`, `input_too_large`, `result_too_large`, `server_busy`, `inspection_timed_out`, and `inspection_failed`. Inputs are bounded to 1,024-character relative paths, 256 exclusions, 256 classification overrides with 128-character values, and 1 MiB configuration files. Successful MCP results are limited to 8 MiB UTF-8. Missing SDKs, malformed projects, unavailable Git metadata, and other recoverable inspection failures remain canonical `InspectionReport` diagnostics. Request cancellation is protocol-native: it propagates as cancellation instead of being converted to a tool envelope.

The complete security rationale and residual-risk statement are in the [MCP threat model](mcp-threat-model.md). In particular, the root boundary constrains tool arguments but does not sandbox MSBuild imports, property functions, SDK resolvers, child processes, filesystem access, or network access.

### Granular tools

All granular tools accept the same optional inspection properties as `inspect_repository`, reject unknown properties, and return `data.inspectionSchemaVersion`. `get_project_details` additionally requires a normalized repository-relative `projectPath`; an absent project returns `project_not_found`.

| Tool | Focused data |
| --- | --- |
| `list_projects` | `projects` with path, name, target frameworks, classification, and diagnostic counts. |
| `get_project_details` | One canonical `ProjectInspection` as `project`. |
| `get_project_reference_graph` | Project paths, canonical references, and unresolved-reference diagnostics. |
| `get_repository_diagnostics` | Repository and project diagnostics with nullable `projectPath` context. |
| `get_sdk_metadata` | Canonical configured and resolved SDK facts as `dotNetSdk`. |

An empty repository returns empty arrays. Recoverable conditions such as a missing SDK, malformed project, or unresolved reference remain successful partial results carrying canonical diagnostics. Fatal inspection failures use `inspection_failed`; cancellation propagates through the protocol without a tool envelope.

## References

- [Official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP C# SDK: stdio transport](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)
- [MCP C# SDK: tools and structured content](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tools/tools.md)
- [ModelContextProtocol 2.2.0 on NuGet](https://www.nuget.org/packages/ModelContextProtocol/2.2.0)
