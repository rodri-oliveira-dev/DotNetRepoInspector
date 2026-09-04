# ADR 0005: Define the container execution and SDK compatibility contract

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0005-container-execution-contract.md)

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** DotNetRepoInspector maintainers

## Context

DotNetRepoInspector is built for `net10.0`, but its supported inspection matrix is broader than its own runtime. The existing compatibility gate installs .NET 8 and .NET 10 SDKs side by side and proves that a repository `global.json` can select either family independently from the Inspector runtime.

That distinction must remain true in the official container image. A runtime-only image would execute the Inspector but would break MSBuild evaluation for repositories that select an SDK not present in the image.

Containerization also does not change the current trust model. ADR 0001 and the security documentation establish that MSBuild evaluation is not a sandbox. A repository can influence imports, SDK resolution, conditions, property functions, filesystem access, and network access that are available to the process identity. Docker can provide an operational isolation boundary only to the extent that the container is itself configured with restricted mounts, privileges, credentials, and network access.

This ADR defines the contract that the implementation, CI, publication, and documentation issues must preserve.

## Decision

### Official image identity

The planned official image names are:

```text
ghcr.io/rodri-oliveira-dev/dotnet-repo-inspector
docker.io/rodrigodotnet/dotnet-repo-inspector
```

Both registries must represent the same product version and source revision. Publication, tag policy, SBOM, provenance, and release verification are follow-up concerns; this ADR only fixes the identity and runtime contract.

The official image is a Linux container image. The target platforms are:

- `linux/amd64`;
- `linux/arm64`.

There is no architecture exception at the time of this decision: Microsoft publishes supported .NET SDK container artifacts for both architectures, including the .NET 8 and .NET 10 families required by this repository. If either required SDK family ceases to be available or usable on one target platform, publication for that platform must fail until the compatibility contract is deliberately revised; silently publishing a reduced matrix is not allowed.

### SDK compatibility inside the image

The image must contain stable SDKs from both supported families:

| Container platform | Inspector runtime | Required SDK families available to inspected repositories |
| --- | --- | --- |
| `linux/amd64` | `net10.0` | .NET 8 and .NET 10 |
| `linux/arm64` | `net10.0` | .NET 8 and .NET 10 |

The guarantee is by SDK family, not by the original minimum feature band used by a fixture. The existing compatibility fixtures pin `8.0.100` and `10.0.100` with `rollForward: latestFeature`; therefore a serviced stable SDK in the corresponding `8.0.x` or `10.0.x` family may satisfy the fixture through normal `dotnet` resolution rules.

The image does not guarantee .NET 9, preview SDKs, workloads, or arbitrary SDK families unless they are later added to the repository compatibility policy and this contract is revised.

The implementation must preserve normal SDK selection semantics. It must not rewrite an inspected repository's `global.json` or force all MSBuild evaluation through the Inspector's .NET 10 SDK.

### Base-image and SDK maintenance strategy

The image must use official Microsoft .NET Linux distribution artifacts. The .NET 10 SDK is the natural execution foundation because the Inspector itself targets `net10.0`; a stable .NET 8 SDK must then be made available side by side from an official Microsoft source.

The exact installation mechanism is an implementation detail, but it must satisfy all of these invariants:

- the selected Linux distribution/variant is supported for both target architectures;
- both required SDK families are installed from official Microsoft artifacts;
- no `latest` base tag is used;
- every `FROM` uses a human-readable version/OS tag plus an immutable digest when the registry supports it;
- any separately installed SDK is selected by an explicit stable version and obtained through a verifiable Microsoft distribution mechanism;
- the final image exposes both SDK families through `dotnet --list-sdks`;
- SDK/base updates are expected maintenance, not a reason to freeze old vulnerable layers indefinitely.

Digest pinning provides reproducibility; it does not replace servicing. Automated base-image maintenance is handled by the dedicated follow-up issue and must re-run the same compatibility/security gates before merge.

### Filesystem and mount contract

The container has two distinct persistent paths:

| Path | Purpose | Expected access |
| --- | --- | --- |
| `/repo` | repository/source being inspected | read-only by default |
| `/artifacts` | generated inspection report and other explicit outputs | writable |

`/repo` is the default working directory. The Inspector must not require writes to the inspected repository for a basic inspection. Consumers are expected to enforce the source boundary with a read-only bind mount.

The container root filesystem must support execution with Docker `--read-only`. Runtime scratch data that cannot be avoided by the .NET SDK or operating-system tooling must be ephemeral and explicitly mounted as `tmpfs`, normally under `/tmp`; it is not a persistent data path and must not contain caller credentials. Persistent output belongs only under `/artifacts` unless a future documented feature introduces another explicit data mount.

