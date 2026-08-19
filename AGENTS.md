# AGENTS.md

## Purpose

DotNetRepoInspector is an open-source .NET repository inspection tool. Its core responsibility is to discover .NET projects, evaluate effective MSBuild metadata, normalize that information, and classify projects for automation, CI/CD, and technical-governance use cases.

Repository artifacts and technical identifiers should be written in English unless a task explicitly requires otherwise. Public documentation is maintained in both English and Portuguese (Brazil) as described in the documentation rules below. User-facing conversation may follow the user's language.

## Sources of truth

Read only what is relevant to the task. Prefer, in this order when applicable:

1. `README.md`
2. `docs/README.md`
3. `docs/en/architecture/`
4. `docs/en/decisions/`
5. the corresponding `docs/pt-BR/` document when working on public documentation
6. `Directory.Build.props`
7. `Directory.Packages.props`
8. `.editorconfig`
9. `global.json`
10. the affected project and its closest tests
11. synthetic repositories under `tests/Fixtures/` when inspection behavior is involved

Do not load the whole repository indiscriminately.

## Architecture boundaries

The intended dependency direction is:

```text
DotNetRepoInspector.Cli
          |
          v
DotNetRepoInspector.MSBuild
          |
          v
DotNetRepoInspector.Core
```

- `Core` owns normalized models, classification concepts, result contracts, and implementation-agnostic rules.
- `MSBuild` owns repository/project discovery and effective MSBuild evaluation.
- `Cli` owns command-line UX, process exit behavior, serialization, and composition.
- Future GitHub Action, persistence, HTTP, database, or policy integrations must remain adapters around the normalized inspection model.

The Core must not depend on GitHub APIs, database providers, CI environment variables, or concrete MSBuild process execution.

## Inspection rules

- Treat evaluated MSBuild state as the primary source of project metadata.
- Do not rely on project names, directory names, or the presence of `Program.cs` as the primary classification mechanism.
- Distinguish target framework information from the resolved .NET SDK version.
- Account for `Directory.Build.props`, imported props/targets, conditional properties, and multi-targeting.
- Keep classification deterministic and document precedence when multiple signals exist.
- Prefer `Unknown` over a confident but unsupported classification.
- Do not read or persist application source code merely to produce repository inventory metadata.
- Do not collect secrets, environment-variable values, credentials, connection strings, or arbitrary file contents.
- Inspection should be read-only with respect to the inspected repository.
- Avoid network access by default. Any future enrichment requiring external access must be explicit.

## Public contracts

Machine-readable output is a product contract.

- Include a schema version once the JSON contract is introduced.
- Avoid accidental field renames or semantic changes.
- Add tests for serialization and backward-compatibility expectations when the contract stabilizes.
- Repository/commit identity should be metadata, not a substitute for project identity.
- Optional persistence must store attributable snapshots rather than silently overwrite the only known state.

## Tests

Inspection behavior should be tested using synthetic fixture repositories rather than the developer's local machine state wherever possible.

Fixtures should cover relevant combinations of SDK, `OutputType`, target frameworks, inherited properties, conditional MSBuild evaluation, project references, test-project indicators, and `global.json` behavior.

A fixture must be intentionally small and should not contain real application source. Avoid making tests depend on external repositories or network services.

`tests/Fixtures/Directory.Build.props` intentionally stops fixture projects from inheriting the repository's own build defaults. Preserve that isolation.

## Dependencies

The repository uses Central Package Management.

- Add package versions only to `Directory.Packages.props`.
- Do not add `Version=` to individual `PackageReference` items.
- Introduce dependencies only when they provide concrete value.
- Prefer .NET/MSBuild-supported mechanisms over an additional parsing library when the platform already exposes the needed information reliably.

## Documentation and ADRs

Public documentation is bilingual and must remain synchronized between English and Portuguese (Brazil).

- `README.md` is the English project entry point; `README.pt-BR.md` is its Portuguese counterpart.
- English documentation lives under `docs/en/`; Portuguese documentation lives under `docs/pt-BR/`.
- Whenever a public documentation file is **created, modified, renamed, moved, or deleted**, apply the equivalent change to both language versions in the same change set.
- Language-specific documents should keep equivalent relative paths below `docs/en/` and `docs/pt-BR/` so the two trees remain structurally aligned.
- When an equivalent document exists in the current language, links inside that document must point to files from the same language tree rather than crossing to the other locale.
- `docs/README.md` is the language-selection entry point and may link to both language trees.
- Preserve technical identifiers, type names, API names, commands, configuration keys, diagnostic codes, JSON property names, and other machine-readable values when translating prose.
- Machine-readable artifacts mirrored in both trees, such as JSON schemas and canonical JSON examples, must remain semantically identical unless the contract itself changes.
- If the English and Portuguese versions diverge, treat the English version as the semantic source of truth and update the Portuguese version in the same change.

Update the root README files only as project entry points. Put detailed architecture documentation under `docs/en/architecture/` and `docs/pt-BR/architecture/`.

Create matching ADRs under `docs/en/decisions/` and `docs/pt-BR/decisions/` when changing a durable architectural decision such as:

- raw XML vs evaluated MSBuild;
- process invocation vs in-process MSBuild APIs;
- project-classification precedence;
- JSON schema compatibility strategy;
- GitHub Action packaging strategy;
- persistence/sink abstraction;
- plugin/extensibility model.

Do not rewrite historical ADRs to match later decisions. Supersede them explicitly in both language versions.

## Skills

Before specialized work, inspect `.agents/skills/` and use only skills whose description matches the task. The project-specific `dotnet-repository-inspection` skill should guide changes to discovery, MSBuild evaluation, classification, and inspection contracts.

In case of conflict, this `AGENTS.md` takes precedence over repository skills.

## Validation

Use validation proportional to the change. Once the solution/projects exist, the expected baseline is:

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

For changes to inspection behavior, run the closest fixture-based tests first. For changes to public JSON output, validate serialization tests. For workflow edits, use the GitHub workflow skill and `actionlint` when available.

Clearly report any validation that could not be executed and why.

## Git and change discipline

- Prefer small, cohesive changes.
- Do not mix broad refactoring with functional behavior changes without a clear reason.
- Do not weaken tests or analyzers merely to make a change pass.
- Do not introduce secrets.
- Use Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `ci:`, or `chore:`.
- After repository bootstrap, do not make ordinary development changes directly on `main`; use a focused working branch.
- Do not publish releases, packages, or deploy artifacts unless explicitly requested.
