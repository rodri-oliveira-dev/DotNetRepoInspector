# Deployment evidence

**Languages:** English | [Português (Brasil)](../pt-BR/deployment-evidence.md)

Schema `1.7` adds factual evidence that downstream inventory systems can correlate with separately maintained runtime-support or security data. DotNetRepoInspector does **not** determine whether an application is vulnerable, exploitable, or deployed from the inspected repository.

## Publication properties

Every project contains a `publication` object built from evaluated MSBuild properties:

| Contract field | Evaluated property | Meaning |
| --- | --- | --- |
| `selfContained` | `SelfContained` | Effective self-contained setting in the evaluated context. SDK defaults may produce an evaluated `false`. |
| `publishSelfContained` | `PublishSelfContained` | Publish-specific self-contained setting when available. |
| `publishAot` | `PublishAot` | Native AOT publication request when available. |
| `publishTrimmed` | `PublishTrimmed` | Trimming request when available. |
| `runtimeFrameworkVersion` | `RuntimeFrameworkVersion` | Requested runtime framework version when available. |

Each field has one state:

- `evaluated`: MSBuild returned a supported value; `value` is present, including explicit `false`;
- `unspecified`: the evaluated property was empty;
- `unknown`: the evaluated value could not be represented safely or, for booleans, was not valid;
- `context-dependent`: target-framework evaluations produced different states or values.

Multi-target projects are evaluated once for the outer build and once per target framework. The project-level publication value is retained only when those contexts agree; disagreement becomes `context-dependent`. Target frameworks and runtime identifiers remain separate evidence. These properties do not prove the arguments used by a later `dotnet publish`, the files copied into an image, or the runtime currently deployed.

## Docker image discovery

Container Discovery is opt-in:

```bash
dotnet repo-inspect . --discover-containers --output artifacts/inspection.json
```

It can also be enabled with `containerDiscovery.enabled` in configuration. When disabled, the Engine does not invoke the discovery pipeline, does not enumerate Dockerfiles, and emits empty `containerImages` plus `containerDiscovery.enabled=false`.

The bounded parser recognizes files named `Dockerfile`, `Dockerfile.*`, or `*.Dockerfile`; global `ARG` defaults declared before the first `FROM`; `FROM`; `--platform=...`; comments; line continuations; stage aliases; references to earlier stages; `scratch`; tags; and SHA-256 digests. It classifies `sdk`, `aspnet`, `runtime`, and `runtime-deps` only for the exact official `mcr.microsoft.com/dotnet/...` repository family. Similar names in other registries remain ordinary external images.

Unsupported flags, malformed references, missing/dynamic arguments, shell expressions, and secret-shaped `ARG` names are not guessed. They produce `resolutionStatus=unresolved` and omit the unresolved expression from the normalized evidence. A parser diagnostic may identify the source file and line using controlled reason codes.

Discovery is deterministic and read-only. It sorts paths and results ordinally, skips build/generated directories and filesystem links, rejects paths outside the repository, accepts caller cancellation, and enforces these default ceilings:

| Resource | Default limit |
| --- | ---: |
| Visited filesystem paths | 10,000 |
| Dockerfiles | 500 |
| Bytes per file | 1 MiB |
| Total bytes | 20 MiB |
| Directory depth | 32 |
| Results | 1,000 |
| Diagnostics | 100 |
| Elapsed discovery time | 30 seconds |

Reaching a ceiling preserves collected evidence, emits a controlled diagnostic, and sets `containerDiscovery.truncated=true`.

## Security boundary and limitations

Container Discovery never invokes Docker or a shell, builds or pulls an image, executes repository code, expands process environment variables, contacts a registry, or resolves a remote digest. It reads only eligible bounded Dockerfile text. It does not interpret BuildKit syntax completely, execute Dockerfile commands, inspect Compose/Kubernetes manifests, inspect an image filesystem, or prove what was deployed.

Only normalized references and source locations cross into `InspectionReport`. Source bodies and unresolved expressions are not serialized. `ARG` defaults whose names look credential-bearing are deliberately treated as unresolved so their values do not become evidence. See [Security and privacy](security.md) for the separate MSBuild trust model and guidance for untrusted repositories.

## Runtime-audit input example

The following command collects evidence that a later, separate inventory process can correlate with official runtime-support metadata:

```bash
dotnet repo-inspect . --discover-containers --output artifacts/inspection.json
```

A downstream system can read `projects[].targetFrameworks`, `projects[].runtimeIdentifiers`, `projects[].publication`, and resolved external entries in `containerImages[]`. For example, an inventory record might retain the evaluated `publishAot` state beside `mcr.microsoft.com/dotnet/aspnet:10.0` and its Dockerfile location.

That correlation is not a vulnerability result. The Inspector does not query CVE sources, determine patch applicability, inspect the actually deployed artifact, or claim that an application is vulnerable. Security conclusions require external authoritative release data plus deployment provenance and policy owned by the downstream consumer.

## Current adapter exposure

The CLI exposes `--discover-containers`. Other adapters can enable the capability through the shared repository configuration/request contract without implementing another parser. The central `.github` inventory workflow is intentionally a follow-up integration after this feature ships; it is not modified by issue #218.