A hardened local/offline baseline is therefore equivalent to:

```bash
docker run --rm \
  --read-only \
  --network none \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=64m \
  --mount type=bind,src="$PWD",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  <image> /repo --output /artifacts/inspection.json
```

The final image entrypoint must forward arguments to the existing DotNetRepoInspector CLI contract, so callers do not need to know the internal DLL or tool-installation layout.

Scenarios that need additional writable caches, private SDK material, or package-feed state are explicit deviations from the baseline. They must use dedicated, narrowly scoped mounts and must not make `/repo` writable merely to satisfy tooling.

### Non-root execution and UID/GID behavior

The final image must declare and run as a non-root user by default. It may use the non-root account supplied by the selected official .NET base image or an equivalent image-local account; root must not be required for inspection.

The runtime must also tolerate an explicit numeric UID/GID supplied by the caller where Docker supports it, for example:

```bash
docker run --user "$(id -u):$(id -g)" ...
```

This allows files created in the host-mounted `/artifacts` directory to be owned by the invoking host user instead of by the image's default numeric identity.

The image must not depend on a writable root-owned home directory or on a passwd entry for the effective runtime UID. Writable CLI/home/temp state required by .NET must resolve to the ephemeral scratch area. The caller is responsible for making `/artifacts` writable by the effective UID/GID; the image must not solve host permission problems by switching to root or chmod/chown of the mounted repository.

### Privilege and host-integration baseline

Basic inspection must work without:

- privileged mode;
- the Docker socket or another container-engine socket;
- host PID/IPC namespaces;
- additional Linux capabilities;
- writable source mounts;
- host credential directories;
- repository, cloud, SSH, signing, deployment, Docker, or Kubernetes credentials.

The validation baseline uses `--cap-drop=ALL` and `--security-opt=no-new-privileges`. No implementation may require `/var/run/docker.sock` or `--privileged` as a convenience for inspecting repositories.

### Network policy

Local inspection is an offline-capable operation. When the repository-selected SDKs, imports, and other evaluation dependencies are already present, the container must be able to inspect with `--network none`.

The image must not perform an implicit restore or automatic upload merely because network access is available. Repository-controlled MSBuild evaluation can still attempt network access if the container network permits it; this is another reason the hardened baseline disables networking.

Network access is opt-in for features that inherently need it. The built-in HTTP sink is the canonical example and remains explicitly selected with the existing CLI options such as `--sink http` and `--sink-url`. Private SDK/package-feed scenarios may also require network access, but they are not part of the offline compatibility guarantee and must be configured explicitly.

Enabling network access does not relax any other control: non-root execution, restricted mounts, least privilege, and secret handling still apply.

### Secret and credential policy

No credential may be baked into an image layer, build argument, image environment default, OCI label/annotation, manifest, or example command line.

The existing Inspector credential rules remain authoritative:

- sink credentials are runtime-only;
- credentials are never passed as CLI arguments or embedded in `--sink-url`;
- the HTTP sink bearer token uses the existing `DOTNET_REPO_INSPECTOR_HTTP_TOKEN` runtime environment variable;
- private feed/SDK credentials, when unavoidable, must be short-lived and scoped only to the required source;
- broad host credential directories such as Docker, cloud, SSH, Kubernetes, or package-manager profiles must not be mounted as a shortcut;
- credentials must not be written to `/artifacts`, logs, diagnostics, image metadata, or persisted scratch data.

Passing a runtime environment variable to the container does not make it safe for MSBuild to see arbitrary credentials. DotNetRepoInspector's child-process filtering remains defense in depth only. The safest offline inspection has no credentials in the container at all.

### Containerization is not an MSBuild sandbox

**The official container image is not a security sandbox for MSBuild evaluation.**

The container only limits what the evaluated repository can reach when the caller limits what the container can reach. Any file mounted into the container, environment value visible to the process, network destination reachable from the namespace, or capability granted to the container may be reachable by repository-controlled MSBuild evaluation.

Accordingly, inspecting untrusted code still requires an isolated, ephemeral, non-privileged environment without sensitive data. The Docker hardening in this ADR reduces exposure but does not convert evaluation of untrusted MSBuild logic into a trusted operation.

## Compatibility validation contract

The implementation and CI follow-up issues must prove the contract rather than infer it from image contents.

For both `linux/amd64` and `linux/arm64`, validation must demonstrate at minimum:

