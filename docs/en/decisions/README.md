# Architecture Decision Records

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/README.md)

Record durable decisions here using sequential files such as `0001-msbuild-evaluation-strategy.md`.

An ADR should capture:

- context and problem;
- decision;
- alternatives considered;
- consequences/trade-offs;
- status and superseding ADR when applicable.

## Records

- [ADR 0001: Evaluate projects through `dotnet msbuild`](0001-msbuild-evaluation-strategy.md) — Accepted.
- [ADR 0002: Distribute the GitHub Action as a composite action over the .NET Tool](0002-github-action-distribution-strategy.md) — Accepted.
- [ADR 0003: Keep snapshot persistence optional behind sink adapters](0003-persistence-sink-architecture.md) — Accepted.

Likely next decisions include classification precedence and evidence identity/idempotency.
