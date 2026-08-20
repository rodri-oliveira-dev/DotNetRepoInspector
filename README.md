# DotNetRepoInspector

**Languages:** English | [Português (Brasil)](README.pt-BR.md)

[![Build & Tests](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml/badge.svg)](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/badge/coverage-%E2%89%A570%25-brightgreen)](.github/coverage-baseline.json)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Inspect and classify .NET projects, extracting architecture metadata for CI/CD, automation, and technical governance.**

> Status: early development. The repository is being bootstrapped and the public contracts described below are still subject to change.

## Why DotNetRepoInspector?

.NET repositories often contain a mix of Web applications, Workers, console applications, libraries, tests, multiple target frameworks, SDK constraints, and repository-level MSBuild configuration.

CI/CD platforms and engineering teams repeatedly need to rediscover that information with ad-hoc scripts. DotNetRepoInspector aims to provide one normalized, automation-friendly view of a repository based on evaluated .NET/MSBuild metadata.

The long-term goal is to support three related use cases:

1. **Inspect** — discover .NET projects and effective build metadata.
2. **Classify** — identify project roles such as Web, Worker, Console, Library, Test, and Unknown.
3. **Track** — optionally persist versioned inspection snapshots so teams can build technical evidence and historical views.

## Design principles

- **MSBuild is the source of truth.** Prefer evaluated project properties over raw `.csproj` XML parsing.
- **Zero configuration by default.** A useful inspection should require only a repository path.
- **Automation first.** Output must be deterministic, machine-readable, and suitable for CI/CD.
- **No source-code collection.** Inspection is focused on project/repository metadata, not application source code.
- **Storage is optional.** The inspector must work without a database or external service.
- **Provider agnostic.** GitHub Actions is one integration, not the core architecture.
- **Versioned contracts.** Machine-readable output should carry a schema version as the project evolves.

## Initial scope

The first usable version is expected to discover and expose:

- project path and name;
- project SDK;
- project type/classification;
- `TargetFramework` / `TargetFrameworks`;
- `OutputType`;
- test-project metadata;
- packability metadata;
- runtime identifiers when configured;
- `global.json` SDK configuration;
- resolved .NET SDK version;
- project-to-project references;
- repository and commit metadata when available.

Initial classifications:

- Web
- Worker
- Console
- Library
- Test
- Unknown

Additional subtypes such as Web API, Razor Pages, Blazor, Azure Functions, and other workloads may be added when they can be identified reliably without fragile filename conventions.

## Example output

The exact schema is not final yet, but the intended shape is similar to:

```json
{
  "schemaVersion": "1.0",
  "repository": {
    "name": "example/repository",
    "commit": "61f842a"
  },
  "dotnet": {
    "configuredSdk": "10.0.100",
    "resolvedSdk": "10.0.4xx"
  },
  "projects": [
    {
      "name": "Orders.Api",
      "path": "src/Orders.Api/Orders.Api.csproj",
      "type": "web",
      "sdk": "Microsoft.NET.Sdk.Web",
      "targetFrameworks": ["net10.0"],
      "isTestProject": false,
      "isPackable": false
    }
  ]
}
```

## Install as a .NET Tool

The CLI is packaged with NuGet package ID `DotNetRepoInspector` and tool command `dotnet-repo-inspect`. Tools whose command starts with `dotnet-` can be invoked through the .NET CLI without the prefix, so the public command is:

```bash
dotnet repo-inspect .
```

The package targets .NET 10. A compatible .NET runtime/SDK is required on the machine running the tool.

> Packaging and installation are validated in CI, but the package has not been published to NuGet.org yet. The commands below using the public feed apply once a release is published.

### Global installation

```bash
dotnet tool install --global DotNetRepoInspector
dotnet repo-inspect --help
```

Update later with:

```bash
dotnet tool update --global DotNetRepoInspector
```

### Local tool manifest

```bash
dotnet new tool-manifest
dotnet tool install DotNetRepoInspector
dotnet repo-inspect .
```

If the repository already has a tool manifest, skip `dotnet new tool-manifest`. Restore pinned tools with `dotnet tool restore` and update this tool with `dotnet tool update DotNetRepoInspector`.

### Build and install a local package

Contributors can validate the distributable package without publishing anything:

```bash
dotnet pack ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
  --configuration Release \
  --output ./artifacts/packages \
  -p:Version=0.0.0-local
dotnet tool install --global DotNetRepoInspector \
  --version 0.0.0-local \
  --add-source ./artifacts/packages
dotnet repo-inspect --version
```

The version can therefore be supplied by CI/release automation through `-p:Version=...` without editing the project file. See [the CLI documentation](docs/en/cli.md) for local-tool installation, update/uninstall commands, output behavior, and exit codes.

## Usage

Inspect the current repository and write JSON to stdout:

```bash
dotnet repo-inspect .
```

Save the inspection to a file:

```bash
dotnet repo-inspect . --output inspection.json
```

The direct executable name also works for a globally installed tool:

```bash
dotnet-repo-inspect .
```

### Optional HTTP snapshot persistence

Snapshot persistence is opt-in. Without `--sink`, the Inspector does not contact a persistence endpoint.

Send the canonical inspection snapshot to a consumer-owned HTTP/HTTPS endpoint:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

The HTTP sink sends a `POST` with the canonical `InspectionSnapshot` JSON and the snapshot key in the `Idempotency-Key` header. Retry is limited to transient transport/timeouts and HTTP `408`, `429`, `500`, `502`, `503`, and `504` responses.

