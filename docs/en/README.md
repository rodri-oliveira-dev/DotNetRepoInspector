# Documentation

**Languages:** English | [Português (Brasil)](../pt-BR/README.md)

Keep the root [`README.md`](../../README.md) as the project entry point and place deeper design material here.

- [`architecture/`](architecture/) — current architecture, contracts, and implementation design.
- [`classification.md`](classification.md) — deterministic project classification rules and precedence.
- [`configuration.md`](configuration.md) — optional repository configuration, exclusions, classification overrides, and precedence.
- [`diagnostics.md`](diagnostics.md) — stable diagnostic catalog and operational logging rules.
- [`security.md`](security.md) — data collection boundary, MSBuild trust model, secret handling, Action permissions, and sink credential guidance.
- [`persistence.md`](persistence.md) — optional snapshot persistence, sink extension contract, timeout, failure mode, and retry boundary.
- [`snapshot-provenance.md`](snapshot-provenance.md) — evidence identity, UTC provenance, report digest, and idempotency semantics.
- [`inspection-engine.md`](inspection-engine.md) — end-to-end inspection orchestration, failure semantics, determinism, and cancellation.
- [`cli.md`](cli.md) — command-line usage, output streams, exit codes, and cancellation behavior.
- [`github-action.md`](github-action.md) — reusable GitHub Action inputs, outputs, runtime bootstrap, permissions, and CI validation.
- [`container.md`](container.md) — official GHCR/Docker Hub distribution, tag/digest policy, SBOM/provenance evidence, local builds, hardened mounts, non-root execution, and offline usage.
- [`releases.md`](releases.md) — Semantic Versioning, schema compatibility, protected publication, release artifacts, tags, and provenance.
- [`v1-release-readiness.md`](v1-release-readiness.md) — v1.0.0 public baseline, automated readiness gate, first-publication checklist, and post-release verification.
- [`compatibility.md`](compatibility.md) — supported .NET SDK/TFM and operating-system compatibility matrix.
- [`mcp.md`](mcp.md) — MCP installation, stdio startup, client setup, tool catalog, security, examples, and troubleshooting.
- [`mcp-agent-compatibility.md`](mcp-agent-compatibility.md) — MCP client compatibility matrix, smoke tests, and deterministic evals.
- [`mcp-release-candidate.md`](mcp-release-candidate.md) — MCP release-candidate gates, findings, evidence, and GA blockers.
- [`mcp-ga-readiness.md`](mcp-ga-readiness.md) — frozen MCP v1 contract, planned stable version, final gates, and publication status.
- [`mcp-1.2.0-release-notes.md`](mcp-1.2.0-release-notes.md) — draft release notes for the planned first stable MCP package.
- [`performance.md`](performance.md) — synthetic large-repository baseline, measured hotspots, and regression guardrails.
- [`real-repository-validation.md`](real-repository-validation.md) — pinned public-repository validation harness and bug-reproduction policy.
- [`project-reference-graph.md`](project-reference-graph.md) — normalized `ProjectReference` graph semantics.
- [`schema/inspection-v1.md`](schema/inspection-v1.md) — public JSON contract and compatibility policy.
- [`decisions/`](decisions/) — Architecture Decision Records for durable technical decisions.

Documentation distinguishes the public v1 compatibility surface from explicitly post-v1 evolution work.
