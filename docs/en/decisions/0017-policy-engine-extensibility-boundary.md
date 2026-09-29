# ADR 0017: Define the policy engine extensibility boundary

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0017-policy-engine-extensibility-boundary.md)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Decision owners:** DotNetRepoInspector maintainers

## Context

DotNetRepoInspector needs opt-in governance rules without turning inspection into policy enforcement or duplicating rule semantics across the CLI, GitHub Action, MCP, persistence, or future delivery adapters.

The inspection engine already produces normalized Core contracts after Git and MSBuild evidence has been collected. Policy evaluation therefore needs a stable extensibility boundary that consumes those normalized facts, preserves deterministic behavior, and keeps policy findings distinct from inspection diagnostics.

The first concrete rule is the TargetFramework policy from issue #157, but the boundary must support additional rules without coupling Core to MSBuild or a delivery mechanism.

## Decision

Policy extensibility is owned by `DotNetRepoInspector.Core.Policies`.

### Rule ownership

- `IPolicyRule` is the public rule contract.
- Rules consume only `PolicyEvaluationContext`, which is built from normalized Core inspection contracts.
- Rules must not depend on MSBuild, Git, CLI, GitHub Actions, MCP transports, persistence adapters, or repository source-file parsing.
- A rule owns its stable `DRPxxxx` code and emits structured rule findings; it does not decide process exit codes or delivery behavior.

### Registration and configuration

- The Engine/configuration layer owns translating validated repository configuration into explicit rule instances.
- Rule registration is explicit. The product does not discover rules through reflection, assembly scanning, static global registries, or delivery-adapter conventions.
- No configuration means no registered policies. Inspection remains zero-configuration and policy-free by default.
- Configuration schema versioning is independent from inspection-report schema versioning.

### Evaluation lifecycle

- `RepositoryInspector` completes normal inspection first and creates the normalized `InspectionReport`.
- If no rules are registered, the report is returned without policy evaluation.
- If rules are registered, `PolicyEngine` evaluates them after inspection using normalized facts only.
- Policy evaluation does not mutate projects, diagnostics, classification, SDK facts, references, or other inspection evidence.

### Ordering and determinism

- `PolicyEngine` rejects duplicate rule codes.
- Rules are evaluated in explicit registration order.
- Findings produced by each rule preserve that rule's deterministic evaluation order.
- The public serializer canonicalizes `policyFindings` so equivalent facts produce stable JSON regardless of incidental collection ordering.

### Findings and diagnostics

- Policy results are represented by top-level `policyFindings`.
- Inspection failures and recoverable inspection problems remain `InspectionDiagnostic` values under `diagnostics` or `projects[].diagnostics`.
- A policy violation must never be converted into an inspection diagnostic merely to influence an exit code.
- Delivery adapters may derive execution status from findings. The CLI and GitHub Action return exit code `1` for an `error` policy finding, while a policy `warning` does not fail an otherwise healthy inspection.

### Adapter responsibilities

- The CLI serializes the canonical report and applies documented exit semantics; it does not implement rule logic.
- The GitHub Action forwards the existing configuration contract to the CLI and reuses the same report and exit code; it does not add a second policy parser.
- MCP, persistence, containers, and future adapters consume the same normalized report and must not redefine policy semantics.

## Alternatives considered

### Implement policies in the CLI or GitHub Action

Rejected. This would create multiple policy engines, make behavior delivery-specific, and allow local CLI and CI results to diverge.

### Let rules consume raw MSBuild types or project files

Rejected. Rules would become coupled to infrastructure and collection details instead of the stable normalized facts exposed by Core.

### Mix policy violations into inspection diagnostics

Rejected. A governance decision is semantically different from an inspection problem. Mixing them would make consumers unable to distinguish repository health from policy compliance.

### Discover rules dynamically through reflection or a global registry

Rejected for the current product boundary. Explicit construction from validated configuration is easier to reason about, test, order deterministically, and secure.

## Consequences

### Positive

- Inspection remains deterministic and useful without policies.
- New rules can extend governance without changing MSBuild collection or delivery adapters.
- Policy compliance and inspection health remain independently machine-readable.
- CLI and GitHub Action behavior stay aligned because both reuse the same Engine and report.
- The dependency direction remains Core-first and delivery-agnostic.

### Trade-offs

- Adding a configurable rule requires explicit Engine registration/configuration mapping in addition to the Core rule implementation.
- The current boundary favors built-in rules over runtime plugin discovery.
- Consumers that care about policy intent must inspect configuration as well as findings because an empty `policyFindings` collection can mean either no enabled policy produced a finding or no policy was enabled.

## References

- Issue #28: policy engine core
- Issue #157: TargetFramework policy and configuration
- Issue #158: CLI and inspection-contract integration
- Issue #159: GitHub Action integration and E2E
- Inspection schema: [`../schema/inspection-v1.md`](../schema/inspection-v1.md)
- Configuration: [`../configuration.md`](../configuration.md)
- GitHub Action: [`../github-action.md`](../github-action.md)
