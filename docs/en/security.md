# Security and privacy

**Languages:** English | [Português (Brasil)](../pt-BR/security.md)

DotNetRepoInspector is designed to produce a technical metadata snapshot, not a source-code or secret inventory. This document defines the current collection boundary, the MSBuild trust model, delivery permissions, and the controls that keep operational data out of the normalized report.

## Data collected

The normalized `InspectionReport` can contain only the public contract documented under [`schema/inspection-v1.md`](schema/inspection-v1.md):

- repository identity: repository name, `HEAD` commit SHA, symbolic branch, sanitized `origin` URL, and dirty/clean state when available;
- .NET SDK metadata: applicable `global.json` path, configured SDK version/roll-forward/prerelease settings, and resolved SDK version;
- project metadata: repository-relative project path and name, resolved/declared SDKs, target frameworks, output type, test/packable flags, runtime identifiers, classification, and normalized `ProjectReference` paths;
- stable diagnostics: `DRIxxxx` code, severity, stable message, normalized source, and controlled context.

Paths in the report are normalized and repository-relative where the contract defines them as such. Machine-specific absolute workspace paths are not intentionally exposed.

The Git adapter removes URI user information, query strings, and fragments from an absolute remote URL before it enters the public contract. This prevents common token-bearing remote formats from being serialized.

## Data not collected or serialized

The Inspector does **not** intentionally place the following data in the normalized snapshot:

- source-code contents;
- arbitrary file contents;
- the raw MSBuild property dictionary;
- process environment-variable values;
- credentials, passwords, API keys, access tokens, private keys, or connection strings;
- raw child-process stdout/stderr;
- NuGet authentication material;
- GitHub tokens or workflow secrets;
- contents of configuration files other than the small set of values represented explicitly by the public contract.

`global.json` is read only to obtain the supported SDK configuration fields. Project files and imports are evaluated by MSBuild to obtain effective metadata, but their text is not copied into the report.

Diagnostic context is defense-in-depth sanitized at serialization time: values whose context keys look credential-bearing (for example `token`, `password`, `connectionString`, `secret`, or `apiKey`) are emitted as `<redacted>`.

## No automatic upload

The CLI and .NET Tool write the inspection JSON to stdout or to the requested local file. The GitHub Action writes the report to a runner-local file and exposes its path as an Action output. DotNetRepoInspector does not upload the report to a remote service by itself.

A later persistence/sink integration must make network transfer explicit and preserve the rules in the **Sink credentials** section below.

## MSBuild trust model

MSBuild evaluation is **not a sandbox or a security boundary**. Microsoft documents that unknown MSBuild logic should be treated as capable of executing code in the build environment. Even when no target is requested, evaluation can process imports, SDK resolution, conditions, environment-backed properties, and property functions. Property functions can read environment variables and accessible files.

DotNetRepoInspector deliberately uses `-getProperty` / `-getItem` without requesting targets, so normal build targets and tasks are not executed merely to collect metadata. This reduces side effects, but it does not make untrusted evaluation safe.

Current runtime mitigations are:

- MSBuild runs out-of-process from the Inspector;
- process arguments use `ProcessStartInfo.ArgumentList`; no shell command is constructed;
- no build/design-time target is requested for metadata collection;
- cancellation terminates the child process tree;
- `MSBUILDDISABLENODEREUSE=1` prevents MSBuild worker reuse across inspections;
- telemetry is disabled for Inspector-owned `dotnet` child processes;
- credential-like environment variables are removed before `dotnet` and MSBuild start;
- known credential-bearing handles/configuration pointers such as `SSH_AUTH_SOCK`, `GPG_AGENT_INFO`, `DOCKER_CONFIG`, and `KUBECONFIG` are removed from those child environments;
- raw MSBuild/stdout/stderr details are not mapped into the normalized `InspectionReport`.

Environment filtering is defense in depth, not data-loss prevention. A secret stored under an unusual, non-secret-looking environment-variable name can still be visible to MSBuild. Evaluation can also access files and network resources available to the operating-system identity. There is currently no filesystem or network sandbox.

