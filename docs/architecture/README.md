# Architecture

Current boundaries:

- **Core** — normalized inspection model and classification concepts. It has no dependency on Git, MSBuild, CI providers, or persistence.
- **MSBuild** — project discovery and evaluated MSBuild metadata extraction.
- **Git** — repository-state adapter that discovers the Git work tree and collects normalized repository metadata through the `git` executable.
- **Engine** — application-level inspection orchestration. It composes Git metadata, SDK inspection, project discovery, evaluated MSBuild facts, classification, references, and diagnostics into a stable `InspectionReport`.
- **CLI** — command-line composition, serialization, and exit semantics.
- **Integrations** — future adapters such as GitHub Actions, persistence sinks, and policy/reporting layers.

The dependency direction is intentionally one-way:

```text
Delivery / integrations
        ↓
      Engine
   ↙    ↓    ↘
 Git  MSBuild  Core
  ↘     ↓     ↙
       Core
```

`Core` remains infrastructure-agnostic. `Engine` may depend on infrastructure adapters in order to orchestrate an inspection, but it does not know about command-line concerns, GitHub Actions, persistence, or any other delivery mechanism.

The end-to-end inspection behavior, including partial versus fatal failure semantics, is documented in [`../inspection-engine.md`](../inspection-engine.md).
