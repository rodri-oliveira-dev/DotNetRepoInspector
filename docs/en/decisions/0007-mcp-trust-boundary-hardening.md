# ADR 0007: Harden the local MCP trust boundary

**Languages:** English | [Portuguese (Brazil)](../../pt-BR/decisions/0007-mcp-trust-boundary-hardening.md)

- **Status:** Accepted
- **Date:** 2026-09-19
- **Decision owners:** DotNetRepoInspector maintainers

## Context

ADR 0006 defined a local, stdio, read-only MCP adapter with an explicit repository root. Tool path validation was lexical, while repository configuration could still be reached through a link below the root. The MCP process also inherited the client's complete environment before the existing `dotnet` child filter applied. Large client inputs or inspection results had no adapter-level bound.

MSBuild evaluation remains capable of reading environment variables, files, imports, and network resources available to the OS identity. Path checks cannot make that evaluation a sandbox.

## Decision

The MCP v1 trust boundary uses these defense-in-depth rules:

1. Resolve linked segments in the explicit root path once at startup and store the final absolute directory.
2. Reject every tool-controlled path that is absolute, escapes lexically, or crosses a symbolic link, junction, or reparse point below that root. Project discovery continues to skip reparse points.
3. Apply the same rule to explicit and default configuration files. Limit configuration files to 1 MiB.
4. Limit relative paths to 1,024 characters, exclusions and overrides to 256 entries each, override values to 128 characters, and MCP results to 8 MiB UTF-8.
5. Remove credential-like environment variables and credential-handle/configuration variables from the MCP process before host/Engine startup. Keep the existing second filter for `dotnet`/MSBuild children.
6. Canonically serialize and deserialize an `InspectionReport` before MCP projections, reusing normalization and sensitive diagnostic-context redaction.
7. Keep every v1 tool read-only, non-destructive, idempotent, closed-world, and without command/executable inputs.

The checks fail closed with stable errors (`path_through_link`, `input_too_large`, or `result_too_large`) and do not echo supplied values.

## Consequences

The adapter prevents direct client-selected link traversal and bounds common accidental denial-of-service payloads without changing `InspectionReport`. Repositories that intentionally place their DotNetRepoInspector configuration behind a link must use a regular file inside the root instead.

Environment filtering can break private SDK/feed resolution that depends on credential variables. Pre-provisioning dependencies or using a dedicated isolated identity is preferred to exposing credentials to untrusted evaluation.

TOCTOU remains possible because validation and use are separate operations. MSBuild can follow repository-controlled imports/references or execute property functions outside the logical root. Users must inspect untrusted repositories only inside an external OS/container/VM boundary without secrets and with restricted filesystem/network access.

## Alternatives considered

- **Allow links whose current target is inside root:** rejected because target swaps create a larger and harder-to-explain race surface.
- **Clear the entire process environment:** rejected because SDK discovery and process startup need a minimal platform environment; name-based removal preserves compatibility while reducing exposure.
- **Return arbitrarily large reports:** rejected because stdio clients and hosts need a predictable response ceiling.
- **Claim root validation sandboxes MSBuild:** rejected as technically false.

## References

- [MCP threat model](../architecture/mcp-threat-model.md)
- [Security and privacy](../security.md)
- [ADR 0006](0006-mcp-adapter-architecture.md)
- Issue #130: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/130
