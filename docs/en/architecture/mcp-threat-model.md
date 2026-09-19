# MCP server threat model

**Languages:** English | [Portuguese (Brazil)](../../pt-BR/architecture/mcp-threat-model.md)

This threat model covers the local stdio `DotNetRepoInspector.Mcp` v1 server. It complements the repository-wide [security model](../security.md), [ADR 0006](../decisions/0006-mcp-adapter-architecture.md), and [ADR 0007](../decisions/0007-mcp-trust-boundary-hardening.md).

## Specification

### Protected assets

- credentials and environment variables held by the MCP client process;
- files outside the explicitly selected repository root;
- protocol integrity of stdin/stdout and operational logs on stderr;
- availability of the MCP host and client;
- integrity of the inspected repository, which must not be changed by v1 tools;
- the deterministic, non-sensitive `InspectionReport` contract.

### Trust boundaries

1. **MCP client to server:** tool names and JSON arguments are untrusted.
2. **Server to repository filesystem:** `--root` is the logical filesystem boundary for tool-controlled paths.
3. **Server to Engine and child processes:** Git and MSBuild operate with the server's OS identity and remaining environment.
4. **Repository to MSBuild:** project files, imports, SDK resolvers, conditions, and property functions are repository-controlled and untrusted.
5. **Server to client:** structured results, protocol errors, logs, and diagnostics must not disclose operational secrets.

### Security requirements

- require one existing `--root`, canonicalize links in the root path once at startup, and keep the result immutable;
- accept only normalized repository-relative tool paths and reject lexical escapes, absolute paths, and any symlink/junction below the canonical root;
- keep all v1 tools read-only, closed-world, idempotent, and free of arbitrary command inputs;
- remove credential-like environment variables before hosting or spawning Engine child processes;
- apply canonical report normalization/redaction before full or granular output;
- cap path lengths, collection counts, configuration size, and MCP result size;
- use sanitized stable errors and keep stdout exclusive to MCP;
- propagate cancellation and terminate child process trees;
- state clearly that these controls do not turn MSBuild into a sandbox.

## Threats And Controls

| Threat | Technical controls | Residual risk |
| --- | --- | --- |
| Path traversal or absolute path access | `Path.GetFullPath`, `Path.GetRelativePath`, root containment checks, closed input schemas, and stable rejection before Engine invocation. | Files can change after validation (TOCTOU). |
| Symlink/junction escape | Root path links are resolved at startup; tool paths crossing any `ReparsePoint` below root are rejected; discovery skips reparse points. | A link can be swapped after validation. MSBuild imports and property functions are not constrained by this logical boundary. |
| Command injection | Tool contracts expose typed inspection options only; child commands use `ArgumentList`, not a shell; no target or arbitrary executable is client-selectable. | Repository-controlled MSBuild evaluation can invoke capabilities available to MSBuild itself. |
| Secret inheritance | The MCP process removes credential-like names and credential-handle variables; `dotnet` children apply the existing filter again and disable telemetry/node reuse. | Name filtering is not DLP; unusually named secrets, readable files, OS credential stores, and network identities may remain accessible. |
| Secret disclosure | Expected errors are static and sanitized; raw exception/process output is not returned; canonical serialization redacts sensitive diagnostic-context keys; Git remote credentials are sanitized. | Repository metadata intentionally returned by the contract remains visible to the client. |
| Resource exhaustion | 1,024-character relative paths, 256 exclusions, 256 overrides, 128-character override values, 1 MiB configuration files, and 8 MiB MCP results. Cancellation remains supported. | Inspection still evaluates every discovered project before an output limit can be enforced. Performance/time limits belong to reliability hardening. |
| Repository mutation | All tools advertise read-only/non-destructive annotations and call inspection APIs only. No write, shell, restore, build, persistence, or upload tool exists. | MSBuild evaluation is not guaranteed side-effect-free for hostile custom logic. OS isolation is required for untrusted repositories. |
| Protocol/log injection | stdout is owned by stdio transport; host logs use stderr; child stdin is closed; argument values and raw exceptions are not logged. | A compromised dependency or runtime could violate process-level assumptions. |

## Link And TOCTOU Policy

An explicitly supplied root may itself be a symbolic link or junction. The server resolves every existing linked segment in that root path and stores the final absolute directory as the trust anchor. Below that anchor, tool-controlled paths must not cross symbolic links, junctions, or other reparse points, even when their targets remain inside the repository. This conservative rule is consistent across platforms; unsupported link creation is skipped only by platform-conditional tests.

The validation is a check, not a filesystem transaction. Another process with write access can replace a checked component before Engine access. Preventing that race requires OS-specific handle-relative traversal or a sandbox and is not provided. Run the server against a repository that untrusted actors cannot mutate concurrently.

MSBuild may resolve imports, SDKs, project references, property functions, and external tools outside the logical root. Those paths are not client tool arguments and cannot be reliably confined by lexical validation. Treat repositories as trusted, or inspect them in an ephemeral, non-privileged environment without secrets and with restricted filesystem/network access.

## Plan And Tasks

1. Canonicalize the startup root and reject links below it for every path-bearing tool input, including the default configuration path.
2. Enforce bounded tool inputs, configuration files, and results before crossing the MCP response boundary.
3. Strip sensitive environment names at MCP startup and retain the existing `dotnet` child-process hardening.
4. Canonicalize/redact reports before granular projections and verify that expected failures never expose exception details.
5. Prove read-only annotations, traversal rejection, link policy, limits, redaction, cancellation, stdout isolation, and CLI/Engine regression behavior.

## Validation Matrix

Automated tests cover required/missing/linked roots, valid normalized paths, traversal and absolute paths, linked explicit/default configuration files, linked directories, oversized collections/configuration/results, sensitive environment-name classification, diagnostic redaction, closed schemas, read-only annotations, protocol cancellation, stderr/stdout separation, and clean shutdown. Link tests execute when the host OS permits symbolic-link creation.

## Non-goals

This hardening does not provide a filesystem sandbox, network sandbox, process sandbox, malware boundary, authorization layer, or guarantee that evaluating a hostile MSBuild graph is side-effect-free. HTTP transport and write tools remain out of scope.
