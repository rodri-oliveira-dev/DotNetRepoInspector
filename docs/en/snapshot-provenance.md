# Snapshot provenance and idempotency

**Languages:** English | [Português (Brasil)](../pt-BR/snapshot-provenance.md)

Persisted evidence is represented by `InspectionSnapshot`. The snapshot wraps the normalized inspection report with provider-neutral provenance and a deterministic idempotency key.

## Envelope

A serialized snapshot contains:

```json
{
  "schemaVersion": "1.3",
  "inspectorVersion": "1.0.0",
  "repositoryIdentity": "github.com/owner/repository",
  "commitSha": "0123456789012345678901234567890123456789",
  "ref": "refs/heads/main",
  "observedAtUtc": "2026-08-20T08:53:12+00:00",
  "execution": {
    "id": "123456789",
    "provider": "github-actions",
    "ref": "refs/heads/main"
  },
  "reportSha256": "<64 lowercase hex characters>",
  "idempotencyKey": "dri1:<64 lowercase hex characters>",
  "idempotencyScope": "repositoryState",
  "report": {
    "schemaVersion": "1.3"
  }
}
```

`schemaVersion` is the inspection report schema version. It is intentionally repeated at the envelope level so a sink can route/index evidence without parsing the nested report first.

## Creating snapshots

Use `InspectionSnapshotFactory` after inspection:

```csharp
var execution = new InspectionExecutionMetadata(
    Id: runId,
    Provider: "github-actions",
    Ref: "refs/heads/main");

var snapshot = new InspectionSnapshotFactory().Create(
    report,
    inspectorVersion,
    execution);
```

Execution metadata is optional. The model is not tied to GitHub Actions; examples of provider values include `github-actions`, `gitlab-ci`, `azure-pipelines`, `jenkins`, or `local`.

## Repository identity

For recognized remotes, the factory normalizes identity to `host/path`:

```text
https://github.com/Owner/Repo.git
          -> github.com/Owner/Repo

git@github.com:Owner/Repo.git
          -> github.com/Owner/Repo
```

Transport credentials/user information and `.git` are not part of repository identity.

When no canonical remote can be derived, the fallback is `name:<repository-name>`. That value is useful as provenance but is intentionally not considered strong enough for repository-state idempotency.

## Report digest

`reportSha256` hashes the same deterministic, redacted JSON produced by `InspectionJsonSerializer`.

This means:

- project/diagnostic ordering is normalized before hashing;
- sensitive diagnostic context is redacted before hashing;
- the digest does not intentionally encode credentials or raw process output.

## Idempotency scopes

### `repositoryState`

Used only when:

- the remote identity is canonical;
- a commit SHA is present;
- the repository is explicitly clean (`isDirty == false`).

The key is stable across re-runs of the same evidence. CI run ID, observation timestamp, branch/ref alias, and HTTPS versus SSH clone transport do not create duplicates by themselves.

The key changes when material identity changes, including repository, commit, Inspector version, inspection schema, or evaluated report facts.

### `observation`

Used when strong repository-state identity cannot be proven, including dirty worktrees, missing commit SHA, or missing canonical remote.

If `execution.id` is provided, the observation key uses the normalized provider plus execution ID. This keeps retries inside the same execution stable. Without an execution ID, `observedAtUtc` discriminates the observation.

Observation scope is deliberately conservative: it avoids treating two potentially different dirty/local states as the same historical evidence.

## Re-executions

For a clean canonical Git repository, two executions can have different timestamps, CI run IDs, branches/refs, or clone transport and still resolve to the same idempotency key when the canonical evidence is equivalent.

The sink should use the key as an upsert/deduplication key. It may still retain execution history separately if the consumer wants to record every observation.

For `observation` scope, a different CI execution normally produces a different key.

## UTC timestamps

`observedAtUtc` is always normalized to UTC by the factory. Consumers should store it as a UTC instant rather than converting it into the sink server's local timezone.

## Retention

The Inspector does not define retention periods. Snapshot retention, compaction, archival, and deletion are responsibilities of the sink/consumer.

## Security

Do not add credentials, authorization headers, connection strings, or tokens to `InspectionExecutionMetadata`. Sink credentials belong to the host's secret mechanism and are not provenance.

`InspectionSnapshotJsonSerializer` serializes the nested report through `InspectionJsonSerializer`, preserving the project's sensitive-context redaction rules.

## Related decisions

- [ADR 0003: Keep snapshot persistence optional behind sink adapters](decisions/0003-persistence-sink-architecture.md)
- [ADR 0004: Define snapshot provenance and idempotency from canonical evidence](decisions/0004-snapshot-provenance-idempotency.md)
- [Optional persistence](persistence.md)
- [Security and privacy](security.md)
