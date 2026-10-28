# ADR 0019: Keep deployment evidence factual, opt-in, and adapter-independent

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0019-deployment-evidence-boundary.md)

- **Status:** Accepted
- **Date:** 2026-10-27
- **Decision owners:** DotNetRepoInspector maintainers

## Context

SDK and target-framework metadata alone cannot describe the runtime evidence relevant to a fleet inventory. Evaluated publication settings and Dockerfile base-image references provide useful additional facts, but neither proves how an application was published or what is deployed. Dockerfile analysis also introduces a file-reading trust boundary that must not affect default inspection cost or couple Core to a Docker implementation.

ADR 0017 separates evidence from policy decisions, and ADR 0018 defines an opt-in boundary for C# Integration Discovery. Deployment evidence needs the same durable separation while retaining MSBuild as the source of truth for publication properties.

## Decision

### Publication facts

- `DotNetRepoInspector.MSBuild` evaluates the allow-listed `SelfContained`, `PublishSelfContained`, `PublishAot`, `PublishTrimmed`, and `RuntimeFrameworkVersion` properties through the existing MSBuild evaluation process.
- Boolean and string facts retain `evaluated`, `unspecified`, `unknown`, and `context-dependent` states. Values are emitted only for `evaluated`.
- Multi-target evaluation preserves a project-level value only when target-framework contexts agree. Conflicts are represented as `context-dependent`, not resolved by precedence or inference.
- Core owns only normalized publication contracts and has no dependency on MSBuild types or execution.

### Container evidence

- `DotNetRepoInspector.ContainerDiscovery` owns bounded Dockerfile enumeration and parsing. It has no dependency on Core, Docker clients, registry clients, or provider SDKs.
- The Engine invokes this pipeline only after explicit CLI/request or repository-configuration opt-in. With no opt-in, the pipeline is not called and no Dockerfile path is enumerated.
- The supported grammar is deliberately small: eligible Dockerfile names, comments, continuations, global `ARG` defaults, `FROM`, `--platform`, aliases, earlier-stage references, `scratch`, tags, and SHA-256 digests.
- Unsupported or dynamic input produces unresolved evidence without retaining the expression. Secret-shaped argument defaults are never substituted into output.
- Discovery is read-only, follows no filesystem links, stays below the repository root, observes cancellation, and enforces path/file/byte/depth/result/diagnostic/time ceilings. Partial results have explicit truncation state.
- The pipeline never invokes Docker or a shell, executes repository code, reads process environment values for interpolation, contacts registries, or resolves remote digests.

### Contracts and policy

- Core owns normalized `ProjectPublicationMetadata`, `ContainerImageEvidence`, and execution metadata only. These contracts contain no parser, Docker, MSBuild, GitHub, registry, or vulnerability-scanner types.
- Schema `1.7` is additive. Older compatible `1.x` payloads may omit the new fields; deserialization supplies conservative defaults.
- Evidence is distinct from `policyFindings` and diagnostics. The Inspector does not ingest CVEs, decide patch applicability, infer runtime exposure, or claim vulnerability.
- Delivery adapters reuse the Engine/request/configuration boundary. They do not implement their own parser or security policy.

## Alternatives considered

### Always scan Dockerfiles

Rejected because it changes default filesystem access, cost, and trust assumptions for every inspection.

### Use Docker or a registry API to resolve images

Rejected because it introduces daemon/network dependencies, mutable external state, credentials, and a false impression that a referenced image is deployed.

### Put Dockerfile parsing or MSBuild types in Core

Rejected because Core is the normalized contract and policy boundary. Infrastructure dependencies would reverse the intended dependency direction and constrain other adapters.

### Infer vulnerability status from versions or tags

Rejected because repository evidence alone cannot establish deployed bytes, patch applicability, exploitability, or authoritative security status. That correlation belongs to a downstream system with external release metadata and deployment provenance.

## Consequences

### Positive

- Default inspection behavior and Dockerfile cost remain unchanged.
- Downstream inventories receive deterministic, source-attributed evidence without an embedded CVE policy.
- Unknown and context-dependent facts remain explicit.
- Docker, registry, GitHub, and MSBuild implementation details stay outside Core.

### Trade-offs

- Dockerfile support is intentionally incomplete and may emit unresolved evidence for valid advanced syntax.
- Image tags and declared digests are references, not proof of registry content or deployment.
- Project-level publication aggregation does not expose a separate public record for every target-framework context; disagreement is conservatively represented as `context-dependent`.
- The central `.github` inventory workflow needs a separate follow-up to consume and correlate schema `1.7` evidence.

## References

- Issue #218
- [ADR 0017 — policy engine extensibility boundary](0017-policy-engine-extensibility-boundary.md)
- [ADR 0018 — Integration Discovery boundary](0018-integration-discovery-boundary.md)
- [Deployment evidence](../deployment-evidence.md)
- [Inspection schema](../schema/inspection-v1.md)
