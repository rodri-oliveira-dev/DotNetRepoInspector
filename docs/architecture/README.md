# Architecture

Detailed architecture documentation will live here as the implementation is introduced.

Current boundaries:

- **Core** — normalized inspection model and classification concepts. It has no dependency on Git, MSBuild, CI providers, or persistence.
- **MSBuild** — project discovery and evaluated MSBuild metadata extraction.
- **Git** — repository-state adapter that discovers the Git work tree and collects normalized repository metadata through the `git` executable.
- **CLI** — command-line composition, serialization, and exit semantics.
- **Integrations** — future adapters such as GitHub Actions, persistence sinks, and policy/reporting layers.

Architecture should preserve a reusable inspection engine that is not coupled to a specific CI provider or database. Infrastructure adapters may depend on `Core`; `Core` must not depend on them.
