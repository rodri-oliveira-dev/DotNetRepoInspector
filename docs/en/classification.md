# Project classification

**Languages:** English | [Português (Brasil)](../pt-BR/classification.md)

DotNetRepoInspector classifies projects from evaluated structural facts instead of project names, directory names, or source-code inspection.

The classifications are `web`, `worker`, `console`, `library`, `test`, and `unknown`.

## Inputs

The classifier consumes normalized facts produced by the inspection pipeline:

- declared project SDK names;
- effective `OutputType`;
- effective `IsTestProject`;
- effective `IsTestingPlatformApplication`;
- evaluated `PackageReference` identities used by approved classification rules.

The Core classifier has no dependency on MSBuild. `MsBuildProjectClassificationAdapter` maps `MsBuildProjectFacts` into the Core input model.

The public `projects[].isTestProject` field keeps its original meaning: it is the evaluated MSBuild `IsTestProject` fact. A project can therefore be classified as `test` from another approved signal while `isTestProject` is `false` or absent.

## Test-project signals

Test-project detection follows [ADR 0009](decisions/0009-test-project-detection-signals.md):

| Evidence | Signal | Confidence | Notes |
| --- | --- | --- | --- |
| `IsTestingPlatformApplication == true` | `property:IsTestingPlatformApplication=true` | `high` | Authoritative Microsoft.Testing.Platform application signal. |
| `IsTestProject == true` | `property:IsTestProject=true` | `high` | Existing authoritative VSTest signal. |
| declared `MSTest.Sdk` | `sdk:MSTest.Sdk` | `high` | Explicit test-specific project SDK. |
| evaluated `Microsoft.NET.Test.Sdk` package with missing `IsTestProject` | `package:Microsoft.NET.Test.Sdk` | `medium` | Conservative fallback for no-restore/package-import gaps. |

An explicit `IsTestProject=false` prevents the package-only `Microsoft.NET.Test.Sdk` fallback from promoting the project to `test`. It does not negate independent strong signals such as `IsTestingPlatformApplication=true` or declared `MSTest.Sdk`.

Package-only MTP hints such as `Microsoft.Testing.Platform.MSBuild`, generic test-framework packages, repository-level runner selection, names, and paths are not authoritative by themselves.

## Precedence and conflict handling

Rules are evaluated in this order:

1. `IsTestingPlatformApplication == true` -> `test`.
2. `IsTestProject == true` -> `test`.
3. declared `MSTest.Sdk` -> `test`.
4. when `IsTestProject` is missing, evaluated `Microsoft.NET.Test.Sdk` -> `test`.
5. both `Microsoft.NET.Sdk.Web` and `Microsoft.NET.Sdk.Worker` -> `unknown` because the specialized SDK signals conflict.
6. `Microsoft.NET.Sdk.Web` -> `web`.
7. `Microsoft.NET.Sdk.Worker` -> `worker`.
8. `OutputType == Exe` -> `console` when no more specific signal matched.
9. `OutputType == Library` -> `library` when no more specific signal matched.
10. otherwise -> `unknown`.

A strong test signal therefore remains `test` even when the project is executable or declares a specialized workload SDK. Conflicting Web/Worker SDK declarations produce `unknown` only when no approved test signal has already matched.

`WinExe` is not classified as `console` because it can represent desktop application models outside the current classification vocabulary.

## Signals and confidence

| Classification | Structural evidence | Signal | Confidence |
| --- | --- | --- | --- |
| `test` | MTP application | `property:IsTestingPlatformApplication=true` | `high` |
| `test` | `IsTestProject == true` | `property:IsTestProject=true` | `high` |
| `test` | declared `MSTest.Sdk` | `sdk:MSTest.Sdk` | `high` |
| `test` | fallback `Microsoft.NET.Test.Sdk` with missing `IsTestProject` | `package:Microsoft.NET.Test.Sdk` | `medium` |
| `web` | declared `Microsoft.NET.Sdk.Web` | `sdk:Microsoft.NET.Sdk.Web` | `high` |
| `worker` | declared `Microsoft.NET.Sdk.Worker` | `sdk:Microsoft.NET.Sdk.Worker` | `high` |
| `console` | effective `OutputType == Exe` | `property:OutputType=Exe` | `medium` |
| `library` | effective `OutputType == Library` | `property:OutputType=Library` | `high` |
| `unknown` | insufficient or conflicting evidence | observed facts/conflict signal when available | omitted |

## Deliberately excluded heuristics

The engine does not classify from:

- suffixes such as `.Api`, `.Worker`, or `.Tests`;
- project or directory names;
- `Microsoft.Extensions.Hosting` alone;
- `Microsoft.Testing.Platform.MSBuild` or generic test-framework package presence alone;
- repository-level test-runner selection alone;
- the presence of `BackgroundService`, test attributes, or other source-code types;
- arbitrary raw MSBuild properties that have not been promoted to normalized classification facts.

New signals should only be added when the inspection model can collect them explicitly and their precedence is deterministic.
