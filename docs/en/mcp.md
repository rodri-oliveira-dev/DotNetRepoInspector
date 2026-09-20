# MCP server guide

**Languages:** English | [Portuguese (Brazil)](../pt-BR/mcp.md)

`DotNetRepoInspector.Mcp` is a local, read-only Model Context Protocol server for querying the deterministic facts already produced by DotNetRepoInspector. It is intended for developers, repository maintainers, platform teams, and agent-client integrators who need project inventory, SDK, diagnostic, and reference-graph facts without parsing CLI output themselves.

This guide documents the implementation currently in the repository. It does not describe remote HTTP, repository writes, RAG, or a published NuGet package because those capabilities do not exist yet.

## Specification

The documentation contract is:

- preserve DotNetRepoInspector's identity as a deterministic .NET repository inspector;
- explain local build and stdio startup with an explicit `--root`;
- describe every implemented tool from its generated contract and tested behavior;
- distinguish protocol tests, validated clients, and unvalidated client configurations;
- make the MSBuild trust boundary and client-side data handling explicit;
- provide reproducible engineering tasks and failure diagnosis without requiring an LLM in CI.

Acceptance is based on the MCP implementation, process-level protocol tests, security tests, the client compatibility evidence, synchronized English/PT-BR content, and valid internal references.

## Architecture

```text
MCP client
    |
    | MCP over stdio
    v
DotNetRepoInspector.Mcp
    |
    | IRepositoryInspector
    v
DotNetRepoInspector.Engine
    |                 |
    v                 v
MSBuild adapter     Git adapter
    |                 |
    +--------+--------+
             v
       InspectionReport
```

The MCP process owns startup parsing, stdio transport, tool schemas, input boundaries, response projection, cancellation, and sanitized operational logging. The Engine remains the inspection source of truth and composes evaluated MSBuild facts and Git metadata into the canonical `InspectionReport`. Granular tools project smaller views of that report; they do not reimplement discovery, evaluation, classification, or graph construction.

The dependency direction is `Mcp -> Engine -> MSBuild/Git/Core`. Core, Engine, MSBuild, and Git do not depend on MCP or an LLM provider. See the [MCP architecture specification](architecture/mcp-server.md), [ADR 0006](decisions/0006-mcp-adapter-architecture.md), and [ADR 0008](decisions/0008-mcp-operational-reliability.md).

## Build and start

Prerequisites:

- a .NET 10 SDK compatible with the repository's `global.json`;
- the SDKs required by the repository being inspected;
- Git when Git metadata is expected;
- an MCP client with local stdio-server support.

Build the server from this repository:

```bash
dotnet restore
dotnet build src/DotNetRepoInspector.Mcp/DotNetRepoInspector.Mcp.csproj --configuration Release --no-restore
```

Start the framework-dependent build directly:

```bash
dotnet src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp.dll \
  --root /absolute/path/to/repository
```

The executable under the same output directory can also be used as the client command. On Windows it is `DotNetRepoInspector.Mcp.exe`; on Linux/macOS it has no extension.

The process communicates only through MCP messages on stdout and writes structured operational logs to stderr. A terminal launch appears idle while it waits for MCP input; clients normally create and own this child process. Closing stdin terminates it gracefully.

Invalid startup arguments return exit code `2`. Exactly one `--root <path>` or `--root=<path>` is required, the directory must already exist, and unknown startup arguments are rejected.

### Distribution status and `dnx`

`DotNetRepoInspector.Mcp` is a framework-dependent .NET Tool with package ID `DotNetRepoInspector.Mcp`, command `dotnet-repo-inspector-mcp`, package types `DotnetTool` and `McpServer`, symbols, and an embedded `.mcp/server.json`. The manifest declares stdio and requires the `--root` filepath; it contains no credential input or value.

The protected workflow packs and validates an exact lockstep product version. From a controlled local package source:

```bash
dnx DotNetRepoInspector.Mcp@1.0.0 \
  --source /absolute/path/to/packages \
  --yes -- \
  --root /absolute/path/to/repository
```

The same package can be installed conventionally:

```bash
dotnet tool install --tool-path ./tools DotNetRepoInspector.Mcp \
  --version 1.0.0 --add-source /absolute/path/to/packages
./tools/dotnet-repo-inspector-mcp --root /absolute/path/to/repository
```

The package has not yet been published to NuGet.org. The public `dnx` command becomes consumable only after the protected release workflow publishes that exact version; do not infer publication from a successful local dry-run.

## Repository root and filesystem boundary

`--root` is fixed for the lifetime of the server and is the repository inspected by every tool call. A client configuration should pass it as a separate command argument:

