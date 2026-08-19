# Documentation

**Languages:** English | [Português (Brasil)](../pt-BR/README.md)

Keep the root [`README.md`](../../README.md) as the project entry point and place deeper design material here.

- [`architecture/`](architecture/) — current architecture, contracts, and implementation design.
- [`classification.md`](classification.md) — deterministic project classification rules and precedence.
- [`diagnostics.md`](diagnostics.md) — stable diagnostic catalog and operational logging rules.
- [`inspection-engine.md`](inspection-engine.md) — end-to-end inspection orchestration, failure semantics, determinism, and cancellation.
- [`cli.md`](cli.md) — command-line usage, output streams, exit codes, and cancellation behavior.
- [`compatibility.md`](compatibility.md) — supported .NET SDK/TFM and operating-system compatibility matrix.
- [`performance.md`](performance.md) — synthetic large-repository baseline, measured hotspots, and regression guardrails.
- [`real-repository-validation.md`](real-repository-validation.md) — pinned public-repository validation harness and bug-reproduction policy.
- [`project-reference-graph.md`](project-reference-graph.md) — normalized `ProjectReference` graph semantics.
- [`schema/inspection-v1.md`](schema/inspection-v1.md) — public JSON contract and compatibility policy.
- [`decisions/`](decisions/) — Architecture Decision Records for durable technical decisions.

Documentation should distinguish current behavior from planned behavior while the project is in early development.
