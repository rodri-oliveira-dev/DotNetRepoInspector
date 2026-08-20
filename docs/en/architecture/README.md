# Architecture

**Languages:** English | [Português (Brasil)](../../pt-BR/architecture/README.md)

Current boundaries:

- **Core** — normalized inspection model and classification concepts. It has no dependency on Git, MSBuild, CI providers, or persistence.
- **MSBuild** — project discovery and evaluated MSBuild metadata extraction.
- **Git** — repository-state adapter that discovers the Git work tree and collects normalized repository metadata through the `git` executable.
- **Engine** — application-level inspection orchestration. It composes Git metadata, SDK inspection, project discovery, evaluated MSBuild facts, classification, references, and diagnostics into a stable `InspectionReport`.
- **Persistence** — optional post-inspection sink contract and provider-neutral publication policy. It depends only on Core and is never invoked by Engine implicitly.
- **CLI** — command-line composition, serialization, and exit semantics.
- **Integrations** — delivery adapters such as GitHub Actions plus future concrete persistence sinks and policy/reporting layers.

The dependency direction is intentionally one-way:

```text
                 Delivery / integrations
                    |             |
                    v             v
                  Engine      Persistence
               /    |    \          |
             Git  MSBuild  Core <----+
              \     |     /
                  Core
```

`Core` remains infrastructure-agnostic. `Engine` may depend on infrastructure adapters required to inspect a repository, but it does not know about command-line concerns, GitHub Actions, persistence, or any other delivery mechanism.

Persistence is composed by a host only after an `InspectionReport` exists. Therefore transport outages, credentials, retry state, and sink-specific failures never become inspection facts.

The end-to-end inspection behavior, including partial versus fatal inspection failure semantics, is documented in [`../inspection-engine.md`](../inspection-engine.md). Optional persistence is documented separately in [`../persistence.md`](../persistence.md) and [ADR 0003](../decisions/0003-persistence-sink-architecture.md).
