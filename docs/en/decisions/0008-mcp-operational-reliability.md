# ADR 0008: Bound MCP inspection concurrency and operational telemetry

**Languages:** English | [Portuguese (Brazil)](../../pt-BR/decisions/0008-mcp-operational-reliability.md)

- **Status:** Accepted
- **Date:** 2026-09-19
- **Decision owners:** DotNetRepoInspector maintainers

## Context

Every MCP tool derives its answer from a fresh `IRepositoryInspector` call. Concurrent calls for the server's single repository root could multiply Git and MSBuild child processes, contend for the same files, and produce observations from different instants. A session cache would reduce repeated work, but the repository, configuration, imports, SDK selection, and Git state can all change without a reliable invalidation signal.

The stdio protocol also requires stdout isolation, while operators need enough information to correlate slow, failed, timed-out, and cancelled calls without exposing repository paths or tool inputs.

## Decision

1. One inspection may execute at a time for the immutable server root. Up to eight additional calls may wait; later calls fail with `server_busy`.
2. Queue waits and Engine execution share the MCP request cancellation token and a five-minute server timeout. Client cancellation propagates; server timeout returns `inspection_timed_out`.
3. Child-process cancellation and process-tree termination remain owned by the existing Git/MSBuild adapters.
4. Each accepted or rejected call emits one structured JSON completion event on stderr with `Tool`, `DurationMs`, `Status`, and an opaque `CorrelationId`. Paths, arguments, exception text, and diagnostic contents are not logged.
5. No session cache is introduced. Fresh inspection is preferred until a bounded cache demonstrates a material gain and has repository-state-aware invalidation.
6. Reproducible MCP measurements compare process launch, startup plus handshake, CLI, Engine, and every v1 tool against the controlled `ProjectKinds` fixture. Versioned limits are regression guards, not an SLA.

## Consequences

The host cannot create an unbounded set of simultaneous MSBuild evaluations for one root. A long inspection introduces head-of-line blocking, but waiting calls remain cancellable and the bounded queue protects process and memory use. Tool calls can still observe repository mutations between sequential inspections; no snapshot or filesystem lock is implied.

The five-minute timeout is intentionally above current controlled baselines. Large repositories that legitimately need longer remain better served by direct Engine/CLI use until configurable MCP policy is justified. MSBuild is still not a sandbox.

## Alternatives considered

- **Unbounded parallel calls:** rejected because it can multiply child processes and reduce determinism.
- **Return one cached report for the whole session:** rejected because invalidation cannot reliably cover imported files, SDK state, Git state, and concurrent repository changes.
- **Log repository paths and exception objects:** rejected because operational correlation does not require sensitive context.
- **Apply the timeout to Engine and CLI globally:** rejected because repository sizes vary and this decision concerns the local MCP host only.

## References

- [MCP server](../architecture/mcp-server.md)
- [Performance](../performance.md)
- [ADR 0007](0007-mcp-trust-boundary-hardening.md)
- Issue #131: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/131
