# MCP agent compatibility and evals

**Languages:** English | [Portuguese (Brazil)](../pt-BR/mcp-agent-compatibility.md)

This page records the reproducible compatibility matrix and eval plan for the local stdio `DotNetRepoInspector.Mcp` server. It covers roadmap issues #133 and #139 without adding provider SDKs or probabilistic LLM dependencies to product code.

## Scope and sources

The server under test is the same `DotNetRepoInspector.Mcp` binary documented in [`architecture/mcp-server.md`](architecture/mcp-server.md). The compatibility target is the MCP stdio boundary: initialize, tool discovery, and calls to the read-only tools that project facts from the canonical `InspectionReport`.

Official client documentation reviewed on 2026-09-20:

- OpenAI Codex MCP configuration: <https://learn.chatgpt.com/docs/extend/mcp?surface=cli>
- Claude Code MCP configuration: <https://code.claude.com/docs/en/mcp>
- Gemini CLI MCP server configuration: <https://github.com/google-gemini/gemini-cli/blob/main/docs/tools/mcp-server.md>

The MCP server uses `ModelContextProtocol` `2.2.0`. The deterministic eval runner records MCP protocol version `2025-06-18` in its reports.

## Specification

Compatibility requirements:

- every client must launch the same server executable with stdio transport;
- every client must pass the repository root explicitly through `--root`;
- handshake must publish server name `DotNetRepoInspector.Mcp`, server version, capabilities, and the same six tools;
- discovery must include `inspect_repository`, `list_projects`, `get_project_details`, `get_project_reference_graph`, `get_repository_diagnostics`, and `get_sdk_metadata`;
- primary tool calls must return the same structured facts for the same fixture root;
- no OpenAI, Anthropic, Google, Gemini, or other provider SDK is added to `DotNetRepoInspector.Mcp`;
- client-specific credentials, transcripts, and API keys must not be committed.

Eval requirements:

- dataset is versioned under `evals/DotNetRepoInspector.Mcp.Evals/Dataset/`;
- ground truth is derived from fixtures and `InspectionReport` projections;
- reports are emitted as Markdown and JSON under `artifacts/mcp-evals/`;
- deterministic assertions are preferred whenever objective verification exists;
- LLM-as-judge is not a source of truth for deterministic facts;
- probabilistic live-client runs stay opt-in and outside the default CI gate.

## Plan

The deterministic runner uses the official MCP client SDK as a protocol harness. It does not call a model. Each eval case starts the same stdio server for a synthetic fixture root, lists tools, calls the expected tool, and validates structured content with objective assertions.

Live client validation uses the same binary and fixture roots, but each client owns its own configuration surface:

- Codex: `codex mcp add <name> -- <command> --root <root>`
- Claude Code: `claude mcp add --transport stdio <name> -- <command> --root <root>`
- Gemini CLI: `settings.json` `mcpServers.<name>.command` and `args`

Provider/client evals must compare structured facts rather than requiring identical final prose. A client passes the smoke only when there is evidence of a real tool call and the returned facts match the deterministic ground truth.

## Tasks

The versioned eval dataset covers:

| Case | Fixture | Expected tool | Objective facts |
| --- | --- | --- | --- |
| `tfm-project-index` | `ProjectKinds` | `list_projects` | target frameworks for Web and MultiTargeting projects |
| `classification-project-kinds` | `ProjectKinds` | `list_projects` | Web, Worker, Console, Library, Test, and MultiTargeting classifications |
| `dependency-fan-out` | `ProjectReferences/FanOut` | `get_project_reference_graph` | A references B and C |
| `diagnostics-missing-sdk` | `Compatibility/MissingSdk` | `get_repository_diagnostics` | diagnostic `DRI1002` |
| `diagnostics-unresolved-reference` | `ProjectReferences/Unresolved` | `get_project_reference_graph` | missing reference plus `DRI1003` |
| `graph-chain-interpretation` | `ProjectReferences/Chain` | `get_project_reference_graph` | A -> B -> C |
| `sdk-metadata-global-json` | `Sdk/WithGlobalJson` | `get_sdk_metadata` | configured SDK `10.0.100` |