```text
command: dotnet
args: ["/absolute/path/DotNetRepoInspector.Mcp.dll", "--root", "/absolute/path/repository"]
```

Tool paths are repository-relative. Absolute paths, empty paths, traversal outside the root, and paths that cross a filesystem link/reparse point are rejected. Limits include 1,024 characters per relative path, 256 exclusions, 256 classification overrides, a 1 MiB configuration file, and an 8 MiB successful tool result.

This boundary constrains client-supplied paths; it is not an operating-system sandbox. MSBuild evaluation may load repository-controlled props, targets, SDK resolvers, tasks, and property functions, and those components may execute code or access resources available to the process. Inspect untrusted repositories only in an isolated, ephemeral, non-privileged environment without credentials, secrets, or sensitive mounts. See the [MCP threat model](architecture/mcp-threat-model.md).

## Common input and response contracts

All six tools are advertised as read-only, non-destructive, idempotent, and closed-world. Unknown input properties are rejected. Except for the required `projectPath` on `get_project_details`, every input is optional:

| Property | JSON type | Meaning |
| --- | --- | --- |
| `configurationPath` | `string` | Repository-relative path to a DotNetRepoInspector configuration file. |
| `disableConfigurationFile` | `boolean` | Disables both the default and an explicit configuration file. Defaults to `false`. |
| `excludedPaths` | `string[]` | Repository-relative project paths to omit. |
| `classificationOverrides` | `object<string,string>` | Classification values keyed by repository-relative project path. |

`configurationPath` cannot be combined with `disableConfigurationFile: true`. When neither is supplied, the Engine may load `.dotnetrepoinspector.json` from the root. Configuration semantics are described in [configuration.md](configuration.md).

Every structured response uses this envelope:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {},
  "error": null
}
```

On an expected tool failure, `ok` is `false`, `data` is `null`, MCP `isError` is true, and `error` contains `code`, a sanitized `message`, and an empty `details` object. Successful granular responses include `data.inspectionSchemaVersion`; the full tool returns that version inside `data.report.schemaVersion`.

## Tool catalog

### `inspect_repository`

Purpose: return the complete canonical `InspectionReport`, including repository/Git metadata, configured and resolved SDK data, projects, classifications, references, and diagnostics.

Input schema: the four [common inputs](#common-input-and-response-contracts); no required properties and `additionalProperties: false`.

Example request:

```json
{
  "excludedPaths": ["src/Legacy/Legacy.csproj"],
  "classificationOverrides": {
    "src/Web/Web.csproj": "web"
  }
}
```

Abridged response shape (the complete report follows the [inspection schema](schema/inspection-v1.md)):

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "report": {
      "schemaVersion": "1.3",
      "repository": {},
      "dotNetSdk": {},
      "projects": [],
      "diagnostics": []
    }
  },
  "error": null
}
```

Expected errors: all common tool errors listed below. Recoverable project, SDK, configuration, and Git problems are usually diagnostics in the report rather than MCP tool errors.

### `list_projects`

Purpose: return a compact, path-sorted project index for inventory and tool routing.

Input schema: the four common inputs; no required properties and `additionalProperties: false`.

Example request: `{}`

Response data:

```json
{
  "inspectionSchemaVersion": "1.3",
  "projects": [
    {
      "path": "src/App/App.csproj",
      "name": "App",
      "targetFrameworks": ["net10.0"],
      "classification": {
        "kind": "web",
        "confidence": "high",
        "signals": ["sdk:Microsoft.NET.Sdk.Web"]
      },
      "diagnosticCount": 0,
      "errorCount": 0,
      "warningCount": 0
    }
  ]
}
```

Expected errors: common tool errors. An empty repository succeeds with `projects: []`.

### `get_project_details`

Purpose: return one canonical `ProjectInspection` selected by its repository-relative path from `list_projects`.

Input schema: `projectPath` is a required string; the four common inputs are optional; `additionalProperties: false`.

Example request:

```json
{
  "projectPath": "src/App/App.csproj"
}
```

Abridged response data (the complete `ProjectInspection` follows the [inspection schema](schema/inspection-v1.md)):

```json
{
  "inspectionSchemaVersion": "1.3",
  "project": {
    "path": "src/App/App.csproj",
    "name": "App",
    "targetFrameworks": ["net10.0"],
    "references": [],
    "diagnostics": []
  }
}
```

Expected errors: common tool errors plus `project_not_found` when the normalized path is absent from the inspection result.

### `get_project_reference_graph`

Purpose: return every project path with its canonical `ProjectReference` edges and unresolved-reference diagnostics.

Input schema: the four common inputs; no required properties and `additionalProperties: false`.

