# MCP release candidate readiness

**Languages:** English | [Portuguese (Brazil)](../pt-BR/mcp-release-candidate.md)

This report defines and records the release-candidate work for roadmap issue #137. The selected identity is **`1.2.0-rc.1`**: the latest public product release is `1.1.0`, and adding the MCP distribution is a backward-compatible product capability, so the next lockstep version is a minor prerelease.

## Specification

The RC must use the protected release workflow and the same exact version for the CLI, MCP package, GitHub Action, container, manifest, and release tag. Entry requires completed #133, #134, #135, #136, and #139; green repository gates; no untreated critical MCP security issue; and the applicable #106 release baseline. Promotion to GA is prohibited until the published package is resolved by exact version from NuGet.org and at least two real MCP clients from different providers pass against that package.

Evidence must distinguish a local build, a package resolved from a controlled local feed, a package published to NuGet.org, and a run by a real provider client. Deterministic protocol assertions do not substitute for multi-provider evidence.

## Plan

1. Validate entry issues and security state before enabling publication.
2. Build, test, pack, inspect metadata, and execute `1.2.0-rc.1` from a controlled feed.
3. Run the fixture eval dataset, the repository solution, and known error fixtures through the packaged server.
4. Run the protected workflow with `publish=false` and retain its manifest, checksums, and package artifacts.
5. Only after all entry gates and administrative Trusted Publishing policy are confirmed, dispatch `publish=true` from an allowed ref and obtain the required environment approval.
6. Resolve the exact published version with `dnx`, rerun protocol/evals, and validate at least two provider clients before promotion.

## Tasks and evidence

| Gate | Required evidence | Current state |
| --- | --- | --- |
| Package and protocol | Exact prerelease package, metadata validation, handshake, `tools/list`, and `inspect_repository` | Passed from controlled feed for `1.2.0-rc.1` |
| Deterministic evals | Seven versioned fixture cases, including missing SDK and unresolved reference errors | Passed 7/7 through exact-version `dnx` |
| Real repository | Repository solution inspected by the packaged server | Passed; MCP project reported `net10.0` |
| Multi-client | Two real provider clients using the published package | **Blocked:** only OpenAI Codex has prior real-client evidence |
| Publication | Exact version available from NuGet.org | **Blocked:** package ID currently has no published versions |
| Supply chain | Release manifest, SHA-256 sums, attestations, container SBOM/provenance | Prepared by the protected workflow; official evidence requires publication run |

## Findings

| ID | Severity | Impact and reproduction | Resolution required |
| --- | --- | --- | --- |
| `RC-001` | Blocker | Issue #133 remains open because Claude Code and Gemini CLI are unavailable in the validation environment; only one provider is proven. | Validate the exact published RC with Claude Code or Gemini CLI and attach non-sensitive tool-call evidence. |
| `RC-002` | Blocker | Issue #139 remains open because the eval suite has not run through two provider clients. | Run the versioned suite with a second provider and compare structured facts, tool selection, groundedness, and unsupported claims. |
| `RC-003` | Blocker | Issue #106 remains open, while the official workflow publishes the lockstep container and requires its release baseline. | Complete or explicitly resolve the applicable #106 publication-readiness criteria before approval. |
| `RC-004` | Blocker | `DotNetRepoInspector.Mcp` returns no versions from the NuGet.org flat-container endpoint. Published-package, page, provenance, and post-publication smoke evidence do not exist. | Configure/activate the NuGet.org Trusted Publishing policy and perform the authorized protected prerelease publication. |
| `RC-005` | Medium | The machine-readable v1 baseline still identifies the historical `1.0.0` baseline while public releases have advanced to `1.1.0`. It remains valid as a v1 contract baseline but is not the selected RC version. | Keep the baseline immutable; record the RC identity separately, as done under `mcp.releaseCandidate`. |

## Implementation status

The repository records `1.2.0-rc.1` as **blocked**, not published. `.github/scripts/validate_mcp_rc.ps1` validates package contents, local tool installation, exact-version `dnx` resolution from a controlled feed, stdio handshake, discovery, `inspect_repository`, and all deterministic eval cases. `.github/release-readiness-v1.json` lists issues #106, #133, and #139 plus the external Trusted Publishing policy as blockers.

Controlled-feed run on 2026-09-20: 7/7 eval cases and 1/1 real-repository case passed with 100% task completion, tool selection, factual fidelity, and groundedness; unnecessary calls and unsupported claims were zero. The error cases verified `DRI1002` and `DRI1003`. Local SHA-256 evidence was recorded for the CLI package, MCP package, and MCP symbols. These results prove the generated package, not NuGet.org publication or real-client compatibility.

No `publish=true` workflow may be dispatched while those blockers remain. Once cleared, the release workflow itself is the only authorized publication path. See [release engineering](releases.md), [client compatibility and evals](mcp-agent-compatibility.md), and [MCP operations](mcp.md).
