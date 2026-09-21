# MCP general availability readiness

**Languages:** English | [Portuguese (Brazil)](../pt-BR/mcp-ga-readiness.md)

This document is the SDD and evidence record for roadmap issue #138. The planned first stable MCP package is **`DotNetRepoInspector.Mcp` `1.2.0`**, versioned in lockstep with the repository. It is not published and must not be described as generally available while the mandatory gates below remain open.

## Specification

GA requires a completed and published RC, zero untreated critical/high findings, current bilingual documentation and compatibility evidence, and a green machine-readable readiness contract. The stable package must be published only by the protected Release workflow from an allowed ref, with Trusted Publishing and required human approval. The exact NuGet.org package must then pass `dnx`, MCP handshake, discovery, primary tool calls, deterministic evals, hashes, attestations, provenance, and post-publication smoke tests. Additional provider clients are compatibility evidence, not GA gates.

The public v1 tool catalog is frozen for `1.2.0`:

| Tool | Stable contract |
| --- | --- |
| `inspect_repository` | Canonical `InspectionReport` envelope |
| `list_projects` | Compact deterministic project facts |
| `get_project_details` | One repository-relative project |
| `get_project_reference_graph` | Reference edges and unresolved diagnostics |
| `get_repository_diagnostics` | Repository and project diagnostic facts |
| `get_sdk_metadata` | Configured and resolved SDK metadata |

Inputs, outputs, error envelopes, `mcpSchemaVersion` `1.0`, read-only annotations, stdio transport, and explicit `--root` remain as documented in [the MCP guide](mcp.md) and [server contract](architecture/mcp-server.md). No incompatible contract change is part of GA preparation.

## Plan

1. Review RC findings and keep GA blocked until #137 is complete.
2. Validate `1.2.0` locally and in a controlled feed, then run all repository gates.
3. Merge the consolidated PR only after required reviews and checks.
4. Confirm NuGet.org Trusted Publishing policy and protected `release` environment approval.
5. Dispatch the Release workflow from the allowed ref with version `1.2.0` and `publish=true`.
6. Verify NuGet.org, exact-version `dnx`, deterministic MCP validations, the established Codex smoke, release assets, manifest, hashes, attestations, and post-publication checks.
7. Mark GA and the roadmap complete only after all evidence is attached to #138.

## Tasks and current evidence

| Gate | State | Evidence or dependency |
| --- | --- | --- |
| RC review | Blocked | #137 remains open; its blocker findings are unresolved |
| Security | Prepared | Repository security suites and GitHub alert surfaces are checked; publication requires a fresh protected run |
| Contract freeze | Complete | Six tools listed above and in machine-readable readiness |
| Stable package metadata | Prepared | `1.2.0` can be packed and inspected; NuGet.org evidence does not exist |
| Compatibility | Prepared | Codex is validated and deterministic MCP protocol/evals are green; additional providers in #133/#139 are non-blocking follow-ups |
| Performance/reliability | Prepared | `.github/mcp-performance-baseline.json`, bounded queue, cancellation, timeout, and stderr telemetry |
| Existing distributions | Prepared | CLI, Action, and container retain independent readiness tracking; container work does not gate MCP GA |
| Supply chain | Blocked | Official hashes, attestations, provenance, SBOM, and manifest require the protected publication run |
| Publication | Blocked | Merge/allowed ref, environment approval, and NuGet.org Trusted Publishing policy are external gates |

The GA-candidate measurement on Windows x64/.NET 10 recorded 68 ms process launch, 968 ms startup plus handshake, 11.43 s Engine inspection, 11.75 s CLI inspection, and MCP tool calls from 10.38 s to 11.22 s for the six-project `ProjectKinds` fixture. All ratios and fixed budgets passed. These values are regression evidence for the controlled fixture, not an SLA for arbitrary repositories.

## Implementation

`.github/release-readiness-v1.json` records the stable version, frozen tools, baseline, documentation, blocking issues, and external controls. Tests require this state to remain `blocked` until evidence changes deliberately. Draft release notes are maintained at [`mcp-1.2.0-release-notes.md`](mcp-1.2.0-release-notes.md).

## GA blockers

- #137: no RC is published, so exact public package and post-publication evidence do not exist.
- NuGet.org Trusted Publishing policy for `DotNetRepoInspector.Mcp` is not confirmed.
- The consolidated PR is not merged to an allowed release ref, and protected environment approval has not occurred.

These are mandatory MCP publication gates, not vNext candidates. Container release-readiness tracked in #106 and additional provider validation tracked in #133/#139 remain independent of MCP GA. Existing post-v1 work such as richer classifications (#25) and optional policies (#28) remains outside GA and does not alter the frozen MCP v1 contract.
