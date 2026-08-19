# Real repository validation

**Languages:** English | [Português (Brasil)](../pt-BR/real-repository-validation.md)

Synthetic fixtures remain the primary regression suite for DotNetRepoInspector because they are small, deterministic, reviewable, and independent from external services. Real-repository validation complements those fixtures by exercising the Inspector against pinned public .NET repositories that contain combinations and organization styles that are impractical to reproduce in every local fixture.

## Separation from the standard test suite

Real-repository validation is intentionally isolated from the normal build/test workflow.

- The standard solution tests do not clone or fetch external repositories.
- After the Inspector dependencies have been restored, the standard test suite remains executable without Internet access.
- External validation runs in the separate `Validate real repositories` workflow or through the repository script explicitly.
- External repositories are not added as test-project dependencies or copied into the fixture tree.
- A failure caused only by GitHub/network availability does not change the semantics of the synthetic regression suite.

## Versioned sample

The validation manifest is [`../../.github/real-repositories/manifest.json`](../../.github/real-repositories/manifest.json). Every repository is pinned to a full commit SHA and every case contains explicit expectations.

| Repository | Pinned commit | Inspection root | Main scenarios |
| --- | --- | --- | --- |
| `MassTransit/Sample-Outbox` | `1ab8e66ebf96e5733e68c2f4d2201276f38ed9c5` | repository root | Web, Worker, library, project references, `net8.0` |
| `ardalis/CleanArchitecture` | `fbdc0951879f5e8dca1bebc273d4b28cb2934469` | `tests/Clean.Architecture.AspireTests` | test classification, ancestor `Directory.Build.props`, ancestor `global.json`, `net9.0`, reference outside the inspection root |
| `App-vNext/Polly` | `47e3b412e8c3b7e6db1629acd98f3e3b6b529d6c` | `src/Polly.Core` | multi-target library, imported `Directory.Build.props`, exact SDK selection |

Changing one of these commits is a reviewable compatibility-baseline change. The manifest must never follow a branch or tag implicitly.

## Reproducibility and safety

The harness deliberately limits what external repositories can cause the validation job to execute.

- Repository URLs must be public `https://github.com/<owner>/<repository>.git` URLs.
- Commits must be lowercase full 40-character Git SHAs.
- Each repository is fetched at the pinned commit and checked out with detached `HEAD`.
- Submodules are not initialized.
- The manifest cannot define arbitrary shell commands or preparation steps.
- The harness does **not** run restore, build, tests, package scripts, or repository-specific commands for external repositories.
- Only DotNetRepoInspector is restored and built by the workflow.
- The validation environment installs the .NET SDKs needed by the current pinned scenarios (`8.0.424`, `9.0.317`, and `10.0.400`).
- Reports include the observed Git commit, and the harness fails when it differs from the pinned commit.

DotNetRepoInspector still invokes MSBuild evaluation as part of its normal inspection boundary. The external harness adds no additional project execution mechanism beyond that product behavior.

## Expectations

The manifest can assert stable, normalized facts such as:

- CLI exit code;
- minimum discovered project count;
- repository commit SHA;
- configured and resolved SDK version;
- project path;
- project classification;
- `IsTestProject`;
- project SDK identity;
- target frameworks;
- normalized `ProjectReference` paths.

Assertions are intentionally limited to facts that are meaningful to the Inspector's public behavior. Machine-specific absolute paths and transient output are not baselined.

## Running locally

External validation requires network access and the SDK versions required by the manifest.

Build the Inspector first:

```bash
dotnet restore ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj
dotnet build ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj --configuration Release --no-restore
```

Then run the harness with PowerShell 7 or later:

```powershell
./.github/scripts/validate_real_repositories.ps1
```

The script writes one `inspection.json` and `stderr.txt` per scenario plus a consolidated `artifacts/real-repositories/summary.md`.

After the workflow is present on the default branch, `Validate real repositories` can also be started manually with `workflow_dispatch`. Pull requests that change the manifest, harness script, or workflow run the external validation automatically.

## Bug reproduction policy

A real repository is a discovery source, not a permanent substitute for an isolated regression test. When an external validation exposes an Inspector defect:

1. Confirm the divergence against the pinned external commit.
2. Reduce the relevant MSBuild/project shape to the smallest synthetic local fixture that reproduces the defect.
3. Add a regression test that fails against that local fixture.
4. Fix the Inspector using the local deterministic reproduction.
5. Re-run the pinned real-repository case and update expectations only when the intended public behavior changed.

Do not fix a production rule solely around the name, path, package, or incidental layout of a specific external repository.

## Known limitations

This validation intentionally does not prove every possible .NET repository shape.

- Package-generated `.props`/`.targets` that exist only after restoring an external repository may not participate because external restore is deliberately not executed.
- Repositories requiring custom workloads, proprietary SDKs, authenticated package feeds, submodules, or nonstandard preparation can fail evaluation and should be documented rather than silently accommodated.
- The pinned sample proves compatibility with known repository states, not with the current tip of their default branches.
- The separate external job depends on public GitHub availability, while the main synthetic test suite does not.
- The sample should remain small enough to be reviewable and operationally inexpensive; add a repository only when it contributes a distinct inspection shape.
