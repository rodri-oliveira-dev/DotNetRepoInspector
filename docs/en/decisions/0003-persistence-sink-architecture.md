# ADR 0003: Keep snapshot persistence optional behind sink adapters

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0003-persistence-sink-architecture.md)

- **Status:** Accepted
- **Date:** 2026-08-20
- **Decision owners:** DotNetRepoInspector maintainers

## Context

DotNetRepoInspector produces a normalized `InspectionReport` that is useful both as immediate CI/CD output and as historical evidence. Consumers may want to retain snapshots to analyze .NET versions, application types, dependencies, or architecture changes over time.

The inspection path must nevertheless remain useful with no external infrastructure. `Core` must not know about databases, HTTP, cloud providers, CI platforms, credentials, retries, or retention policies.

The persistence design must also account for:

- use from the CLI, GitHub Actions, and other hosts;
- third-party destinations;
- schema evolution;
- timeouts and cancellation;
- configurable fatal versus non-fatal persistence failure;
- retry and idempotency boundaries;
- protection of credentials and other secrets.

## Decision

Persistence is an optional post-inspection integration implemented behind a sink abstraction outside `Core` and outside the inspection `Engine`.

A new `DotNetRepoInspector.Persistence` assembly contains the provider-neutral extension contract and publisher policy. It depends only on `DotNetRepoInspector.Core`.

The normal flow is:

```text
repository
   |
   v
Engine -> InspectionReport
              |
              | optional, explicit
              v
      InspectionSnapshotPublisher
              |
              v
       IInspectionSnapshotSink
              |
              v
       external destination
```

The `Engine` does not discover, configure, or invoke sinks. A delivery host invokes persistence only after it already has an `InspectionReport`.

### Extension contract

Third-party destinations implement `IInspectionSnapshotSink` and receive an `InspectionSnapshot` plus a `CancellationToken`.

The initial `InspectionSnapshot` envelope contains the normalized report. Issue #21 will extend the evidence envelope with stable identity/provenance metadata before the first network sink is shipped. The envelope exists now so the sink API can evolve by adding evidence metadata without coupling destination adapters to the inspection engine.

A sink returns `InspectionSinkWriteResult` for expected operational outcomes. Failures contain:

- a stable adapter-owned code;
- a safe human-readable message;
- an `IsTransient` classification when the adapter can determine it.

Sink messages must not contain credentials, authorization headers, connection strings, raw exception dumps, or secret-bearing response bodies.

Unexpected exceptions crossing the extension boundary are normalized by `InspectionSnapshotPublisher` into a generic `unexpected-sink-failure` result without copying exception text.

### Opt-in and failure semantics

Persistence is disabled by absence: if a host does not explicitly select/configure a sink, no persistence object is created and no external call is made.

`InspectionPersistenceOptions` defines provider-neutral execution policy:

- default timeout: 15 seconds;
- default failure mode: `NonFatal`;
- optional `Fatal` mode for pipelines where evidence persistence is mandatory.

A persistence failure does not mutate `InspectionReport` and does not create a `DRI` inspection diagnostic. `InspectionPersistenceResult.ShouldFailExecution` tells the delivery layer whether that separate persistence failure should fail the command/job.

Caller cancellation is propagated. Publisher timeout is represented as `persistence-timeout` and classified transient.

### Retry ownership

The generic publisher does **not** retry.

Retry belongs to a concrete sink because only that adapter can reliably know:

- which destination errors are transient;
- whether a request may have been accepted before a transport failure;
- which backoff/retry semantics the destination supports;
- whether replay is safe under the idempotency contract.

A concrete sink may retry only transient failures, with bounded attempts/backoff, while honoring the overall timeout and cancellation. Issue #21 defines identity and idempotency before issue #22 implements a network sink.

### Configuration and credentials

Persistence configuration is a delivery concern and is intentionally separate from `.dotnetrepoinspector.json`, which configures repository inspection/classification.

A concrete sink may expose non-secret settings through CLI/Action options, for example sink selection, endpoint identifier, timeout, or failure mode. Secrets must be supplied through host-appropriate secret facilities such as environment variables or GitHub Actions secrets.

Credentials never become fields of `InspectionReport` or `InspectionSnapshot`, and must not be copied into diagnostic context or normal logs.

