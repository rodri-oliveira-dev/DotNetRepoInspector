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
                 InspectionSnapshotFactory
                           |
                           v
                 InspectionSnapshotPublisher
                           |
                           v
                  IInspectionSnapshotSink
                           |
                           v
                 consumer-owned destination
```

`DotNetRepoInspector.Persistence` depends only on `DotNetRepoInspector.Core`. The built-in HTTP adapter lives in the separate `DotNetRepoInspector.Persistence.Http` assembly. `Core` and `Engine` do not depend on persistence or HTTP.

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

No sink is created or invoked by the inspection engine. The CLI or another delivery host explicitly chooses a sink and calls `InspectionSnapshotPublisher` after inspection has produced an `InspectionReport`.

Without `--sink`, the CLI keeps its existing zero-network persistence behavior. Repository configuration in `.dotnetrepoinspector.json` does not enable persistence.

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

## Built-in HTTP/webhook sink

ADR 0003 selects HTTP/webhook delivery as the first built-in sink. `HttpInspectionSnapshotSink` sends the canonical snapshot envelope to a consumer-owned endpoint with:

- HTTP `POST`;
- `Content-Type: application/json`;
- the payload produced by `InspectionSnapshotJsonSerializer`;
- `Idempotency-Key: <snapshot.idempotencyKey>`;
- optional `Authorization: Bearer <token>` when a token is supplied through the host environment.

The endpoint must be an absolute HTTP or HTTPS URL. URLs containing embedded user information such as `https://user:password@example/...` are rejected. The adapter never reads a response body into a failure message.

### CLI configuration

Enable the sink explicitly:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Optional delivery policy:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-timeout-seconds 30 \
  --sink-max-attempts 3 \
  --sink-failure-mode fatal
```

Supported HTTP sink CLI options are:

| Option | Default | Meaning |
| --- | --- | --- |
| `--sink http` | disabled | Explicitly selects the built-in HTTP sink. |
| `--sink-url <url>` | none | Consumer-owned absolute HTTP/HTTPS endpoint. Required when the sink is enabled. |
| `--sink-timeout-seconds <1..300>` | `15` | Overall persistence deadline, including retries. |
| `--sink-max-attempts <1..5>` | `3` | Maximum number of HTTP attempts. |
| `--sink-failure-mode non-fatal|fatal` | `non-fatal` | Whether persistence failure should fail the command/pipeline. |

There is intentionally **no CLI token argument**. For Bearer authentication, set `DOTNET_REPO_INSPECTOR_HTTP_TOKEN` in the process environment or equivalent secret facility:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . --sink http --sink-url https://evidence.example/api/snapshots
```

Do not put tokens in `--sink-url`, command-line arguments, `.dotnetrepoinspector.json`, committed scripts, or the inspection JSON.

## Retry and failure classification

The generic publisher does not retry. Retry belongs to the HTTP adapter because it can classify HTTP/transport failures and safely replay the same snapshot using its idempotency key.

The HTTP sink retries only:

- `HttpRequestException` transport failures;
- request timeouts not caused by caller cancellation;
- HTTP `408`, `429`, `500`, `502`, `503`, and `504`.

Retries use bounded exponential backoff, respect the configured maximum attempt count, and remain inside the publisher's overall timeout/cancellation boundary.

Authentication failures (`401`/`403`), `404`, and other non-transient `4xx` responses are not retried. Failure messages use stable classifications and do not copy exception text or response bodies.

## Fatal and non-fatal delivery

The report is produced before persistence is attempted.

With the default `non-fatal` mode, a persistence failure is logged to stderr but normal inspection exit semantics are preserved. With `fatal`, a persistence failure returns CLI exit code `5`. The already-produced `InspectionReport` is not rewritten with a persistence diagnostic in either mode.

Caller cancellation, including Ctrl+C, is propagated through snapshot publication and the HTTP request and exits through the normal cancellation path (`130`).

## GitHub Action

The reusable Action exposes `sink-url`, `sink-token`, `sink-timeout-seconds`, `sink-failure-mode`, and `sink-max-attempts`. `sink-token` should always reference a GitHub Actions secret. The Action maps it directly to `DOTNET_REPO_INSPECTOR_HTTP_TOKEN`; it is not appended to the CLI argument list.

Example:

```yaml
- name: Inspect and persist evidence
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
    sink-url: https://evidence.example/api/snapshots
    sink-token: ${{ secrets.INSPECTOR_EVIDENCE_TOKEN }}
    sink-failure-mode: fatal
```

When persistence is enabled, the Action also supplies generic execution provenance (`run_id:run_attempt`, provider, and ref) to the snapshot factory without adding GitHub-specific types to the persistence contract.

## Configuration and secrets

Persistence configuration is a delivery concern, not repository-inspection configuration. The repository's `.dotnetrepoinspector.json` must not contain sink credentials.

Tokens, connection strings, API keys, and similar values must come from environment/secret facilities appropriate to the host and must never be copied into `InspectionReport`, `InspectionSnapshot`, diagnostic context, or normal logs.

See [`security.md`](security.md) for the project-wide secret-handling rules.

## Related decisions

- [ADR 0003: Keep snapshot persistence optional behind sink adapters](decisions/0003-persistence-sink-architecture.md)
- [ADR 0004: Define snapshot provenance and idempotency from canonical evidence](decisions/0004-snapshot-provenance-idempotency.md)
- [Snapshot provenance and idempotency](snapshot-provenance.md)
- [CLI](cli.md)
- [GitHub Action](github-action.md)
- [Inspection schema](schema/inspection-v1.md)
- [Security and privacy](security.md)
