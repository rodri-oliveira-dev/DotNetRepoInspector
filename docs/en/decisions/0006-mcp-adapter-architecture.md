# ADR 0006: Define the MCP adapter architecture and MVP contract

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0006-mcp-adapter-architecture.md)

- **Status:** Accepted
- **Date:** 2026-09-19
- **Decision owners:** DotNetRepoInspector maintainers

## Context

DotNetRepoInspector already has a stable inspection core: `DotNetRepoInspector.Core` owns normalized contracts, `DotNetRepoInspector.Engine` exposes `IRepositoryInspector`, infrastructure adapters collect Git/MSBuild evidence, and the CLI serializes the public `InspectionReport`.

Issue #125 starts the `DotNetRepoInspector.Mcp` roadmap. This first step must define the architecture and contract before implementation issues create the host, tools, tests, distribution workflow, and end-user documentation. The MCP surface must not become a second inspection engine, a policy engine, a remote service, or a model-provider integration.

Current MCP/.NET packaging conventions matter to this decision because the ecosystem is still evolving. The checked guidance for this ADR is:

- the official MCP C# SDK exposes `ModelContextProtocol` for stdio servers using hosting/DI and attribute-based discovery;
- a stdio server communicates MCP protocol messages over stdin/stdout and should send logs to stderr;
- NuGet MCP servers are .NET tool packages executed through `dnx`;
- NuGet.org encourages an embedded `.mcp/server.json` manifest and the `McpServer` package type;
- the published server can be consumed by MCP clients such as VS Code, Visual Studio, GitHub Copilot coding agent, Claude Code, Cursor, and other protocol-compatible hosts.

## Specification

### Objectives

`DotNetRepoInspector.Mcp` must expose deterministic repository inspection facts to MCP clients while preserving the existing domain boundaries:

1. MCP is a delivery adapter over `DotNetRepoInspector.Engine`.
2. `IRepositoryInspector` remains the source of truth for inspection behavior.
3. The server is local, read-only, stdio-based, and model/provider agnostic.
4. The repository root is an explicit startup boundary and every tool request is constrained to that boundary.
5. The tool catalog is small enough for the MVP but structured so later granular tools remain derived views over the same inspection report.

### Requirements

- Create `src/DotNetRepoInspector.Mcp` as a host/adapter project in the implementation issue.
- Reference `DotNetRepoInspector.Engine` and `DotNetRepoInspector.Core`; do not reference the CLI as the runtime composition layer.
- Use the official MCP C# SDK package appropriate for stdio hosting (`ModelContextProtocol`) and `Microsoft.Extensions.Hosting`.
- Use stdio as the only MVP transport through `.WithStdioServerTransport()`.
- Register read-only tools from the MCP adapter assembly.
- Accept an explicit repository root at server startup through `--root <path>`.
- Resolve the startup root to an existing absolute path before serving any tool.
- Reject tool inputs that are absolute paths, path traversal attempts, malformed relative paths, or paths resolving outside the startup root.
- Propagate MCP request cancellation to `IRepositoryInspector.InspectAsync`.
- Keep MCP protocol payloads on stdout/stdin only; operational logs, diagnostics about server startup, and host messages must go to stderr.
- Return machine-readable tool results and errors; do not require an LLM-specific parser.
- Preserve `InspectionReport` as the canonical inspection payload and avoid public JSON contract changes for this ADR.

### Architectural restrictions

- Do not duplicate discovery, MSBuild evaluation, classification, Git metadata, JSON serialization, or diagnostic rules in the MCP project.
- Do not add OpenAI, Anthropic, Google, Gemini, GitHub Copilot, or other LLM/provider SDKs to product code.
- Do not add Streamable HTTP, SSE, remote hosting, authentication, RAG, embeddings, source-code generation, write tools, persistence side effects, or repository mutation to the MVP.
- Do not inspect source file contents merely to satisfy MCP requests.
- Do not silently default to an arbitrary current directory as the trust boundary.

### Contracts

The MCP adapter has its own tool-result contract, but successful inspection data remains the existing `InspectionReport`.

All tool responses use a JSON object emitted as MCP tool content:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {}
}
```

Errors use the same envelope with `ok: false`:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": false,
  "error": {
    "code": "repository_root_required",
    "message": "The MCP server requires an explicit repository root.",
    "details": {}
  }
}
```

The MCP envelope version is independent from `InspectionReport.schemaVersion`. Changing the envelope in a breaking way requires a new MCP major version; changing `InspectionReport` remains governed by the existing inspection schema compatibility policy.

Expected MVP error codes:

| Code | Meaning |
| --- | --- |
| `repository_root_required` | The server was started without an explicit repository root. |
| `repository_root_not_found` | The configured root does not exist or is not a directory. |
| `path_outside_repository_root` | A supplied relative path resolves outside the startup root. |
| `invalid_tool_input` | Input JSON is malformed or violates the tool schema. |
| `inspection_cancelled` | The MCP request or process shutdown cancelled inspection. |
| `inspection_failed` | The engine failed before an `InspectionReport` could be produced. |
| `project_not_found` | A project-specific tool could not find the requested repository-relative project path. |