### First built-in sink

The first concrete built-in sink will be an HTTP/webhook adapter, implemented by issue #22 after issue #21.

HTTP/webhook is selected because it:

- is usable from local automation and common CI/CD systems;
- delegates storage technology to a consumer-owned service;
- avoids coupling the Inspector to a database engine or cloud provider;
- is straightforward for third parties to emulate or replace;
- can carry the canonical versioned evidence payload.

The HTTP sink will not live in `Core` or `Engine`. It will be a separate adapter package/project over the persistence abstraction.

## Alternatives considered

### Direct configurable database persistence

**Rejected as the primary architecture.**

Advantages:

- direct writes can be convenient for a known internal environment;
- database-native upsert/idempotency primitives may be available.

Trade-offs:

- couples the Inspector to drivers, SQL dialects, migrations, pooling, connection strings, and database-specific failure semantics;
- expands the dependency and vulnerability surface;
- is awkward for consumers using different databases or cloud-native storage;
- makes secret handling more complex in a general-purpose CLI/Action.

A third-party sink may still implement direct database persistence.

### File/object as the only persistence mechanism

**Kept as interoperability, rejected as the only persistence architecture.**

The existing JSON output is already a useful artifact and can be uploaded by CI to object storage. It is the lowest-coupling option and remains supported.

However, file output alone does not provide a uniform extension boundary for remote evidence services, application databases, or custom retention systems. Therefore it complements rather than replaces sinks.

### Built-in HTTP/webhook only, without an interface

**Rejected.**

It would be simple initially but would make HTTP semantics part of the application boundary and force future destinations either into the CLI/Engine or into duplicated integration code.

### Plugin discovery/loading system

**Rejected for the initial version.**

Runtime plugin discovery would introduce assembly loading, trust, version compatibility, packaging, and security complexity that is not needed to make the contract extensible.

Third parties can reference `DotNetRepoInspector.Persistence` and implement `IInspectionSnapshotSink` in their own host/package. A dynamic plugin loader can be considered later if real demand justifies the added complexity.

## Consequences

### Positive

- inspection remains zero-infrastructure and deterministic;
- `Core` remains provider-agnostic;
- `Engine` remains responsible only for inspection;
- persistence failures cannot silently alter inspection facts;
- delivery layers can choose fatal or non-fatal persistence behavior;
- timeouts and cancellation have a shared provider-neutral contract;
- third parties have a small extension interface without needing GitHub-specific types;
- HTTP can be implemented without locking the project to a storage backend.

### Trade-offs

- delivery hosts must explicitly compose persistence after inspection;
- there is no generic retry because retry safety is destination-specific;
- provenance/idempotency requires the follow-up contract in issue #21;
- the first concrete sink remains unavailable until issue #22;
- dynamic runtime plugin discovery is intentionally not provided.

## Compatibility and versioning

The persisted evidence must preserve the inspection `schemaVersion`. Consumers must use the existing inspection schema compatibility rules when reading historical payloads.

Transport configuration and sink failures are not part of the inspection schema. Adding or changing a sink therefore must not require an inspection schema version bump unless the normalized evidence payload itself changes.

The `InspectionSnapshot` evidence envelope is the extension point for provenance metadata defined by issue #21.

## Security

This decision follows the project security model:

- persistence is opt-in;
- no sink credential belongs in the inspection JSON;
- sink-specific secret values must come from external secret facilities;
- failure messages are safe summaries, not raw remote payloads/exceptions;
- unexpected sink exceptions are normalized without exception text;
- untrusted repository evaluation and persistence credentials should not share a privileged environment unless the consumer has explicitly reviewed that trust boundary.

See [`../security.md`](../security.md).

## Follow-up work

- **#21** — define evidence identity, provenance, UTC timestamp, CI metadata, and idempotency key semantics.
- **#22** — implement the first HTTP/webhook sink with bounded transient retry, timeout/cancellation, secret-safe configuration, and idempotency support.

## References

- Inspection contract: [`../schema/inspection-v1.md`](../schema/inspection-v1.md)
- Persistence contract: [`../persistence.md`](../persistence.md)
- Security model: [`../security.md`](../security.md)