1. the image builds from the pinned/declared base references;
2. the effective runtime user is non-root;
3. `dotnet --list-sdks` contains at least one stable .NET 8 SDK and one stable .NET 10 SDK;
4. the existing `tests/Fixtures/Compatibility/Net8` repository resolves to the .NET 8 family;
5. the existing `tests/Fixtures/Compatibility/Net10` repository resolves to the .NET 10 family;
6. both fixtures can be inspected without making `/repo` writable;
7. a representative successful inspection writes its report only to `/artifacts`;
8. a compatible prepared fixture succeeds with root filesystem `--read-only`, ephemeral scratch, and `--network none`;
9. the baseline needs no Docker socket, privileged mode, host credential mount, or secret;
10. failure of either required SDK family or either target architecture blocks official multi-platform publication.

Emulation through Buildx/QEMU is acceptable for CI when native runners are unavailable, provided the resulting image for each target architecture is actually executed during the smoke test rather than only built. Release verification must also confirm that the published OCI index contains both required platforms.

The repository's existing cross-platform host matrix (Linux, Windows, macOS) remains the contract for direct CLI/.NET Tool execution. This ADR adds a Linux-container platform matrix; it does not remove or replace the host compatibility matrix.

## Alternatives considered

### Runtime-only final image

Rejected. DotNetRepoInspector performs MSBuild evaluation and must honor repositories selecting supported SDK families. A small runtime-only image would make the distribution misleading by losing the current .NET 8/.NET 10 compatibility guarantee.

### Separate official image per SDK family

Rejected for the initial distribution. It would force callers to know the repository SDK before inspection and would diverge from the existing side-by-side host behavior. A single image containing both supported families preserves the current contract.

### Run as root and rely on Docker isolation

Rejected. Root is unnecessary for the CLI and increases the impact of an overly broad mount or container misconfiguration.

### Writable source mount

Rejected as the default. Inspection is a metadata-reading operation. Outputs belong in a separate artifacts mount, which makes write intent explicit and easier to constrain.

### Network enabled as a requirement

Rejected. Basic inspection must remain local/offline-capable. Network-dependent sinks and private dependency resolution are separate opt-in scenarios.

### Treat the container as a sandbox for untrusted MSBuild

Rejected. That would contradict the actual MSBuild trust model and the existing security documentation. Container hardening narrows accessible resources but does not make repository-controlled evaluation intrinsically safe.

## Consequences

### Positive

- the container preserves the existing .NET 8/.NET 10 evaluation guarantee;
- the implementation has objective mount, identity, network, secret, and platform contracts;
- source and output writes are separated explicitly;
- non-root and read-only-root operation can be tested before publication;
- consumers can run a genuinely offline hardened baseline when dependencies are pre-provisioned;
- GHCR and Docker Hub use a single planned image identity and release model;
- multi-platform publication has an explicit fail-closed compatibility requirement.

### Trade-offs

- the official image is larger than a runtime-only CLI image because it contains multiple SDK families;
- supporting `--read-only` requires explicit ephemeral scratch handling;
- arbitrary host UID/GID execution requires the image not to depend on a conventional writable home directory;
- repositories that depend on private SDKs, feeds, workloads, or network imports need additional explicit runtime configuration and are outside the offline baseline;
- the image reduces operational exposure but cannot promise safe evaluation of hostile MSBuild logic.

## Follow-up work

- **#101** — implement the Dockerfile and local hardened execution exactly against this contract.
- **#102** — make the platform/SDK/mount/non-root/read-only/offline/security smoke tests release-gating CI checks.
- **#103** — automate safe base-image/digest servicing.
- **#104** — publish the same multi-platform release to the two registries defined here.
- **#105** — attach digest-based SBOM/provenance and supply-chain verification.
- **#106** — publish the end-user container/security documentation and complete release readiness.

## References

- SDK/OS compatibility policy: [`../compatibility.md`](../compatibility.md)
- Security model: [`../security.md`](../security.md)
- Repository security policy: [`../../../SECURITY.md`](../../../SECURITY.md)
- MSBuild evaluation strategy: [ADR 0001](0001-msbuild-evaluation-strategy.md)
- Persistence/sink architecture: [ADR 0003](0003-persistence-sink-architecture.md)
- Microsoft Learn — .NET container images: https://learn.microsoft.com/dotnet/core/docker/container-images
- Microsoft Artifact Registry — .NET SDK images: https://mcr.microsoft.com/artifact/mar/dotnet/sdk
- Issue #100: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/100