## Plan

### Dependency direction

The MCP project is an adapter at the delivery edge:

```text
MCP client
   |
   v
DotNetRepoInspector.Mcp
   |
   v
DotNetRepoInspector.Engine
   |
   +--> DotNetRepoInspector.Core
   +--> DotNetRepoInspector.MSBuild
   +--> DotNetRepoInspector.Git
```

`DotNetRepoInspector.Mcp` may depend on hosting, the MCP C# SDK, Engine, and Core contracts. Core must not depend on MCP. Engine must not know about MCP transports, tool names, MCP error envelopes, clients, or model providers.

### Trust boundary

The configured repository root is the maximum filesystem scope for MCP tool inputs. The adapter validates paths before calling the engine or deriving a view. The boundary limits accidental or malicious tool arguments, but it is not a sandbox for MSBuild evaluation. Untrusted repositories still require isolated, ephemeral, non-privileged execution without secrets, as documented for the CLI/container.

The MVP does not grant write access. No MCP tool creates, edits, deletes, formats, restores, commits, persists, or uploads repository files.

### Transport and host behavior

The MVP uses stdio only. This matches local MCP use, NuGet/dnx execution, and IDE/agent clients that launch a child process. Streamable HTTP is explicitly out of scope until a later ADR revisits remote hosting, authentication, host validation, CORS, and operational exposure.

The host must configure logging so all logs go to stderr. Any stdout write outside the MCP transport is a protocol bug.

### Tool catalog

The initial v1 tool catalog is:

| Tool | Purpose | Implementation issue |
| --- | --- | --- |
| `inspect_repository` | Run `IRepositoryInspector` for the configured root and return the full canonical `InspectionReport`. | #127 |
| `list_projects` | Return a compact project index derived from the same inspection report: path, name, target frameworks, classification, and diagnostic counts. | #128 |
| `get_project_details` | Return one `ProjectInspection` by repository-relative project path, derived from an inspection report. | #128 |
| `get_project_reference_graph` | Return project-reference edges and unresolved-reference diagnostics. | #128 |
| `get_repository_diagnostics` | Return repository-level diagnostics and project diagnostics with their project path context. | #128 |
| `get_sdk_metadata` | Return configured and resolved .NET SDK metadata. | #128 |

No prompts or resources are part of the MVP. Tools are intentionally read-only and deterministic; client-side prompts may decide how to use the facts, but the server does not ask a model to interpret them.

### Tool input and output schemas

Common inspection options accepted by tools that run or derive from inspection:

```json
{
  "type": "object",
  "properties": {
    "configurationPath": {
      "type": "string",
      "description": "Optional repository-relative path to a DotNetRepoInspector configuration file."
    },
    "disableConfigurationFile": {
      "type": "boolean",
      "default": false
    },
    "excludedPaths": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Optional repository-relative project paths to exclude."
    },
    "classificationOverrides": {
      "type": "object",
      "additionalProperties": { "type": "string" }
    }
  },
  "additionalProperties": false
}
```

`inspect_repository` output:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "report": "InspectionReport"
  }
}
```

`list_projects` output:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "inspectionSchemaVersion": "1.3",
    "projects": [
      {
        "path": "src/App/App.csproj",
        "name": "App",
        "targetFrameworks": ["net10.0"],
        "classification": {
          "kind": "web",
          "confidence": "high"
        },
        "diagnosticCount": 0,
        "errorCount": 0,
        "warningCount": 0
      }
    ]
  }
}
```

`get_project_details` adds one required input:

```json
{
  "projectPath": "src/App/App.csproj"
}
```

Its output contains a single `ProjectInspection` as `data.project`.

`get_project_reference_graph` returns `data.projects`, where each item contains a project `path`, its canonical `references`, and unresolved-reference `diagnostics`.

