# DotNetRepoInspector

**Languages:** English | [Português (Brasil)](README.pt-BR.md)

[![Build & Tests](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml/badge.svg)](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/badge/coverage-%E2%89%A570%25-brightgreen)](.github/coverage-baseline.json)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Inspect and classify .NET projects using evaluated MSBuild metadata for CI/CD, automation, architecture governance, and optional historical evidence.**

> Status: **v1.0.0 release candidate**. The public v1 contract is defined and validated in CI, but the `DotNetRepoInspector` package, GitHub Release, and `v1` Action tag are not considered published until the protected Release workflow completes successfully.

## What v1 does

DotNetRepoInspector produces one deterministic, machine-readable view of a .NET repository without requiring source-code analysis or an external database.

The v1 surface includes:

- discovery of SDK-style .NET projects;
- evaluated MSBuild facts such as SDKs, target frameworks, output type, test metadata, packability, runtime identifiers, and `ProjectReference` edges;
- `global.json` and resolved SDK metadata;
- Git repository, commit, branch, remote, and dirty-state metadata when available;
- deterministic base classification: Web, Worker, Console, Library, Test, and Unknown;
- versioned inspection JSON (`schemaVersion 1.3` for v1.0.0);
- optional repository configuration for exclusions and explicit classification overrides;
- CLI/.NET Tool and reusable Composite GitHub Action;
- optional HTTP/webhook snapshot persistence with provenance and idempotency;
- structured diagnostics, cancellation, cross-platform compatibility checks, security hardening, performance guardrails, and validation against pinned public repositories.

Application subtypes and the optional policy engine are post-v1 work and are not part of the v1.0.0 compatibility promise.

## Design principles

- **MSBuild is the source of truth.** Effective evaluated properties take precedence over raw project XML heuristics.
- **Zero configuration by default.** A useful inspection requires only a repository path.
- **Automation first.** Output is deterministic, machine-readable, and suitable for CI/CD.
- **No source-code collection.** The Inspector focuses on project/repository metadata.
- **Persistence is optional.** Inspection works without a database, HTTP endpoint, or cloud account.
- **Provider agnostic.** GitHub Actions is a delivery integration, not the core architecture.
- **Versioned public contracts.** Product, Action, CLI, and JSON compatibility rules are documented and release-gated.

## JSON contract

The v1.0.0 release baseline uses inspection schema **1.3**. A representative payload is:

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

> The package is fully packed and installation-smoke-tested in CI. Until the first protected publication succeeds, commands that resolve from NuGet.org may not be available publicly.

After publication:

```bash
dotnet tool install --global DotNetRepoInspector --version 1.0.0
dotnet repo-inspect --version
dotnet repo-inspect .
```

A repository can also pin the tool in a local tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install DotNetRepoInspector --version 1.0.0
dotnet repo-inspect .
```

Contributors can build and install an unpublished local package. See [`docs/en/cli.md`](docs/en/cli.md).

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

## GitHub Action

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

The public `@v1` alias becomes usable only after the first protected release moves it to the immutable `v1.0.0` release commit. See [`docs/en/github-action.md`](docs/en/github-action.md).

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

The v1.0.0 baseline is machine-readable in [`.github/release-readiness-v1.json`](.github/release-readiness-v1.json) and enforced by repository tests. It locks together the product version, schema version, Action major alias, NuGet/.NET Tool metadata, canonical schema example, and required governance/security files.

The first-publication checklist, external GitHub/NuGet prerequisites, safe dry-run procedure, and post-publication verification are documented in [`docs/en/v1-release-readiness.md`](docs/en/v1-release-readiness.md). General SemVer, release artifacts, tags, provenance, and recovery rules are in [`docs/en/releases.md`](docs/en/releases.md).

This PR/repository preparation does not itself publish a package, tag, or GitHub Release. Official publication is an explicit protected workflow action.

## Documentation

- [English documentation](docs/en/README.md)
- [Documentação em Português (Brasil)](docs/pt-BR/README.md)
- [Inspection schema v1](docs/en/schema/inspection-v1.md)
- [CLI / .NET Tool](docs/en/cli.md)
- [GitHub Action](docs/en/github-action.md)
- [Release/versioning](docs/en/releases.md)
- [v1.0.0 release readiness](docs/en/v1-release-readiness.md)

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

Delivery hosts: CLI / .NET Tool and GitHub Action
Post-v1 adapters: additional sinks, policy/reporting, richer subtypes
```

`DotNetRepoInspector.Core` owns normalized contracts/classification. MSBuild and Git collection remain adapters. `DotNetRepoInspector.Persistence` owns provider-neutral snapshot/provenance contracts, and `DotNetRepoInspector.Persistence.Http` is the first concrete sink. Core and Engine remain independent from HTTP/database providers and credentials.

## Contributing

External contributions are supported. Start with [`CONTRIBUTING.md`](CONTRIBUTING.md), follow the [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md), and use [`SECURITY.md`](SECURITY.md) for vulnerability reporting rather than public issues.

Classification changes require reproducible synthetic fixtures and evaluated evidence; public repositories may reveal a bug but do not replace a permanent local regression fixture.

## Roadmap after v1.0.0

The v1 foundation is complete in code and release automation. Publication itself remains an explicit protected operation. Post-v1 work includes richer application subtypes, additional persistence adapters when justified, and an optional policy layer over the normalized contract.

See tracking issue #30 for the first-public-release roadmap and the dedicated post-MVP issues for further evolution.

## License

DotNetRepoInspector is licensed under the [MIT License](LICENSE).
