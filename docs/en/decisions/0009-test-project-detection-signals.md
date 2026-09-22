# ADR 0009: Detect test projects from VSTest and Microsoft.Testing.Platform signals

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0009-test-project-detection-signals.md)

- **Status:** Accepted
- **Date:** 2026-09-22
- **Decision owners:** DotNetRepoInspector maintainers

## Context

The production classifier currently recognizes a test project only when the evaluated `IsTestProject` property is `true`. That is correct for the VSTest model, but it is not a complete definition of a modern .NET test application.

Microsoft documents `IsTestProject` as the VSTest-oriented property set by `Microsoft.NET.Test.Sdk`. Microsoft.Testing.Platform (MTP) has a separate evaluated property, `IsTestingPlatformApplication`, which identifies a project as an MTP application and can be true while `IsTestProject` is not true.

DotNetRepoInspector also evaluates projects without running restore. In that mode a direct `PackageReference` can still be observed even when generated NuGet imports that would normally set an MSBuild property are absent. The current classifier therefore has two reproducible false-negative shapes:

- an MTP test application with `IsTestingPlatformApplication=true`, `IsTestProject != true`, and executable output is currently classified as `console`;
- a project with a direct `Microsoft.NET.Test.Sdk` reference but missing `IsTestProject` is currently classified from its generic output shape.

The classifier must remain deterministic, infrastructure-agnostic in Core, and independent of project names, directory names, and source-code inspection.

## Decision

### Signal strength

| Strength | Structural signal | Decision |
| --- | --- | --- |
| Strong | effective `IsTestProject == true` | Authoritative VSTest test-project signal. |
| Strong | effective `IsTestingPlatformApplication == true` | Authoritative MTP test-application signal. |
| Strong | declared project SDK `MSTest.Sdk` | Explicit test-specific project SDK; classify as test. |
| Moderate | direct/evaluated `PackageReference` to `Microsoft.NET.Test.Sdk` while `IsTestProject` is missing | Fallback test signal because package imports may be unavailable to no-restore evaluation. |
| Moderate, supporting only | direct/evaluated `PackageReference` to `Microsoft.Testing.Platform.MSBuild` | Evidence of MTP integration, but not sufficient alone because its integration can flow transitively and non-test consumers can explicitly disable application behavior. |
| Weak, supporting only | `Microsoft.Testing.Platform` package presence, framework packages, runner-selection properties, or repository-level MTP runner selection | Contextual evidence only; do not classify as test from these signals alone. |
| Rejected | project name, directory name, `.Tests` suffix, source-code attributes/types | Non-structural or heuristic evidence; never authoritative. |

### Explicit false and ambiguity

`IsTestProject=false` is an explicit negative **VSTest** signal, not a universal statement that the project cannot be a test application.

Therefore:

- `IsTestingPlatformApplication=true` may still classify the project as `test` even when `IsTestProject=false`;
- declared `MSTest.Sdk` remains a strong test signal;
- a `Microsoft.NET.Test.Sdk` package reference **must not** override explicit `IsTestProject=false` by itself;
- `Microsoft.Testing.Platform.MSBuild` or `Microsoft.Testing.Platform` package presence alone never overrides explicit negative/ambiguous facts.

The `ExplicitFalseConflict` fixture records this conservative boundary.

### Implemented precedence

Issue #151 implements the following order before the existing Web/Worker/Console/Library rules:

1. `IsTestingPlatformApplication == true` -> `test`, high confidence.
2. `IsTestProject == true` -> `test`, high confidence.
3. declared `MSTest.Sdk` -> `test`, high confidence.
4. when `IsTestProject is null`, direct/evaluated `Microsoft.NET.Test.Sdk` -> `test`, medium confidence.
5. when `IsTestProject == false`, do not promote from `Microsoft.NET.Test.Sdk` package presence alone.
6. package-only MTP hints remain non-authoritative.
7. continue with the existing specialized-SDK conflict, Web, Worker, Console, Library, and Unknown precedence.

A strong test signal continues to win over executable output and specialized workload SDKs, matching the existing rule that test semantics have higher precedence than Web/Worker/Console shape.

### Implemented normalized facts

The implementation collects or reuses these normalized classification facts:

- existing `bool? IsTestProject`;
- new effective `bool? IsTestingPlatformApplication`;
- existing declared project SDK names, including exact/case-insensitive detection of `MSTest.Sdk`;
- new evaluated `PackageReference` identities, with exact/case-insensitive detection of:
  - `Microsoft.NET.Test.Sdk`;
  - `Microsoft.Testing.Platform.MSBuild` as supporting evidence only.

The MSBuild adapter owns collection/normalization. Core receives only normalized values and remains free of an MSBuild dependency.

Suggested stable classification signals are:

- `property:IsTestProject=true`;
- `property:IsTestingPlatformApplication=true`;
- `sdk:MSTest.Sdk`;
- `package:Microsoft.NET.Test.Sdk`.

The public `projects[].isTestProject` field must keep its current meaning: the evaluated `IsTestProject` MSBuild fact. The implementation does not redefine that field to mean the broader derived classification.

## Fixtures and evidence

The research fixtures live under `tests/Fixtures/TestProjectSignals` so they do not alter the existing `ProjectKinds` package/action smoke baseline before #151:

- `MtpApplication` reproduces an MTP application with `IsTestingPlatformApplication=true` and missing `IsTestProject`;
- `TestSdkFallback` exposes a direct `Microsoft.NET.Test.Sdk` reference while `IsTestProject` is missing;
- `ExplicitFalseConflict` combines `IsTestProject=false` with the package hint and proves the ambiguity boundary.

`TestProjectSignalClassificationTests` verifies that the approved signals are observable through the existing MSBuild evaluation infrastructure and now produce the intended production classifications.

## Consequences

The follow-up implementation can support both VSTest and MTP without name-based heuristics and without making Core aware of MSBuild. It also preserves the distinction between an observed MSBuild property and a derived classification.

Collecting `PackageReference` items adds a bounded amount of evaluation data. The fallback is intentionally narrower than "any test framework package", reducing false positives in shared test utilities or projects that merely reference testing libraries.

Issue #151 converted the research expectations into production classification assertions while preserving the ambiguity fixture.

## Alternatives considered

- **Use project/directory names such as `.Tests`:** rejected as non-structural and easy to misclassify.
- **Treat any test framework package as authoritative:** rejected because assertion/framework packages can appear in support libraries.
- **Treat `Microsoft.Testing.Platform.MSBuild` package presence alone as authoritative:** rejected because MTP integration can be transitive and can be disabled for non-test consumers.
- **Redefine public `isTestProject` to the broader derived result:** rejected because it would conflate an observed MSBuild fact with classifier output.
- **Run restore before classification:** rejected for this issue because it changes the inspection side-effect/performance boundary and is unnecessary when structural fallback facts are available.

## References

- Microsoft Learn — MSBuild properties for Microsoft.NET.Sdk: https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props
- Microsoft Learn — Microsoft.Testing.Platform overview: https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro
- Issue #96: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/96
- Implementation #151: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/151