Bearer authentication is intentionally supplied only through the process environment, never through a CLI argument:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-failure-mode fatal
```

`--sink-failure-mode non-fatal` is the default. Use `fatal` when delivery of the evidence is required for the pipeline; a persistence failure then returns exit code `5` after the inspection report has already been produced.

Never put sink tokens in `.dotnetrepoinspector.json`, the endpoint URL, committed scripts, or inspection JSON. See [the persistence documentation](docs/en/persistence.md) for timeout, cancellation, retry, idempotency, and secret-handling details.

### GitHub Actions

The repository includes a reusable composite Action that executes the same .NET Tool and engine as the CLI:

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

Useful outputs include `report-path`, `schema-version`, `inspector-version`, and `exit-code`. The Action preserves the CLI exit semantics and does not require a GitHub token or write permission for inspection of an existing checkout.

Optional HTTP persistence can be enabled with Action inputs. Secrets stay out of the CLI argument list:

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

> The Action implementation is validated in CI, but the public `v1` tag and matching NuGet package are not published yet. Publication is intentionally deferred to the release automation work.

See [the GitHub Action documentation](docs/en/github-action.md) for inputs, outputs, SDK requirements, persistence, package-source isolation, failure handling, and downstream consumption examples.

## Documentation

The documentation is organized by language and each language tree links only to files in the same language:

- [English documentation](docs/en/README.md)
- [Documentação em Português (Brasil)](docs/pt-BR/README.md)

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
Future adapters: additional sinks, policy/reporting
```

The Core owns normalized inspection models and classification rules. MSBuild-specific discovery/evaluation remains behind an adapter. `DotNetRepoInspector.Persistence` owns provider-independent snapshot/provenance contracts, while `DotNetRepoInspector.Persistence.Http` is the first concrete delivery adapter. Core and Engine remain independent from HTTP, database providers, and sink credentials.

## Repository structure

```text
.
├── .agents/skills/                    # Task-specific agent guidance
├── .github/action/                    # GitHub Action bootstrap/invocation glue
├── .vscode/                           # Portable VS Code recommendations/settings
├── action.yml                         # Reusable composite GitHub Action
├── docs/
│   ├── en/                            # English documentation
│   │   ├── architecture/              # Architecture documentation
│   │   ├── decisions/                 # Architectural decision records
│   │   └── schema/                    # JSON contract documentation and examples
│   └── pt-BR/                         # Portuguese (Brazil) documentation
│       ├── architecture/
│       ├── decisions/
│       └── schema/
├── src/
│   ├── DotNetRepoInspector.Core/              # Domain model, normalization, classification
│   ├── DotNetRepoInspector.Engine/            # End-to-end inspection orchestration
│   ├── DotNetRepoInspector.Git/               # Git repository metadata adapter
│   ├── DotNetRepoInspector.MSBuild/           # Project discovery and MSBuild evaluation
│   ├── DotNetRepoInspector.Persistence/       # Snapshot, provenance, sink abstractions
│   ├── DotNetRepoInspector.Persistence.Http/  # Built-in HTTP/webhook sink
│   └── DotNetRepoInspector.Cli/               # CLI, serialization, and delivery composition
├── tests/
│   ├── DotNetRepoInspector.Core.Tests/
│   ├── DotNetRepoInspector.Engine.Tests/
│   ├── DotNetRepoInspector.Git.Tests/
│   ├── DotNetRepoInspector.MSBuild.Tests/
│   ├── DotNetRepoInspector.Persistence.Tests/
│   ├── DotNetRepoInspector.Persistence.Http.Tests/
│   ├── DotNetRepoInspector.Cli.Tests/
│   └── Fixtures/                              # Synthetic .NET repository/project fixtures
├── AGENTS.md
├── LICENSE
├── README.md
├── README.pt-BR.md
├── Directory.Build.props
├── Directory.Packages.props
└── global.json
```

## Testing strategy

The inspection engine should be validated primarily with synthetic fixture repositories covering combinations such as:

- `Microsoft.NET.Sdk.Web`;
- `Microsoft.NET.Sdk.Worker`;
- executable and library output types;
- test projects;
- `Directory.Build.props` inheritance;
- multi-targeted projects;
- conditional MSBuild properties;
- project references;
- repositories with and without `global.json`.

Tests should verify **evaluated behavior**, not assumptions based only on filenames or raw XML layout. Persistence adapter tests use in-memory `HttpMessageHandler` implementations and do not depend on public infrastructure.

## Persistence and evidence

Persistence is optional and happens only after a usable `InspectionReport` exists. `InspectionSnapshotFactory` creates an attributable envelope containing repository/commit identity when available, UTC observation time, schema and Inspector version, a report digest, and a versioned idempotency key.

The generic publisher applies timeout/failure policy but does not know about HTTP or databases. The built-in HTTP adapter is selected explicitly by the delivery host and can send that snapshot to a consumer-owned endpoint without coupling Core or Engine to an infrastructure provider.

A persistence failure does not become an inspection diagnostic. Consumers can choose `non-fatal` delivery, which preserves normal inspection exit semantics, or `fatal`, which returns exit code `5` while leaving the already-produced report unchanged.

## Roadmap

- [ ] Bootstrap solution and projects
- [ ] Discover supported .NET project files
- [ ] Evaluate effective MSBuild properties
- [ ] Implement deterministic project classification
- [ ] Define and version the JSON contract
- [ ] Add fixture-based tests
- [x] Package the CLI as a .NET tool
- [x] Implement a reusable GitHub Action; public release/tagging remains pending
- [x] Add the first optional snapshot sink (HTTP/webhook)
- [ ] Explore policy/compliance checks over normalized inspection results

## License

DotNetRepoInspector is licensed under the [MIT License](LICENSE).

## Contributing

Contributions, bug reports, and design discussions are welcome while the project takes shape. Until contribution guidelines are formalized, prefer small, focused changes with tests that demonstrate the inspected repository behavior.