`get_repository_diagnostics` output:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "inspectionSchemaVersion": "1.3",
    "diagnostics": [
      {
        "projectPath": null,
        "diagnostic": "InspectionDiagnostic"
      }
    ]
  }
}
```

`get_sdk_metadata` returns the canonical `DotNetSdkMetadata` as `data.dotNetSdk`. Every granular response also carries `data.inspectionSchemaVersion`, so consumers can trace projected facts to the versioned `InspectionReport` contract. Empty and recoverable partial inspections are successful results with empty collections, nullable SDK facts, or canonical diagnostics; they are not adapter errors.

### Versioning and package identity

The MCP server has a separate NuGet package identity:

```text
PackageId: DotNetRepoInspector.Mcp
Tool command: dotnet-repo-inspector-mcp
MCP package type: McpServer
MCP registry name: io.github.rodri-oliveira-dev/dotnet-repo-inspector-mcp
```

The package should be versioned in lockstep with the repository/product release unless a later release ADR defines a separate cadence. Lockstep versioning keeps CLI, Action, container, JSON contract, and MCP facts attributable to the same source revision.

For the MVP, the NuGet package should be framework-dependent rather than self-contained. The server already requires a .NET SDK-capable environment for `dnx`, and repository inspection requires SDK/MSBuild availability that a self-contained runtime would not solve. Self-contained or native AOT packaging can be reconsidered only if distribution evidence shows a concrete benefit and the MSBuild/SDK compatibility contract remains intact.

### Distribution strategy

The package is distributed as a NuGet .NET tool package executable through `dnx`, with an embedded `.mcp/server.json` manifest and `PackageType` `McpServer`. The manifest should describe stdio transport, package identity, package version, repository URL, and a required repository-root startup input.

Publication must use the protected release pipeline and Trusted Publishing model already established for the repository. This ADR does not publish a package or change the release pipeline; implementation belongs to the dedicated distribution issues.

### Multi-client compatibility

Compatibility is defined at the MCP protocol and local stdio process boundary. Codex, Claude Code, Gemini CLI, VS Code, Visual Studio, GitHub Copilot coding agent, Cursor, and similar clients are consumers of the same protocol surface. Product code must not branch on LLM provider, include provider SDKs, or embed provider-specific prompts as runtime behavior.

Client compatibility tests may use different hosts, but those tests validate protocol interoperability and tool selection; they do not change the server architecture.

## Tasks

The roadmap decomposes this ADR into implementation work:

1. #126 creates the stdio host project, startup options, DI composition, stderr logging, cancellation wiring, and repository-root validation.
2. #127 implements `inspect_repository` over `IRepositoryInspector` and proves the canonical `InspectionReport` round trip.
3. #128 implements the derived granular tools without duplicating inspection logic.
4. #129 adds protocol/E2E tests that exercise stdio without network, secrets, or model providers.
5. #130 hardens path-boundary and trust-boundary behavior.
6. #131 covers reliability, observability, cancellation, timeout, and performance guardrails.
7. #132 adds CI quality gates for the MCP server.
8. #133 and #139 validate multi-client behavior as protocol evidence, not product coupling.
9. #134 creates end-user MCP documentation in both languages.
10. #135 and #136 implement NuGet/dnx packaging and protected publication.
11. #137 and #138 ship prerelease/GA only after the preceding evidence is complete.

## Implementation

Issue #125 is complete when this ADR is present in both languages, indexed, and reflected in the architecture overview. No MCP host or tools are implemented by this issue. No `InspectionReport` field is added, removed, or renamed.

## Alternatives considered

### Put MCP tools in the CLI project

Rejected. The CLI owns argument parsing, stdout JSON emission, persistence composition, and exit codes. An MCP server has different transport and protocol responsibilities. Sharing the engine is correct; sharing the CLI host would couple two delivery protocols and increase the risk of stdout pollution.

### Make MCP the new inspection orchestration layer

Rejected. `IRepositoryInspector` already centralizes the inspection workflow and failure semantics. Duplicating it in MCP would create inconsistent behavior across CLI, Action, container, and MCP.

### Use Streamable HTTP for the MVP

Rejected. HTTP is useful for remote/server scenarios but introduces authentication, host validation, CORS, lifecycle, and deployment concerns that are outside the local read-only MVP. stdio matches local agent and IDE usage.

### Couple compatibility to named LLM providers

Rejected. The product should be protocol-compatible, not provider-specific. Provider SDKs would add unnecessary dependencies and trust boundaries while providing no deterministic inspection value.

### Ship a self-contained MCP package first

Rejected for MVP. It increases package size and release complexity while not removing the need for a .NET SDK/MSBuild toolchain to inspect repositories accurately.

## Consequences

### Positive

- MCP becomes an adapter over the existing engine instead of a second product core.
- The root boundary, read-only scope, and no-provider-SDK rule are explicit before implementation.
- The tool catalog is small but covers both full-report and focused-agent workflows.
- NuGet/dnx distribution aligns with current .NET MCP conventions.
- `InspectionReport` remains stable and reusable across CLI, Action, container, persistence, and MCP.

### Trade-offs

- stdio-only MVP does not support remote clients without a local process bridge.
- Framework-dependent packaging requires a compatible .NET SDK/dnx environment.
- Derived tools may rerun inspection unless a later implementation adds a bounded in-process cache with clear invalidation.
- Repository-root validation reduces accidental overreach but does not make MSBuild evaluation safe for hostile repositories.

## References

- Issue #125: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/125
- Roadmap #140: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/140
- MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- MCP C# SDK transport guidance: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md
- MCP C# SDK getting started: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md
- Microsoft Learn: MCP servers in NuGet packages: https://learn.microsoft.com/nuget/concepts/nuget-mcp
- Microsoft Learn: Create a minimal MCP server using C# and publish to NuGet: https://learn.microsoft.com/dotnet/ai/quickstarts/build-mcp-server
- Microsoft Learn: Publish an MCP server on NuGet.org to the Official MCP Registry: https://learn.microsoft.com/dotnet/ai/quickstarts/publish-mcp-registry
- Inspection engine: [`../inspection-engine.md`](../inspection-engine.md)
- Security model: [`../security.md`](../security.md)
- Release/versioning: [`../releases.md`](../releases.md)
