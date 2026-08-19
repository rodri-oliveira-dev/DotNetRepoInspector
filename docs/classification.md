# Project classification

DotNetRepoInspector classifies projects from evaluated structural facts instead of project names, directory names, or source-code inspection.

The initial classifications are `web`, `worker`, `console`, `library`, `test`, and `unknown`.

## Inputs

The classifier currently consumes only facts already normalized by the inspection pipeline:

- declared project SDK names;
- effective `OutputType`;
- effective `IsTestProject`.

The Core classifier has no dependency on MSBuild. `MsBuildProjectClassificationAdapter` maps `MsBuildProjectFacts` into the Core input model.

## Precedence and conflict handling

Rules are evaluated in this order:

1. `IsTestProject == true` -> `test`.
2. both `Microsoft.NET.Sdk.Web` and `Microsoft.NET.Sdk.Worker` -> `unknown` because the specialized SDK signals conflict.
3. `Microsoft.NET.Sdk.Web` -> `web`.
4. `Microsoft.NET.Sdk.Worker` -> `worker`.
5. `OutputType == Exe` -> `console` when no specialized SDK matched.
6. `OutputType == Library` -> `library` when no specialized SDK matched.
7. otherwise -> `unknown`.

A test project therefore remains `test` even if it is executable or declares a specialized SDK. Conflicting Web/Worker SDK declarations intentionally produce `unknown` instead of relying on arbitrary ordering.

`WinExe` is not classified as `console` because it can represent desktop application models that are outside the initial classification vocabulary.

## Signals and confidence

| Classification | Structural evidence | Signal | Confidence |
| --- | --- | --- | --- |
| `test` | `IsTestProject == true` | `property:IsTestProject=true` | `high` |
| `web` | declared `Microsoft.NET.Sdk.Web` | `sdk:Microsoft.NET.Sdk.Web` | `high` |
| `worker` | declared `Microsoft.NET.Sdk.Worker` | `sdk:Microsoft.NET.Sdk.Worker` | `high` |
| `console` | effective `OutputType == Exe` | `property:OutputType=Exe` | `medium` |
| `library` | effective `OutputType == Library` | `property:OutputType=Library` | `high` |
| `unknown` | insufficient or conflicting evidence | observed facts/conflict signal when available | omitted |

## Deliberately excluded heuristics

The initial engine does not classify from:

- suffixes such as `.Api`, `.Worker`, or `.Tests`;
- project or directory names;
- `Microsoft.Extensions.Hosting` alone;
- the presence of `BackgroundService` or other source-code types;
- arbitrary raw MSBuild properties that have not been promoted to normalized classification facts.

These signals may be considered later only if the inspection model collects them explicitly and the rule can be documented with deterministic precedence.