Example request: `{}`

Response data:

```json
{
  "inspectionSchemaVersion": "1.3",
  "projects": [
    {
      "path": "src/App/App.csproj",
      "references": [
        { "path": "src/Library/Library.csproj" }
      ],
      "diagnostics": []
    }
  ]
}
```

Expected errors: common tool errors. A missing target remains a reference edge and carries diagnostic `DRI1003`; it is not a fatal tool error. See [project-reference-graph.md](project-reference-graph.md).

### `get_repository_diagnostics`

Purpose: return repository-level and project-level diagnostics in one list with nullable project context.

Input schema: the four common inputs; no required properties and `additionalProperties: false`.

Example request: `{ "disableConfigurationFile": true }`

Abridged response data (diagnostic records also carry nullable `source`, `details`, and `context` fields):

```json
{
  "inspectionSchemaVersion": "1.3",
  "diagnostics": [
    {
      "projectPath": "src/App/App.csproj",
      "diagnostic": {
        "code": "DRI1002",
        "severity": "error",
        "message": "The required .NET SDK could not be resolved."
      }
    }
  ]
}
```

Expected errors: common tool errors. No findings is a successful empty `diagnostics` array. Consult the [diagnostic catalog](diagnostics.md) for stable meanings rather than matching message text.

### `get_sdk_metadata`

Purpose: distinguish repository `global.json` configuration, the SDK resolved by `dotnet`, and SDK-resolution diagnostics.

Input schema: the four common inputs; no required properties and `additionalProperties: false`.

Example request: `{}`

Response data:

```json
{
  "inspectionSchemaVersion": "1.3",
  "dotNetSdk": {
    "globalJsonPath": "global.json",
    "configured": {
      "version": "10.0.100",
      "rollForward": "latestFeature",
      "allowPrerelease": false
    },
    "resolvedVersion": "10.0.100"
  }
}
```

Expected errors: common tool errors. An unavailable requested SDK is normally represented by `DRI1002` in the canonical inspection diagnostics and nullable/unresolved SDK facts.

### Common tool errors

| Code | Meaning |
| --- | --- |
| `invalid_tool_input` | A required value is empty, properties conflict, an override value is invalid, or an unknown property was supplied. |
| `path_outside_repository_root` | A path is absolute or resolves outside `--root`. |
| `path_through_link` | A supplied path traverses a filesystem link/reparse point. |
| `input_too_large` | A path, collection, or configuration file exceeds a server limit. |
| `result_too_large` | A successful serialized result would exceed 8 MiB UTF-8. |
| `server_busy` | One inspection is running and all eight queue slots are occupied. Retry later. |
| `inspection_timed_out` | Queue wait plus inspection exceeded five minutes. |
| `inspection_failed` | The Engine failed before an `InspectionReport` could be produced. |
| `project_not_found` | `get_project_details` did not find the requested project in the result. |

Client cancellation is propagated through MCP and does not become an error envelope.

## Client configuration

Use absolute command, DLL, and repository paths in persistent client configuration. Replace placeholders below with local paths and build first.

### OpenAI Codex CLI: validated

The project validated Codex CLI `0.154.0-alpha.6.2` with a real stdio handshake, tool discovery, and `list_projects` call:

```bash
codex mcp add dri -- dotnet /absolute/path/DotNetRepoInspector.Mcp.dll \
  --root /absolute/path/repository
codex mcp list
```

Ask Codex to use `dri/list_projects`, then remove the temporary registration when appropriate:

```bash
codex mcp remove dri
```

### Claude Code: documented, not validated

This setup follows the client documentation but was not executed in the project validation environment because `claude` was not installed:

```bash
claude mcp add --transport stdio dotnet-repo-inspector -- \
  dotnet /absolute/path/DotNetRepoInspector.Mcp.dll \
  --root /absolute/path/repository
claude mcp list
```

Use `/mcp` in Claude Code to inspect server status. This is a reproducible pending-validation route, not a support claim.

### Gemini CLI: documented, not validated

This `settings.json` shape follows Gemini CLI documentation but was not executed in the project validation environment because `gemini` was not installed:

```json
{
  "mcpServers": {
    "dotnetRepoInspector": {
      "command": "dotnet",
      "args": [
        "/absolute/path/DotNetRepoInspector.Mcp.dll",
        "--root",
        "/absolute/path/repository"
      ],
      "trust": false
    }
  }
}
```

Only Codex has real external-client evidence at this time. The official SDK process tests prove MCP protocol behavior but are not evidence of an external LLM client. See the [full compatibility matrix and smoke procedure](mcp-agent-compatibility.md).

## Engineering task examples

