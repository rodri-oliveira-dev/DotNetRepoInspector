# DotNetRepoInspector 1.2.0 and MCP server

> Draft release notes. Version `1.2.0` and `DotNetRepoInspector.Mcp` are not published. This document must remain a draft until the protected GA workflow and post-publication gates pass.

## Highlights

- New local, read-only `DotNetRepoInspector.Mcp` server over stdio.
- Six deterministic tools backed by the existing Engine and evaluated MSBuild/Git facts.
- Explicit repository-root boundary with path canonicalization and traversal/link defenses.
- Framework-dependent NuGet `McpServer` and .NET Tool packaging with exact-version `dnx` support.
- Protected OIDC/Trusted Publishing release path with package validation, hashes, attestations, manifest, and post-publication smoke.
- Versioned deterministic eval dataset and client-compatibility evidence, without provider SDKs in product code.

## Stable MCP tool catalog

`inspect_repository`, `list_projects`, `get_project_details`, `get_project_reference_graph`, `get_repository_diagnostics`, and `get_sdk_metadata` form the frozen v1 catalog. All are read-only and use stdio with an explicit `--root`.

## Security and limitations

MSBuild evaluation is not a sandbox. Inspect untrusted repositories only in isolated, ephemeral, least-privileged environments without credentials. The server does not call an LLM; subsequent data handling follows the MCP client's policy. Remote HTTP, write tools, RAG, embeddings, and provider-specific runtime behavior are not part of this release.

## Installation after publication

```bash
dnx DotNetRepoInspector.Mcp@1.2.0 --yes -- --root /absolute/path/to/repository
```

This command is intentionally prospective until NuGet.org publication is verified. See [GA readiness](mcp-ga-readiness.md) before presenting it as available.

## Compatibility status

OpenAI Codex has real-client evidence against the development server. Claude Code and Gemini CLI have reproducible configurations but no project validation evidence. GA requires a second provider against the exact published package.
