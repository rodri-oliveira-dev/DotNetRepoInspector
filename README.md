# DotNetRepoInspector

**Languages:** English | [Português (Brasil)](README.pt-BR.md)

[![CI](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml/badge.svg)](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml)

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

## Planned usage

### CLI

```bash
dotnet repo-inspect .
```

### GitHub Actions

```yaml
- name: Inspect .NET repository
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
```

The Action integration is planned; this example documents the intended consumer experience rather than an already published release.

## Documentation

The documentation is organized by language and each language tree links only to files in the same language:

- [English documentation](docs/en/README.md)
- [Documentação em Português (Brasil)](docs/pt-BR/README.md)

## Architecture

```text
Repository
    |
    v
DotNetRepoInspector.MSBuild
    |
    v
DotNetRepoInspector.Core
    |
    +------------------+
    |                  |
    v                  v
CLI / JSON        Future integrations
                  (GitHub Action, sinks,
                   policy/reporting)
```

The Core owns normalized inspection models and classification rules. MSBuild-specific discovery/evaluation remains behind an adapter. Consumers such as the CLI, GitHub Action, and future persistence sinks should depend on the normalized model rather than duplicate repository-detection logic.

## Repository structure

```text
.
├── .agents/skills/                    # Task-specific agent guidance
├── .vscode/                           # Portable VS Code recommendations/settings
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
│   ├── DotNetRepoInspector.Core/      # Domain model, normalization, classification
│   ├── DotNetRepoInspector.MSBuild/   # Project discovery and MSBuild evaluation
│   └── DotNetRepoInspector.Cli/       # CLI and serialization boundary
├── tests/
│   ├── DotNetRepoInspector.Core.Tests/
│   ├── DotNetRepoInspector.MSBuild.Tests/
│   ├── DotNetRepoInspector.Cli.Tests/
│   └── Fixtures/                      # Synthetic .NET repository/project fixtures
├── AGENTS.md
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

Tests should verify **evaluated behavior**, not assumptions based only on filenames or raw XML layout.

## Persistence and evidence

Persistence is intentionally not required by the inspector. A future sink abstraction can allow inspection snapshots to be sent to a database, file, object storage, or HTTP endpoint.

A stored snapshot should be attributable to the inspected repository state, ideally including repository identity, branch/ref, commit SHA, timestamp, schema version, and inspector version. This makes historical architecture evidence reproducible without coupling the Core to a specific database.

## Roadmap

- [ ] Bootstrap solution and projects
- [ ] Discover supported .NET project files
- [ ] Evaluate effective MSBuild properties
- [ ] Implement deterministic project classification
- [ ] Define and version the JSON contract
- [ ] Add fixture-based tests
- [ ] Package the CLI as a .NET tool
- [ ] Publish a reusable GitHub Action
- [ ] Add optional snapshot sinks
- [ ] Explore policy/compliance checks over normalized inspection results

## Contributing

Contributions, bug reports, and design discussions are welcome while the project takes shape. Until contribution guidelines are formalized, prefer small, focused changes with tests that demonstrate the inspected repository behavior.
