# Local container image

**Languages:** English | [Português (Brasil)](../pt-BR/container.md)

The repository contains the implementation of the planned official DotNetRepoInspector container image. This stage is for local build and validation only; issue #101 does **not** publish an image to GHCR or Docker Hub.

The runtime contract is defined by [ADR 0005](decisions/0005-container-execution-contract.md).

## Image contents

The image:

- runs DotNetRepoInspector on .NET 10;
- contains stable .NET 8 and .NET 10 SDK families side by side so repository `global.json` selection remains authoritative;
- uses Microsoft .NET SDK images with explicit version/OS tags and immutable multi-platform digests;
- runs as the Microsoft-provided non-root `app` identity (`APP_UID`, currently 1654) by default;
- keeps `/repo` for read-only source and `/artifacts` for explicit writable output;
- redirects CLI home, NuGet cache, and other transient state to `/tmp`, allowing the container root filesystem to be read-only when `/tmp` is mounted as `tmpfs`;
- starts the existing CLI directly, so container arguments are the normal DotNetRepoInspector CLI arguments.

## Build locally

From the repository root:

```bash
docker build --pull -t dotnet-repo-inspector:local .
```

No registry push is performed by this command or by the repository's normal validation workflow.

## Verify the SDK matrix

The CLI is the image entrypoint. Override it only for image diagnostics such as checking installed SDKs:

```bash
docker run --rm \
  --entrypoint dotnet \
  dotnet-repo-inspector:local \
  --list-sdks
```

The output must contain at least one stable `8.0.x` SDK and one stable `10.0.x` SDK.

The compatibility fixtures intentionally use normal `global.json` roll-forward semantics:

- `tests/Fixtures/Compatibility/Net8` selects the .NET 8 family;
- `tests/Fixtures/Compatibility/Net10` selects the .NET 10 family.

The image does not rewrite `global.json` and does not force inspected repositories onto the Inspector's .NET 10 SDK.

## CLI smoke checks

```bash
docker run --rm dotnet-repo-inspector:local --help
docker run --rm dotnet-repo-inspector:local --version
```

The container preserves the CLI's existing output and exit-code contract.

## Hardened offline inspection

Create the output directory on the host first:

```bash
mkdir -p artifacts
```

On Linux, use the invoking host UID/GID so the bind-mounted output directory remains writable without making the image run as root. Then inspect a prepared repository or fixture with a read-only source mount, writable artifacts mount, read-only container filesystem, no network, no Linux capabilities, and no privilege escalation:

```bash
docker run --rm \
  --user "$(id -u):$(id -g)" \
  --read-only \
  --network none \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=64m,mode=1777 \
  --mount type=bind,src="$PWD/tests/Fixtures/Compatibility/Net8",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  dotnet-repo-inspector:local \
  /repo --output /artifacts/net8-inspection.json
```

A successful run writes the normalized `InspectionReport` to the host `artifacts` directory without requiring a writable repository, Docker socket, host credential directory, privileged mode, or network access.

Repeat the same command with `tests/Fixtures/Compatibility/Net10` to exercise .NET 10 SDK resolution.

## Host UID/GID and artifact ownership

The image itself declares the Microsoft-provided non-root `app` identity as its default user. Supplying `--user "$(id -u):$(id -g)"` is a runtime override for bind-mount ownership; it does not make the container privileged and the CLI does not require a passwd entry or writable root-owned home directory.

The host `/artifacts` directory must be writable by the selected UID/GID. The image does not switch to root or chmod/chown the mounted repository to work around host permission problems.

## Network-dependent scenarios

Basic prepared-repository inspection is offline-capable and should prefer `--network none`.

Network access is an explicit deviation for features that inherently require it, such as the opt-in HTTP sink or private SDK/package sources. For the HTTP sink, keep the existing credential contract: pass the bearer token only at runtime through `DOTNET_REPO_INSPECTOR_HTTP_TOKEN`; never embed credentials in the image, a build argument, a CLI argument, or `--sink-url`.

Example shape:

```bash
docker run --rm \
  -e DOTNET_REPO_INSPECTOR_HTTP_TOKEN \
  --mount type=bind,src="$PWD",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  dotnet-repo-inspector:local \
  /repo \
  --output /artifacts/inspection.json \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Do not mount broad Docker, cloud, SSH, Kubernetes, NuGet, or other host credential directories as a shortcut.

## Security boundary

**Containerization does not make MSBuild evaluation a security sandbox.**

Repository-controlled MSBuild evaluation can access resources that the container identity can access. Hardened mounts, non-root execution, a read-only root filesystem, dropped capabilities, and disabled networking reduce exposure, but untrusted repositories still require an isolated, ephemeral environment without sensitive data or credentials.

See [`security.md`](security.md) and [ADR 0005](decisions/0005-container-execution-contract.md) for the complete trust model.

## CI validation

[`validate-container.yml`](../../.github/workflows/validate-container.yml) turns the image into a pull-request quality/security gate. It:

- runs Hadolint with warnings treated as failures;
- validates every Microsoft .NET base reference read from the proposed `Dockerfile`, requiring a human-readable tag plus an immutable `sha256` digest;
- builds with `--pull` from those pinned base references;
- executes the image for both `linux/amd64` and `linux/arm64` through Buildx/QEMU;
- runs the reusable [`container_smoke.sh`](../../.github/scripts/container_smoke.sh) suite to verify non-root execution, `--help`, `--version`, .NET 8/.NET 10 SDK resolution, successful report generation, read-only source/root filesystems, offline operation, and documented CLI exit codes `0` through `5`;
- runs a visible Trivy report for all `HIGH`/`CRITICAL` findings, including findings without an upstream fix;
- runs a second Trivy gate with `ignore-unfixed` so only fixable `HIGH`/`CRITICAL` vulnerabilities block the workflow.

The workflow has read-only repository permissions and never logs in to or pushes to a container registry. Third-party Actions used by this gate are pinned to commit SHAs.

## Base-image maintenance with Dependabot

[`.github/dependabot.yml`](../../.github/dependabot.yml) monitors the root `Dockerfile` with the `docker` ecosystem. During the initial container stabilization period it checks on weekdays at `08:00` in `America/Sao_Paulo`, with at most three simultaneous Docker version-update pull requests.

Docker base references remain in `image:version-tag@sha256:digest` form. Dependabot is allowed to propose updated tags/digests, but every resulting `Dockerfile` pull request must pass the same Hadolint, multi-architecture build, smoke, and Trivy gates before merge. The workflow reads base references from the proposed `Dockerfile`, rather than duplicating their versions or digests in CI, so an update cannot be validated against a stale hardcoded reference.

GitHub documents that the Docker ecosystem scans `Dockerfile` manifests from the configured `directory`, and Dependabot's Docker updater preserves and updates an existing digest when changing an already digest-pinned image. Dependabot evaluates `dependabot.yml` from the default branch, so live creation of Docker update pull requests starts only after this configuration reaches the default branch; that documented behavior is the equivalent validation used while this work is still on the integration branch.

The initial `daily` cadence can return to `weekly` after the container rollout is stable: at least four consecutive weeks without recurring manual base-image remediation or security-gate tuning, and no known backlog of fixable `HIGH`/`CRITICAL` findings. Reducing the schedule must not relax digest pinning or any CI/security gate.