Metrics:

- **Task completion:** all deterministic assertions in a case pass.
- **Tool selection:** the observed tool sequence is exactly the expected tool for the case.
- **Unnecessary calls:** calls beyond the expected minimal tool call.
- **Factual fidelity:** passed factual assertions divided by total factual assertions.
- **Groundedness:** deterministic runner returns only facts from structured tool output; live client runs must cite or preserve the tool-derived facts.
- **Unsupported claims:** failed deterministic assertions or live-client statements not supported by tool output.

## Implementation

Build the server and eval runner:

```bash
dotnet build src/DotNetRepoInspector.Mcp/DotNetRepoInspector.Mcp.csproj --configuration Release
dotnet build evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj --configuration Release
```

Run deterministic evals on Windows:

```bash
dotnet run --project evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj \
  --configuration Release \
  -- \
  --server src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp.exe \
  --fixtures tests/Fixtures \
  --output artifacts/mcp-evals \
  --client mcp-sdk-deterministic \
  --provider protocol \
  --model deterministic-assertions \
  --client-version 2.2.0
```

On Linux/macOS, use the extensionless server executable path under the same `bin/Release/net10.0/` directory.

Historical deterministic development-binary run (not the packaged RC):

- timestamp UTC: `2026-09-20T08:25:59.7270206+00:00`
- OS/runtime: Windows `10.0.26200.0`, `.NET 10.0.12`, x64
- server-reported version for this development binary: `DotNetRepoInspector.Mcp` `1.0.0` (not the `1.2.0-rc.1` package identity)
- discovered tools: all six MVP tools
- result: 7/7 cases completed
- task completion: 100%
- tool selection: 100%
- factual fidelity: 100%
- groundedness: 100%
- unnecessary calls: 0
- unsupported claims: 0

