# Optional snapshot persistence

**Languages:** English | [Português (Brasil)](../pt-BR/persistence.md)

DotNetRepoInspector treats persistence as an optional post-inspection integration. A repository can always be inspected without a database, HTTP endpoint, cloud account, or CI-specific service.

The normalized `InspectionReport` remains owned by `DotNetRepoInspector.Core`. Persistence does not add transport failures, credentials, retry state, or destination-specific fields to that report.

## Boundary

The flow is intentionally sequential:

```text
Repository
    |
    v
Inspection Engine ----> InspectionReport
                           |
                           | optional
                           v
                 InspectionSnapshotPublisher
                           |
                           v
                  IInspectionSnapshotSink
                           |
                           v
                 consumer-owned destination
```

`DotNetRepoInspector.Persistence` depends only on `DotNetRepoInspector.Core`. `Core` and `Engine` do not depend on persistence.

## Extension contract

Third-party destinations implement `IInspectionSnapshotSink`:

```csharp
public interface IInspectionSnapshotSink
{
    string Name { get; }

    Task<InspectionSinkWriteResult> WriteAsync(
        InspectionSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
```

A sink must:

- use a stable, non-secret `Name`;
- honor cancellation;
- return operational failures through `InspectionSinkWriteResult.Failed(...)`;
- classify a failure as transient only when the adapter can do so reliably;
- never place credentials, authorization headers, connection strings, raw exception dumps, or secret-bearing response bodies in failure messages;
- avoid modifying the inspection report.

Unexpected exceptions crossing the extension boundary are converted by `InspectionSnapshotPublisher` into the generic `unexpected-sink-failure` result without exposing exception text.

## Opt-in behavior

No sink is created or invoked by the inspection engine. A host explicitly chooses a sink and calls `InspectionSnapshotPublisher` after inspection succeeds far enough to produce an `InspectionReport`.

Generic persistence options are independent from sink-specific credentials:

- `Timeout`: 15 seconds by default;
- `FailureMode`: `NonFatal` by default, or `Fatal` when persistence is required for the pipeline.

`InspectionPersistenceResult.ShouldFailExecution` tells the delivery layer whether a failed persistence attempt should make the overall command/job fail. The inspection report itself is unchanged in both modes.

Persistence failures are not `DRI` inspection diagnostics because they describe delivery of an already-produced report, not inspection of the repository.

## Snapshot provenance and idempotency

Before publishing, the host creates an `InspectionSnapshot` through `InspectionSnapshotFactory`. The envelope includes Inspector version, canonical repository identity, commit/ref, UTC observation time, optional generic execution metadata, a normalized report digest, and a versioned idempotency key.

Two scopes are explicit:

- `RepositoryState` for clean commits with canonical remote identity, allowing equivalent re-runs to share a key;
- `Observation` for dirty or otherwise ambiguous repository states, avoiding accidental deduplication.

See [`snapshot-provenance.md`](snapshot-provenance.md) and ADR 0004 for the complete contract.

## Retry and idempotency

The generic publisher does not retry. A generic retry policy cannot know whether a destination failure is transient or whether replaying the request is safe.

Concrete sinks may retry only when all of the following are true:

1. the adapter can classify the failure as transient;
2. retry count and backoff are bounded;
3. the overall timeout and caller cancellation are respected;
4. replay uses the snapshot idempotency key and is safe for the destination semantics.

The first HTTP sink in issue #22 will consume this contract rather than inventing destination-specific snapshot identity.

## Configuration and secrets

Persistence configuration is a delivery concern, not repository-inspection configuration. The repository's `.dotnetrepoinspector.json` must not contain sink credentials.

A future built-in sink may expose non-secret selection/policy options through CLI or GitHub Action inputs. Tokens, connection strings, API keys, and similar values must come from environment/secret facilities appropriate to the host and must never be copied into `InspectionReport`, `InspectionSnapshot`, diagnostic context, or normal logs.

See [`security.md`](security.md) for the project-wide secret-handling rules.

## First concrete sink

ADR 0003 selects an HTTP/webhook adapter as the first built-in sink because it keeps the Inspector independent from database engines and cloud providers while working naturally in local automation and CI/CD.

The HTTP adapter itself is intentionally not implemented here. Issue #22 owns that implementation and will use the identity/idempotency contract defined by ADR 0004.

## Related decisions

- [ADR 0003: Keep snapshot persistence optional behind sink adapters](decisions/0003-persistence-sink-architecture.md)
- [ADR 0004: Define snapshot provenance and idempotency from canonical evidence](decisions/0004-snapshot-provenance-idempotency.md)
- [Snapshot provenance and idempotency](snapshot-provenance.md)
- [Inspection schema](schema/inspection-v1.md)
- [Security and privacy](security.md)
