# ADR 0004: Define snapshot provenance and idempotency from canonical evidence

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0004-snapshot-provenance-idempotency.md)

- **Status:** Accepted
- **Date:** 2026-08-20
- **Decision owners:** DotNetRepoInspector maintainers

## Context

Persisted inspection evidence must be attributable to the repository state, Inspector version, and execution that produced it. Re-running the same clean commit should not create accidental duplicates, while ambiguous states such as dirty worktrees must not be collapsed as though a commit SHA fully described them.

The model must remain provider-neutral and must not depend on GitHub Actions identifiers.

## Decision

`InspectionSnapshot` is the evidence envelope passed to persistence sinks. It carries:

- inspection `schemaVersion`;
- `inspectorVersion`;
- canonical `repositoryIdentity`;
- `commitSha` when available;
- `ref` when available;
- `observedAtUtc`;
- optional generic execution metadata (`id`, `provider`, `ref`);
- SHA-256 of the normalized, redacted inspection report;
- a versioned idempotency key;
- the idempotency scope;
- the original `InspectionReport`.

`InspectionSnapshotFactory` is the single provider-neutral component responsible for producing this envelope.

### Repository identity

When a recognized Git remote is available, repository identity is normalized to `host/path` and does not retain transport/user information or a trailing `.git`. Equivalent HTTPS and SCP-like SSH remotes therefore resolve to the same identity.

If no canonical remote can be derived, the repository name is retained as `name:<name>` only as descriptive provenance. That fallback is not considered strong enough for repository-state idempotency.

### Report digest

`reportSha256` is SHA-256 over `InspectionJsonSerializer.Serialize(report)`. This reuses the existing deterministic ordering and sensitive-context redaction before hashing, so secrets are not incorporated into the digest.

### Repository-state idempotency

A snapshot uses `RepositoryState` scope only when all are true:

1. a canonical remote identity exists;
2. a commit SHA exists;
3. `repository.isDirty` is explicitly `false`.

The idempotency key is `dri1:<sha256>` over a versioned canonical input containing repository identity, commit, Inspector version, inspection schema version, and a canonical report digest.

For this scope, mutable aliases and execution observations do not affect identity:

- branch/ref is excluded from the canonical digest;
- HTTPS versus SSH clone transport is normalized;
- CI execution ID and timestamp are excluded.

Therefore a re-run of the same clean evidence can be upserted safely under the same key. A different Inspector version, inspection schema, commit, repository, or materially different evaluated report produces a different key.

### Observation idempotency

A snapshot uses `Observation` scope when repository-state identity is not strong enough, including:

- dirty worktree;
- missing commit SHA;
- missing/unrecognized canonical remote identity.

Observation keys include the normalized report digest plus an execution discriminator. When the consumer supplies an execution ID, `provider + execution id` is used so retries inside the same CI execution remain stable. Otherwise `observedAtUtc` is used.

Observation scope deliberately does not claim that two executions describe the same repository state merely because they share a commit SHA.

### Execution provenance

Execution metadata is generic:

```text
id       optional execution/run identifier
provider optional producer name such as github-actions, gitlab-ci, azure-pipelines, jenkins, local
ref      optional full source ref such as refs/heads/main or refs/tags/v1.2.3
```

The provider is normalized to lowercase. The explicit execution ref takes precedence over the branch captured by Git metadata for the snapshot `ref` field.

### UTC timestamps

`observedAtUtc` is obtained from `TimeProvider` and normalized to UTC before being stored. `InspectionSnapshotJsonSerializer` serializes the evidence envelope with `System.Text.Json` using the stable camelCase contract.

### Retention

Retention, history compaction, and deletion remain responsibilities of the sink/consumer. The Inspector only defines evidence identity and replay semantics.

## Alternatives considered

### Commit SHA only

Rejected. The same commit can produce different evidence under different Inspector/schema versions or evaluated SDK environments, and a dirty worktree is not fully represented by HEAD.

### Branch/ref as identity

Rejected. Branches and tags are mutable aliases and cannot be the primary identity of historical evidence.

### Timestamp as the universal unique key

Rejected. It prevents useful deduplication of clean re-runs and turns harmless retries into duplicate evidence.

### CI run ID as the primary key

Rejected. It couples the model to a producer and makes local/non-CI usage second-class.

### Hash raw process output or unredacted state

Rejected. Raw MSBuild/Git output is not the normalized public contract and may contain unstable or sensitive material.

## Consequences

### Positive

- clean re-runs can be deduplicated deterministically;
- ambiguous/dirty states are not incorrectly collapsed;
- snapshot identity is independent of GitHub Actions;
- equivalent Git clone transports share repository identity;
- report hashes inherit deterministic normalization and redaction;
- sinks receive explicit metadata suitable for indexes, upserts, and audit trails.

### Trade-offs

- repository-state idempotency requires a canonical remote plus clean commit;
- local repositories without a remote fall back to observation identity;
- an observation key cannot prove content identity for arbitrary dirty files not represented by the report;
- sinks still choose their own storage/index/retention implementation.

## Security

No credential or authentication material is part of provenance or the idempotency key. Remote normalization uses only canonical host/path identity, and report hashing happens after the inspection serializer has redacted sensitive diagnostic context.

## Follow-up work

- **#22** — use `InspectionSnapshotJsonSerializer` and `IdempotencyKey` in the first HTTP/webhook sink, including bounded transient retry and destination-specific upsert/replay semantics.

## References

- Persistence architecture: [ADR 0003](0003-persistence-sink-architecture.md)
- Provenance contract: [`../snapshot-provenance.md`](../snapshot-provenance.md)
- Inspection schema: [`../schema/inspection-v1.md`](../schema/inspection-v1.md)
- Security model: [`../security.md`](../security.md)
