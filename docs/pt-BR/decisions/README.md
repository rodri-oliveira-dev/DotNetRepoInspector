# Architecture Decision Records

**Idiomas:** [English](../../en/decisions/README.md) | Português (Brasil)

Registre aqui decisões duradouras usando arquivos sequenciais, como `0001-msbuild-evaluation-strategy.md`.

Um ADR deve registrar:

- contexto e problema;
- decisão;
- alternativas consideradas;
- consequências/trade-offs;
- status e ADR que o substitui, quando aplicável.

## Registros

- [ADR 0001: Avaliar projetos por meio de `dotnet msbuild`](0001-msbuild-evaluation-strategy.md) — Aceito.
- [ADR 0002: Distribuir a GitHub Action como Composite Action sobre a .NET Tool](0002-github-action-distribution-strategy.md) — Aceito.
- [ADR 0003: Manter persistência de snapshots opcional atrás de adapters de sink](0003-persistence-sink-architecture.md) — Aceito.
- [ADR 0004: Definir proveniência e idempotência de snapshots a partir da evidência canônica](0004-snapshot-provenance-idempotency.md) — Aceito.
- [ADR 0005: Definir o contrato de execução em container e compatibilidade de SDKs](0005-container-execution-contract.md) — Aceito.
- [ADR 0006: Definir a arquitetura do adapter MCP e o contrato do MVP](0006-mcp-adapter-architecture.md) — Aceito.
- [ADR 0007: Endurecer a fronteira de confiança do MCP local](0007-mcp-trust-boundary-hardening.md) — Aceito.
- [ADR 0008: Limitar a concorrência de inspeções MCP e a telemetria operacional](0008-mcp-operational-reliability.md) — Aceito.

Decisões futuras prováveis incluem precedência de classificação, fronteiras da avaliação de políticas e transporte MCP remoto caso ele se torne um cenário suportado.
