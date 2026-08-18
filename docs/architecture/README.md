# Architecture

Detailed architecture documentation will live here as the implementation is introduced.

Initial boundaries:

- **Core** — normalized inspection model and classification concepts.
- **MSBuild** — project discovery and evaluated MSBuild metadata extraction.
- **CLI** — command-line composition, serialization, and exit semantics.
- **Integrations** — future adapters such as GitHub Actions, persistence sinks, and policy/reporting layers.

Architecture should preserve a reusable inspection engine that is not coupled to a specific CI provider or database.
