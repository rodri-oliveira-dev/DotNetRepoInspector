# Contributing to DotNetRepoInspector

**Languages:** English | [Português (Brasil)](CONTRIBUTING.pt-BR.md)

Thank you for considering a contribution. DotNetRepoInspector is automation-oriented infrastructure, so changes should be small, reproducible, and explicit about any impact on public contracts.

By participating in this project, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). For suspected vulnerabilities, do **not** open a public issue; follow [SECURITY.md](SECURITY.md).

## Before you start

For bugs and focused improvements, an issue is recommended when it helps establish a reproducible problem or expected behavior. For broad architectural changes, new public contracts, new persistence strategies, or behavior that may affect compatibility, open or reference an issue before investing in a large implementation.

Keep pull requests focused. Avoid mixing unrelated refactors with behavior changes.

## Development environment

You need:

- Git;
- a .NET 10 SDK compatible with [`global.json`](global.json) (`10.0.100` with `latestFeature` roll-forward);
- Python 3 only if you want to run the same local coverage summary script used by CI.

No database, cloud account, public HTTP service, or GitHub token is required for the normal build/test loop.

Clone and verify the selected SDK:

```bash
git clone https://github.com/rodri-oliveira-dev/DotNetRepoInspector.git
cd DotNetRepoInspector
dotnet --version
```

## Build and test locally

The baseline validation is:

```bash
dotnet restore ./DotNetRepoInspector.slnx

dotnet format ./DotNetRepoInspector.slnx \
  --verify-no-changes \
  --no-restore \
  --severity warn

dotnet build ./DotNetRepoInspector.slnx \
  --configuration Release \
  --no-restore \
  --warnaserror \
  -p:RunAnalyzers=true

dotnet test \
  --solution ./DotNetRepoInspector.slnx \
  --configuration Release \
  --no-build \
  --no-restore
```

To reproduce the CI coverage run, use:

```bash
dotnet test \
  --solution ./DotNetRepoInspector.slnx \
  --configuration Release \
  --no-build \
  --no-restore \
  --results-directory ./artifacts/test-results \
  -- \
  --report-trx \
  --coverlet \
  --coverlet-output-format cobertura

python ./.github/scripts/coverage_summary.py \
  --reports "artifacts/test-results/**/*cobertura*.xml" \
  --baseline ./.github/coverage-baseline.json
```

Run the closest tests first while iterating, then run the repository baseline before opening a pull request.

## Project conventions

Follow [`AGENTS.md`](AGENTS.md) for architecture boundaries, inspection rules, test strategy, public contracts, dependency management, documentation synchronization, and validation expectations.

Important contribution rules include:

- evaluated MSBuild metadata is the primary source of truth;
- `Core` must remain infrastructure-independent;
- inspection remains read-only and avoids network access by default;
- do not collect or log secrets, credentials, environment values, or arbitrary source contents;
- dependencies use Central Package Management;
- public documentation under the English and Portuguese trees must remain synchronized;
- use Conventional Commit prefixes such as `feat:`, `fix:`, `test:`, `docs:`, `ci:`, `refactor:`, or `chore:`.

## Adding or changing project classifications

Classification changes require reproducible evidence, not naming intuition.

A contribution that adds a classification signal, changes precedence, or fixes a classification bug must:

1. add or update a **minimal synthetic fixture** under `tests/Fixtures/` that reproduces the relevant evaluated MSBuild state;
2. add a regression test that fails without the proposed behavior;
3. prefer evaluated properties/items/imports over project names, directory names, or source-file conventions;
4. preserve deterministic precedence and prefer `Unknown` when the evidence is insufficient;
5. update classification/schema/diagnostic documentation in both languages when public behavior changes.

A real public repository may be used to discover or explain a bug, but the permanent regression test must be distilled into a small local fixture. Do not make the normal test suite depend on network access or mutable external repositories.

## Adding or changing agent skills

Repository skills live under `.agents/skills/<skill-name>/`.

A new or materially changed skill should:

- use a focused, reusable scope instead of becoming a catch-all instruction set;
- use a kebab-case directory name and a `SKILL.md` with accurate `name` and `description` front matter;
- state clearly when the skill should and should not be used;
- remain subordinate to [`AGENTS.md`](AGENTS.md) and avoid duplicating or contradicting repository-wide rules;
- avoid user-specific information, credentials, private organization details, or environment-specific secrets;
- include validation/completion guidance appropriate to its scope;
- update `.agents/skills/THIRD-PARTY-NOTICES.md` when the skill copies or adapts third-party material that requires attribution or notice preservation.

If a skill changes CI, release, security, or public-contract behavior, the corresponding repository documentation and tests/gates remain authoritative; a skill must not silently redefine product behavior.

## Public contracts and compatibility

Treat JSON schema, diagnostics, CLI flags/exit codes, .NET Tool packaging, GitHub Action inputs/outputs, and persistence envelope semantics as product contracts.

When a contribution changes one of these surfaces:

- call out the compatibility impact in the pull request;
- add or update contract tests;
- update the relevant English and Portuguese documentation together;
- prefer additive changes within an existing schema major version;
- use an ADR when making a durable architectural decision.

## Documentation

`README.md` and `README.pt-BR.md` are project entry points. Detailed public documentation lives under `docs/en/` and `docs/pt-BR/` with equivalent relative paths.

When changing public documentation, update both languages in the same pull request. Technical identifiers, commands, JSON properties, diagnostic codes, and API names should not be translated.

## Pull requests

Before requesting review:

- make sure the change is scoped to one concern;
- run formatting, build/analyzers, and relevant tests;
- add fixtures/regression tests for inspection behavior changes;
- update documentation and public-contract tests when applicable;
- confirm no secrets, credentials, generated build outputs, or unrelated files are included.

The pull request template is intentionally conditional: check the items that apply and explain anything that could not be validated locally.

## Security and sensitive reports

Do not include real credentials, private repository content, exploit secrets, authorization headers, connection strings, or customer data in issues, fixtures, tests, logs, or pull requests.

For security vulnerabilities, follow [SECURITY.md](SECURITY.md) and use a private reporting path instead of public issue templates.

## License

By submitting a contribution, you agree that your contribution will be licensed under the project's [MIT License](LICENSE).