- Repository inventory: call `list_projects`; compare classifications, target frameworks, and diagnostic counts without retrieving the full report.
- Framework migration planning: call `list_projects`; identify projects whose `targetFrameworks` do not include the target TFM, then use `get_project_details` for selected projects.
- Dependency impact analysis: call `get_project_reference_graph`; trace incoming/outgoing project edges and flag unresolved `DRI1003` references.
- Build-environment diagnosis: call `get_sdk_metadata`, then `get_repository_diagnostics`; distinguish a `global.json` request from the resolved SDK and look for `DRI1002`.
- CI triage: call `get_repository_diagnostics`; group findings by nullable `projectPath` and stable diagnostic code.
- Auditable snapshot: call `inspect_repository` when the consumer needs the complete versioned contract rather than a focused projection.

Tool output contains facts, not an LLM interpretation. Agents should cite returned paths/codes and avoid unsupported claims.

## Security and data handling

The server itself does not use OpenAI, Anthropic, Google, or other LLM SDKs, does not choose a model, and does not send repository data to a provider. It extracts allow-listed project/repository metadata through the existing Engine. It does not intentionally collect application source text, credentials, environment-variable values, connection strings, or NuGet credentials.

An MCP client receives tool results and may send prompts, results, or derived context to a model according to that client's settings, account, deployment, retention policy, and provider terms. Configure client approvals/trust and data controls before exposing a repository. The server cannot enforce how a client or model handles data after delivery.

Read-only MCP annotations mean the tools do not intentionally modify the repository. They do not make the process a sandbox and cannot neutralize side effects embedded in untrusted MSBuild logic. Environment variables are filtered for child processes and sensitive diagnostic context is redacted, but isolation remains the required control for untrusted input.

## Troubleshooting

### Handshake or discovery fails

- Build the MCP project and point the client at the DLL or platform executable, not the project directory.
- Verify the command works with exactly one existing `--root`; exit code `2` means startup arguments failed.
- Ensure the client is configured for stdio, not HTTP/SSE.
- Check stderr/client logs. Do not add banners, debug prints, or shell wrappers that write to stdout.

### The stdio process appears to hang

This is expected when started manually: the server waits for JSON-RPC/MCP input. Let the MCP client launch it. Ensure no wrapper waits for interactive input and no child process inherits stdin.

### `--root` is rejected

Use one existing directory. Prefer absolute paths in client configuration. Do not place `--root` in the client-only options or combine two root forms. Quote paths with spaces according to the client's argument format.

### A tool path is rejected

Use a repository-relative path returned by `list_projects`. Absolute paths, `..` escapes, empty strings, and link/reparse-point traversal are deliberately blocked. `configurationPath` is also relative to `--root`.

### .NET is missing or the server does not start

Install a compatible .NET 10 SDK/runtime and verify `dotnet --info`. This development build is framework-dependent. A missing host is a client process-launch failure, so no MCP handshake can occur.

### The repository SDK cannot be resolved

Check `global.json`, installed SDKs from `dotnet --list-sdks`, and the [compatibility guide](compatibility.md). A running server normally reports an unavailable repository SDK as diagnostic `DRI1002`; this differs from the .NET 10 host itself being unavailable.

### Stdout contamination or JSON parse errors

stdout is exclusively the MCP transport. Redirect wrapper/application logs to stderr and avoid `Write-Host`, `echo`, shell profiles, or launch scripts that emit text before the server starts. The built-in host already logs to stderr.

### The client cannot launch the server

Use absolute paths, verify working-directory assumptions, quote each argument correctly, and execute the exact command outside the client to inspect exit code/stderr. On Unix, check executable permissions when launching the apphost directly; using `dotnet <dll>` avoids that requirement.

### `dnx` cannot find or run the server

Use .NET SDK 10 or later, pin the package as `DotNetRepoInspector.Mcp@<exact-version>`, and put `dnx` options before `--`; arguments after `--` go to the server. For an unpublished/local package, pass `--source <package-directory>`. A NuGet.org source works only after that exact version has been officially published and indexed.

### Calls are busy, time out, or return too much data

The server runs one inspection at a time, queues eight, and applies a five-minute limit. Retry `server_busy`, reduce repository scope through configuration/exclusions, or use granular tools. `result_too_large` requires a smaller focused result; it is not automatically truncated.

## References

- [MCP architecture and exact contract](architecture/mcp-server.md)
- [MCP threat model](architecture/mcp-threat-model.md)
- [Client compatibility and eval evidence](mcp-agent-compatibility.md)
- [Inspection JSON schema](schema/inspection-v1.md)
- [Diagnostics](diagnostics.md)
- [Security](security.md)