## Inspecting untrusted repositories

For code you do not fully trust, use a separate security boundary around DotNetRepoInspector:

- run on an ephemeral disposable runner/container/VM;
- do not expose repository, cloud, package-feed, signing, SSH, or deployment credentials to the job;
- avoid privileged containers, host sockets, writable host mounts, and production network access;
- restrict outbound network and cloud-instance metadata access when the environment supports it;
- do not colocate sensitive repositories or files in locations readable by the inspection identity;
- pre-provision only the SDKs/dependencies required for inspection;
- destroy the environment after inspection.

If a private SDK or build extension requires credentials during evaluation, prefer a dedicated short-lived identity scoped only to the required package source. Do not reuse deployment or production credentials. Be aware that the Inspector's child-process filtering intentionally removes common credential-like environment variables; pre-provisioning dependencies is safer than making secrets visible to MSBuild evaluation.

## GitHub Action permissions

The reusable composite Action does not require GitHub API write access and does not expose a token input. The repository's own validation workflows use:

```yaml
permissions:
  contents: read
```

Consumer workflows should do the same unless another step genuinely requires additional permissions. Grant additional permissions at the narrowest possible job/step boundary rather than broadening the inspection job.

When checking out source for inspection, `persist-credentials: false` is recommended when later steps do not need Git credentials:

```yaml
permissions:
  contents: read

steps:
  - uses: actions/checkout@v7
    with:
      persist-credentials: false

  - uses: rodri-oliveira-dev/DotNetRepoInspector@v1
    with:
      path: .
```

A workflow should never run inspection of untrusted code in a secrets-bearing privileged job merely because the Action itself requests only read access.

## Logs and diagnostics

Operational logs go to stderr; JSON goes to stdout or the selected file. The CLI:

- does not log raw command-line argument values;
- logs exception types instead of raw exception messages at the delivery boundary;
- redacts structured context when keys look sensitive;
- keeps debug/verbose logs separate from JSON output.

Inspection diagnostics use stable, controlled messages and machine-readable context. Infrastructure-specific raw error text is intentionally not required by the public diagnostic contract.

Do not add raw process output, environment dumps, HTTP authorization headers, connection strings, tokens, or exception messages from credential-bearing SDKs/clients to either diagnostic `details` or log messages.

## Sink credentials

Persistence/sink support is a separate feature, but all future sinks must follow these rules:

- obtain credentials from the host's secret store/environment or workload identity, never from the inspection report;
- never serialize sink credentials into `.dotnetrepoinspector.json` or `InspectionReport`;
- do not pass credentials in CLI arguments or URLs/query strings;
- prefer short-lived workload identity/OIDC credentials over long-lived static tokens;
- use least-privilege scopes limited to the target sink and operation;
- use TLS for remote transport and validate the destination;
- redact authentication material from structured logs and exceptions;
- keep retry/dead-letter payloads limited to the inspection snapshot, not the transport credential context.

## Reporting vulnerabilities

See the repository root [`SECURITY.md`](../../SECURITY.md) for the private reporting process. Do not disclose exploitable vulnerabilities or real credentials in public issues.

## References

- Microsoft Learn — Secure MSBuild usage best practices: https://learn.microsoft.com/visualstudio/msbuild/msbuild-security-best-practices
- Microsoft Learn — Evaluate MSBuild items and properties: https://learn.microsoft.com/visualstudio/msbuild/evaluate-items-and-properties
- Microsoft Learn — Property functions: https://learn.microsoft.com/visualstudio/msbuild/property-functions
- Microsoft Learn — Environment variables in MSBuild: https://learn.microsoft.com/visualstudio/msbuild/how-to-use-environment-variables-in-a-build
- GitHub Docs — Use `GITHUB_TOKEN` for authentication: https://docs.github.com/actions/security-for-github-actions/security-guides/automatic-token-authentication