RC package evidence is recorded separately in [issue #137](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/137#issuecomment-5749538826): the **exact `DotNetRepoInspector.Mcp` package version `1.2.0-rc.1`**, resolved by `dnx` from a **controlled local feed**, passed package/protocol validation, **7/7 deterministic fixture evals**, and **1/1 real-repository smoke**. The [protected release dry-run](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/runs/35507871398) used `publish=false`. This establishes deterministic RC-package evidence, **not** public NuGet.org publication or a Codex smoke against the RC artifact.

## Compatibility Matrix

| Client | Provider | Version used | MCP protocol | Stdio config | Root config | Handshake | Discovery | Tool execution | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OpenAI Codex CLI | OpenAI | `codex-cli 0.154.0-alpha.6.2` | `2025-06-18` through `ModelContextProtocol` server | `codex mcp add dri -- <server> --root <root>` | explicit `--root` argument | validated by real client run | validated by `mcp_tool_call` to `list_projects` | validated: `list_projects` returned 6 projects | Passed historical local-binary Codex smoke; exact RC artifact not validated |
| Claude Code | Anthropic | not observable in the #149 execution environment (2026-09-29) | not observed live; package uses stdio and deterministic harness records `2025-06-18` | `claude mcp add --transport stdio dotnet-repo-inspector -- dnx DotNetRepoInspector.Mcp@1.5.2 --yes -- --root <root>` | explicit `--root` argument | blocked by external client environment | blocked by external client environment | blocked before client launch | External impediment documented by #149; live smoke tracked in #177 |
| Gemini CLI | Google | not observable in the #160 execution environment (2026-09-29) | not observed live; package uses stdio and deterministic harness records `2025-06-18` | `settings.json` with `command: dnx` and `DotNetRepoInspector.Mcp@1.5.2 --yes -- --root <root>` args | explicit `--root` argument | blocked by external client environment | blocked by external client environment | blocked before client launch | External impediment documented by #160; live smoke tracked in #178 |
| MCP SDK deterministic harness | Protocol harness | `ModelContextProtocol` `2.2.0` | `2025-06-18` | `StdioClientTransport` | explicit `--root` argument per fixture | validated | validated | validated across all MVP fact categories | Passed deterministic protocol eval |

Release note: OpenAI Codex CLI was validated against a local development executable, but its exact artifact/package version was not recorded. Do not treat that historical smoke as validation of `1.2.0-rc.1`. The exact `1.2.0-rc.1` package passed deterministic evals from a controlled local feed as recorded above; a Codex smoke against the **exact published RC package** remains pending until publication and must be recorded before GA promotion. Claude Code remains unvalidated because #149 recorded an external execution-environment impediment; its live smoke is tracked explicitly in #177. Gemini CLI likewise remains unvalidated because #160 recorded an external execution-environment impediment; its live smoke is tracked in #178.

Before launching either live client, isolate NuGet package resolution so the evidence is attributable to the exact public stable package rather than a private/local feed or a previously populated global cache.

Create a temporary NuGet configuration containing only NuGet.org:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Then use a fresh, initially empty package cache for the live-client process:

```bash
export NUGET_PACKAGES="$(mktemp -d)"
```

The `dnx` arguments below also pass the exclusive config/source explicitly and disable the HTTP cache, matching the isolation strategy used by `.github/scripts/invoke_mcp_package_smoke.ps1`.

## Claude Code validation attempt for #149 (2026-09-29)

Issue #149 uses a versioned, deterministic fixture as the ground truth for the live-client smoke:

- controlled root: `tests/Fixtures/ProjectKinds`;
- expected project inventory: exactly 6 projects: `Console/Console.csproj`, `Library/Library.csproj`, `MultiTargeting/MultiTargeting.csproj`, `Test/Test.csproj`, `Web/Web.csproj`, and `Worker/Worker.csproj`;
- `list_projects` ground truth: Web=`web`, Worker=`worker`, Console=`console`, Library=`library`, Test=`test`, MultiTargeting=`library`; `Web/Web.csproj` targets `net10.0`; `MultiTargeting/MultiTargeting.csproj` targets `net8.0` and `net10.0`;
- `inspect_repository` ground truth: the same six-project inventory and the same canonical per-project target-framework/classification facts projected by `InspectionReport`;
- authoritative source: the versioned `mcp-evals-v1.json` dataset plus the `ProjectKinds` fixture projects themselves.

### External impediment recorded by #149

The stable package selected for the run is `DotNetRepoInspector.Mcp@1.5.2`, the current stable release at the time of the attempt. The intended server command is:

```bash
dnx DotNetRepoInspector.Mcp@1.5.2 \
  --configfile /absolute/path/to/NuGet.Config \
  --source https://api.nuget.org/v3/index.json \
  --no-http-cache \
  --yes -- \
  --root <absolute-path>/tests/Fixtures/ProjectKinds
```

The execution environment available for #149 exposed GitHub repository operations but did not expose an executable host shell or an installed/authenticated Claude Code session. Consequently:

- Claude Code version: **not observable in this execution environment**;
- Claude Code authorization state: **not observable**;
- MCP protocol negotiated with Claude Code: **not observed**;
- handshake: **not executed**;
- tool discovery: **not executed**;
- `list_projects`: **not executed by Claude Code**;
- `inspect_repository`: **not executed by Claude Code**.

The deterministic harness protocol `2025-06-18` remains useful repository evidence, but it is deliberately **not** presented as a protocol version observed from Claude Code. No product or provider-specific code was changed to work around the environment.

The required live validation is tracked explicitly in [#177](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/177). Only non-sensitive evidence should be recorded when that follow-up runs: client version, package version, negotiated protocol, tool discovery summary, structured fact summary, and pass/fail comparison. Credentials, tokens, and raw sensitive transcripts must not be committed.

### Reproduction when Claude Code is available

```bash
claude --version

claude mcp add --transport stdio dotnet-repo-inspector -- \
  dnx DotNetRepoInspector.Mcp@1.5.2 \
  --configfile /absolute/path/to/NuGet.Config \
  --source https://api.nuget.org/v3/index.json \
  --no-http-cache \
  --yes -- \
  --root <absolute-path>/tests/Fixtures/ProjectKinds

claude mcp list
```

Then use `/mcp` in Claude Code and execute both:

```text
Use dotnet-repo-inspector list_projects. Return the project paths, target frameworks, and classifications from the tool result.
```

```text
Use dotnet-repo-inspector inspect_repository. Return the project count and the same per-project target framework and classification facts from the tool result.
```

Compare the structured facts with the ground truth above, record the negotiated MCP protocol and client/package versions, then remove the temporary MCP registration.

## Gemini CLI validation attempt for #160 (2026-09-29)

Issue #160 reuses the same deterministic ground truth established for Group 5:

- controlled root: `tests/Fixtures/ProjectKinds`;
- expected project inventory: exactly 6 projects: `Console/Console.csproj`, `Library/Library.csproj`, `MultiTargeting/MultiTargeting.csproj`, `Test/Test.csproj`, `Web/Web.csproj`, and `Worker/Worker.csproj`;
- `list_projects` ground truth: Web=`web`, Worker=`worker`, Console=`console`, Library=`library`, Test=`test`, MultiTargeting=`library`; `Web/Web.csproj` targets `net10.0`; `MultiTargeting/MultiTargeting.csproj` targets `net8.0` and `net10.0`;
- `inspect_repository` ground truth: the same six-project inventory and the same canonical per-project target-framework/classification facts projected by `InspectionReport`;
- authoritative source: the versioned `mcp-evals-v1.json` dataset plus the `ProjectKinds` fixture projects themselves.

### External impediment recorded by #160

The stable package selected for the run is `DotNetRepoInspector.Mcp@1.5.2`, the current stable release at the time of the attempt. The intended server configuration uses `dnx` over stdio:

```json
{
  "mcpServers": {
    "dotnetRepoInspector": {
      "command": "dnx",
      "args": [
        "DotNetRepoInspector.Mcp@1.5.2",
        "--configfile",
        "/absolute/path/to/NuGet.Config",
        "--source",
        "https://api.nuget.org/v3/index.json",
        "--no-http-cache",
        "--yes",
        "--",
        "--root",
        "<absolute-path>/tests/Fixtures/ProjectKinds"
      ],
      "trust": false
    }
  }
}
```

The execution environment available for #160 exposed GitHub repository operations but did not expose an executable host shell or an installed/authenticated Gemini CLI session. Consequently:

- Gemini CLI version: **not observable in this execution environment**;
- Gemini CLI authorization state: **not observable**;
- MCP protocol negotiated with Gemini CLI: **not observed**;
- handshake: **not executed**;
- tool discovery: **not executed**;
- `list_projects`: **not executed by Gemini CLI**;
- `inspect_repository`: **not executed by Gemini CLI**.

The deterministic harness protocol `2025-06-18` remains useful repository evidence, but it is deliberately **not** presented as a protocol version observed from Gemini CLI. No product or provider-specific code was changed to work around the environment.

The required live validation is tracked explicitly in [#178](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/178). Only non-sensitive evidence should be recorded when that follow-up runs: client version, package version, negotiated protocol, tool discovery summary, structured fact summary, and pass/fail comparison. Credentials, tokens, and raw sensitive transcripts must not be committed.

### Reproduction when Gemini CLI is available

Record the client version:

```bash
gemini --version
```

Configure the MCP server with the exact stable package as shown above, then start Gemini CLI and execute both:

```text
Use the dotnetRepoInspector MCP server list_projects tool. Return the project paths, target frameworks, and classifications from the tool result.
```

```text
Use the dotnetRepoInspector MCP server inspect_repository tool. Return the project count and the same per-project target framework and classification facts from the tool result.
```

Compare the structured facts with the ground truth above and record the negotiated MCP protocol and client/package versions.

## Reproducible Smoke Tests

### Codex CLI

```bash
codex mcp add dri -- \
  ./src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp \
  --root ./tests/Fixtures/ProjectKinds

codex mcp list
codex exec --ephemeral --json --sandbox read-only \
  "Use the MCP server named dri. Call list_projects. Return only JSON with keys tool_used and project_count."

codex mcp remove dri
```

Expected evidence: JSONL contains an `mcp_tool_call` item with server `dri`, tool `list_projects`, `status` `completed`, and structured content whose `data.projects` length is 6.

Historical Codex smoke on 2026-09-20 (local development executable; exact package version not recorded):

- command accepted stdio config;
- `codex mcp list` showed server `dri` enabled with stdio command and `--root`;
- `codex exec --json` emitted a real `mcp_tool_call` for `dri/list_projects`;
- final answer reported `{"tool_used":"mcp__dri.list_projects","project_count":6}`;
- temporary global MCP config was removed after validation.

After the `1.2.0-rc.1` package is **published to NuGet.org**, repeat this smoke against an isolated NuGet environment so no package from the earlier controlled local feed can be reused.

Use a temporary, initially empty `NUGET_PACKAGES` directory and a temporary `NuGet.Config` containing only:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Then register the Codex stdio server with the exact package and source, for example:

```bash
export NUGET_PACKAGES="$(mktemp -d)"
codex mcp add dri -- \
  dnx DotNetRepoInspector.Mcp@1.2.0-rc.1 \
  --configfile /absolute/path/to/NuGet.Config \
  --source https://api.nuget.org/v3/index.json \
  --no-http-cache \
  --yes -- \
  --root <absolute-fixture-root>
```

The protected release workflow performs the same isolation in `.github/scripts/invoke_mcp_package_smoke.ps1`: the configured source is exclusive, `NUGET_PACKAGES` is recreated empty for each attempt, and the HTTP cache is disabled. The script also writes `package-source-evidence.json` with the exact PackageId, version, source, cache path, and isolation mode. Record that evidence together with the `mcp_tool_call` event and factual result. This **post-publication Codex RC smoke is pending**, not part of the historical result above.

### Claude Code

```bash
claude mcp add --transport stdio dotnet-repo-inspector -- \
  ./src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp \
  --root ./tests/Fixtures/ProjectKinds

claude mcp list
```

In Claude Code, run `/mcp` and ask:

```text
Use dotnet-repo-inspector list_projects and report the project count and classifications.
```

Expected facts: six projects, including Web=`web`, Worker=`worker`, Console=`console`, Library=`library`, Test=`test`, and MultiTargeting=`library`.

### Gemini CLI

Add to the appropriate Gemini CLI `settings.json`:

```json
{
  "mcpServers": {
    "dotnetRepoInspector": {
      "command": "./src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp",
      "args": ["--root", "./tests/Fixtures/ProjectKinds"],
      "trust": false
    }
  }
}
```

Then ask Gemini CLI:

```text
Use the dotnetRepoInspector MCP server list_projects tool and report the project count and target frameworks.
```

Expected facts: six projects; `MultiTargeting/MultiTargeting.csproj` has `net8.0` and `net10.0`.

## Limitations

- The deterministic runner proves protocol compatibility and factual assertions, not real LLM behavior.
- Codex CLI was the only external provider client installed and authorized in the validation environment.
- Claude Code live validation is blocked by the external execution environment recorded in #149 and tracked in #177; Gemini CLI live validation is blocked by the external execution environment recorded in #160 and tracked in #178.
- Live-client transcripts should be reduced to non-sensitive evidence such as client version, tool-call event, structured result summary, and pass/fail facts.
