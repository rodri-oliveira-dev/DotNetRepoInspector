# DotNetRepoInspector

**Languages:** English | [Português (Brasil)](README.pt-BR.md)

[![Build & Tests](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml/badge.svg)](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=rodri-oliveira-dev_DotNetRepoInspector&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=rodri-oliveira-dev_DotNetRepoInspector)
[![NuGet](https://img.shields.io/nuget/v/DotNetRepoInspector.svg)](https://www.nuget.org/packages/DotNetRepoInspector)
[![MCP NuGet](https://img.shields.io/nuget/v/DotNetRepoInspector.Mcp.svg?label=MCP%20NuGet)](https://www.nuget.org/packages/DotNetRepoInspector.Mcp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/badge/coverage-%E2%89%A570%25-brightgreen)](.github/coverage-baseline.json)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![GitHub Marketplace](https://img.shields.io/badge/GitHub%20Marketplace-DotNetRepoInspector-181717?logo=github)](https://github.com/marketplace/actions/dotnetrepoinspector)

**Inspect and classify .NET projects using evaluated MSBuild metadata for CI/CD, automation, architecture governance, and optional historical evidence.**

> Status: **stable v1 contract**. The public v1 contract is defined and validated in CI. Official artifacts are published only through the protected Release workflow.

## What v1 does

DotNetRepoInspector produces one deterministic, machine-readable view of a .NET repository without requiring source-code analysis or an external database.

The v1 surface includes:

- discovery of SDK-style .NET projects;
- evaluated MSBuild facts such as SDKs, target frameworks, output type, test metadata, packability, runtime identifiers, and `ProjectReference` edges;
- `global.json` and resolved SDK metadata;
- Git repository, commit, branch, remote, and dirty-state metadata when available;
- deterministic base classification: Web, Worker, Console, Library, Test, and Unknown;
- versioned inspection JSON (`schemaVersion 1.3`);
- optional repository configuration for exclusions and explicit classification overrides;
- CLI/.NET Tool and reusable Composite GitHub Action;
- optional HTTP/webhook snapshot persistence with provenance and idempotency;
- structured diagnostics, cancellation, cross-platform compatibility checks, security hardening, performance guardrails, and validation against pinned public repositories.

Application subtypes and the optional policy engine are post-v1 work and are not part of the v1 compatibility promise.

## Design principles

- **MSBuild is the source of truth.** Effective evaluated properties take precedence over raw project XML heuristics.
- **Zero configuration by default.** A useful inspection requires only a repository path.
- **Automation first.** Output is deterministic, machine-readable, and suitable for CI/CD.
- **No source-code collection.** The Inspector focuses on project/repository metadata.
- **Persistence is optional.** Inspection works without a database, HTTP endpoint, or cloud account.
- **Provider agnostic.** GitHub Actions is a delivery integration, not the core architecture.
- **Versioned public contracts.** Product, Action, CLI, and JSON compatibility rules are documented and release-gated.

## JSON contract

The v1 contract currently uses inspection schema **1.3**. A representative payload is:

```json
{
  "schemaVersion": "1.3",
  "repository": {
    "name": "sample-service",
    "commitSha": "0123456789abcdef0123456789abcdef01234567",
    "branch": "main",
    "remoteUrl": "https://github.com/example/sample-service.git",
    "isDirty": false
  },
  "dotNetSdk": {
    "globalJsonPath": "global.json",
    "configured": {
      "version": "10.0.100",
      "rollForward": "latestFeature",
      "allowPrerelease": false
    },
    "resolvedVersion": "10.0.100"
  },
  "projects": [
    {
      "path": "src/App/App.csproj",
      "name": "App",
      "resolvedSdkVersion": "10.0.100",
      "sdks": [
        { "name": "Microsoft.NET.Sdk.Web" }
      ],
      "targetFrameworks": ["net10.0"],
      "outputType": "Exe",
      "isTestProject": false,
      "isPackable": false,
      "runtimeIdentifiers": [],
      "classification": {
        "kind": "web",
        "confidence": "high",
        "signals": ["sdk:Microsoft.NET.Sdk.Web"]
      },
      "references": [],
      "diagnostics": []
    }
  ],
  "diagnostics": []
}
```

The canonical example and full contract are maintained in [`docs/en/schema/`](docs/en/schema/). Additive schema changes remain inside major `1`; a breaking schema change requires a new schema and product major and must not move the `v1` Action alias.

## Install as a .NET Tool

Package ID: `DotNetRepoInspector`  
Tool command: `dotnet-repo-inspect`  
Supported public invocation: `dotnet repo-inspect`

The package targets .NET 10 and requires a compatible .NET runtime/SDK to execute.

The CLI package is published on NuGet.org. Installing without `--version` selects the latest stable package available from the configured NuGet sources:

```bash
dotnet tool install --global DotNetRepoInspector
dotnet repo-inspect --version
dotnet repo-inspect .
```

A repository can also install the tool into a local tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install DotNetRepoInspector
dotnet repo-inspect .
```

For reproducible automation, pin an explicit package version in your own manifest/pipeline rather than relying on an example version in this README. Contributors can build and install an unpublished local package. See [`docs/en/cli.md`](docs/en/cli.md).

## CLI usage

Inspect the current repository and emit JSON to stdout:

```bash
dotnet repo-inspect .
```

Write the report to a file:

```bash
dotnet repo-inspect . --output artifacts/inspection.json
```

Use optional exclusions/classification overrides:

```bash
dotnet repo-inspect . \
  --exclude generated \
  --classify src/App/App.csproj=web
```

The default `.dotnetrepoinspector.json` file is optional. See [`docs/en/configuration.md`](docs/en/configuration.md) for its versioned format and precedence rules.

The CLI keeps machine data on stdout/output files and operational logs on stderr. Documented exit codes distinguish report errors, invalid arguments, fatal inspection, output failure, fatal persistence failure, and cancellation. See [`docs/en/cli.md`](docs/en/cli.md).

## MCP server

`DotNetRepoInspector.Mcp` exposes the same deterministic Engine facts to local MCP clients through six read-only tools over stdio. The server is an additional delivery adapter; the CLI, inspection JSON, and the product's primary repository-inspection purpose remain unchanged.

The current development build is started with an explicit repository boundary:

```bash
dotnet src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp.dll \
  --root /absolute/path/to/repository
```

The server does not call an LLM or include provider SDKs. OpenAI Codex CLI has completed a real client smoke test; Claude Code and Gemini CLI configurations are documented but remain unvalidated in this project environment.

`DotNetRepoInspector.Mcp` is packaged as a framework-dependent .NET Tool and NuGet `McpServer`, with command `dotnet-repo-inspector-mcp` and an embedded `.mcp/server.json`. Official publication is handled by the protected Release workflow and the package is distributed through NuGet.org and GitHub Packages. With .NET 10 or later, the latest stable package can be launched directly with `dnx`:

```bash
dnx DotNetRepoInspector.Mcp --yes -- --root /absolute/path/to/repository
```

For reproducible automation, use the optional `@<version>` syntax in your own configuration. Additional Claude Code and Gemini CLI validation is tracked as non-blocking interoperability evidence.

<!-- mcp-name: io.github.rodri-oliveira-dev/dotnet-repo-inspector-mcp -->

See the [MCP user guide](docs/en/mcp.md) for setup, client configuration, tool schemas, examples, security boundaries, and troubleshooting. The [client compatibility matrix](docs/en/mcp-agent-compatibility.md) records the evidence and pending validations; [GA readiness](docs/en/mcp-ga-readiness.md) is the source of truth for publication status.

## GitHub Action

> **Available on GitHub Marketplace:** [DotNetRepoInspector](https://github.com/marketplace/actions/dotnetrepoinspector). Use `@v1` to follow compatible v1 releases. For maximum reproducibility, pin an immutable full release tag or commit SHA from the Releases page.

The repository contains a reusable Composite Action that runs the exact .NET Tool version pinned by the Action revision:

```yaml
- name: Checkout
  uses: actions/checkout@v7

- name: Inspect .NET repository
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
```

Outputs include `report-path`, `schema-version`, `inspector-version`, and `exit-code`. The Action does not require write permissions or a GitHub token for inspection of an already checked-out repository.

The public `@v1` alias is available for direct use in GitHub Actions. Pin an immutable full release tag or commit SHA when exact reproducibility is preferred. See [`docs/en/github-action.md`](docs/en/github-action.md).

## Container images

Official stable releases publish the same multi-architecture image to **GHCR** and **Docker Hub**:

- `ghcr.io/rodri-oliveira-dev/dotnet-repo-inspector`
- `docker.io/rodrigodotnet/dotnet-repo-inspector`

The images target `linux/amd64` and `linux/arm64`, run non-root, include the supported .NET SDK families needed for MSBuild evaluation, and are released with SBOM/provenance verification. A hardened local/offline invocation can use the moving stable tag:

```bash
mkdir -p artifacts
docker run --rm \
  --read-only \
  --network none \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=64m \
  --mount type=bind,src="$PWD",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  ghcr.io/rodri-oliveira-dev/dotnet-repo-inspector:latest \
  /repo --output /artifacts/inspection.json
```

For reproducible deployments, pin an immutable image digest in your own automation instead of relying on `:latest`. Container execution narrows the operational boundary but does **not** make MSBuild evaluation a sandbox.

## Distribution channels

The protected Release workflow validates once and publishes through independent channels:

- CLI and MCP packages to **NuGet.org**;
- CLI and MCP packages to **GitHub Packages**;
- the reusable Action through **GitHub Marketplace / Git tags**;
- multi-architecture container images to **GHCR** and **Docker Hub**.

The final GitHub Release is created only after all required publication channels and post-publication verification gates succeed.


## Optional HTTP snapshot persistence

Persistence is disabled unless a sink is selected. The built-in HTTP/webhook sink sends the canonical `InspectionSnapshot` to a consumer-owned endpoint and includes the snapshot idempotency key in the `Idempotency-Key` header.

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Bearer credentials are supplied only through the environment, not a CLI argument:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-failure-mode fatal
```

Persistence is `non-fatal` by default. In `fatal` mode, a delivery failure returns exit code `5` after the inspection report has already been produced. See [`docs/en/persistence.md`](docs/en/persistence.md).

## Compatibility and trust boundary

The Inspector itself targets .NET 10. CI validates target repositories using .NET 8 and .NET 10 SDKs side-by-side on Ubuntu, Windows, and macOS.

MSBuild evaluation is **not a sandbox**. Untrusted repositories should be inspected only in isolated, ephemeral, non-privileged environments without credentials or sensitive data. See [`SECURITY.md`](SECURITY.md) and [`docs/en/security.md`](docs/en/security.md).

## Release readiness

The v1 release baseline is machine-readable in [`.github/release-readiness-v1.json`](.github/release-readiness-v1.json) and enforced by repository tests. It locks together the product version, schema version, Action major alias, NuGet/.NET Tool metadata, canonical schema example, and required governance/security files.

The first-publication checklist, external GitHub/NuGet prerequisites, safe dry-run procedure, and post-publication verification are documented in [`docs/en/v1-release-readiness.md`](docs/en/v1-release-readiness.md). General SemVer, release artifacts, tags, provenance, and recovery rules are in [`docs/en/releases.md`](docs/en/releases.md).

This PR/repository preparation does not itself publish a package, tag, or GitHub Release. Official publication is an explicit protected workflow action.

## Documentation

- [English documentation](docs/en/README.md)
- [Documentação em Português (Brasil)](docs/pt-BR/README.md)
- [Inspection schema v1](docs/en/schema/inspection-v1.md)
- [CLI / .NET Tool](docs/en/cli.md)
- [MCP server](docs/en/mcp.md)
- [GitHub Action](docs/en/github-action.md)
- [Release/versioning](docs/en/releases.md)
- [v1 release readiness](docs/en/v1-release-readiness.md)

## Architecture

```text
Repository
    |
    v
Inspection Engine ----> InspectionReport ----> JSON output
                           |
                           | optional
                           v
                 Snapshot Persistence
                           |
                           v
                    HTTP/webhook

Delivery hosts: CLI / .NET Tool, MCP server, GitHub Action, and container images
Post-v1 adapters: additional sinks, policy/reporting, richer subtypes
```

`DotNetRepoInspector.Core` owns normalized contracts/classification. MSBuild and Git collection remain adapters. `DotNetRepoInspector.Persistence` owns provider-neutral snapshot/provenance contracts, and `DotNetRepoInspector.Persistence.Http` is the first concrete sink. Core and Engine remain independent from HTTP/database providers and credentials.

## Contributing

External contributions are supported. Start with [`CONTRIBUTING.md`](CONTRIBUTING.md), follow the [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md), and use [`SECURITY.md`](SECURITY.md) for vulnerability reporting rather than public issues.

Classification changes require reproducible synthetic fixtures and evaluated evidence; public repositories may reveal a bug but do not replace a permanent local regression fixture.

## Roadmap

The v1 foundation and public distribution channels are established. Official publication remains an explicit protected operation. Ongoing work includes richer application subtypes, additional persistence adapters when justified, an optional policy layer over the normalized contract, and further interoperability evidence.

## License

DotNetRepoInspector is licensed under the [MIT License](LICENSE).
