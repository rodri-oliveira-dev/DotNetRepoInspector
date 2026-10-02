# Integration Discovery threat model

**Languages:** English | [Português (Brasil)](../../pt-BR/architecture/integration-discovery-threat-model.md)

This model covers the opt-in C# syntax-analysis boundary described by [ADR 0018](../decisions/0018-integration-discovery-boundary.md). The inspected repository, every eligible path, and every source byte are untrusted. The assets to protect are host availability and memory, repository-boundary integrity, deterministic output, and the confidentiality of source, credentials, queries, and payloads.

| Threat | Control | Residual risk |
| --- | --- | --- |
| Huge files or many files | Independent path, file, byte, finding, diagnostic, and duration budgets; pre-read size checks | Work up to the configured budgets is intentional |
| Pathological syntax trees | Syntax-only parsing, elapsed-time budget, cancellation, per-detector isolation | Roslyn still consumes CPU/memory within process limits |
| Secret-shaped or malicious strings | Allow-listed evidence projection, safe-resource normalization, no source snippets or arbitrary literal serialization | A logical identifier chosen by an application author can itself be sensitive; operators must keep identifiers non-secret |
| Traversal, reparse points, and symlinks | Canonical repository-relative paths and existing link/reparse-point rejection policy | Filesystem race resistance depends on the host and should be reinforced with OS isolation for hostile repositories |
| Generated and build output | Generated-file markers and build-directory exclusions | Unconventionally named generated code can be considered eligible until another budget or exclusion applies |
| Cancellation or detector failure | Cancellation is propagated; detector failures become bounded controlled diagnostics and do not expose exception text | Abrupt host termination can prevent a report from being produced |
| Finding-budget exhaustion | Deterministic traversal and global finding limit with explicit truncation metadata | Findings after the deterministic cutoff are intentionally omitted |

Integration Discovery does not execute, compile, or load target code and does not perform network discovery. It is not an operating-system sandbox. MSBuild project evaluation has a separate, broader trust boundary; hostile repositories should still be inspected with a disposable, least-privilege, network-restricted identity as described in the [security guide](../security.md).
