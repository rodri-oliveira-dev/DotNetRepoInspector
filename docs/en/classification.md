# Project classification

**Languages:** English | [Português (Brasil)](../pt-BR/classification.md)

DotNetRepoInspector classifies projects from evaluated structural facts instead of project names, directory names, or source-code inspection.

The classifications are `web`, `worker`, `console`, `library`, `test`, and `unknown`.

`projects[].classification.subtype` is a separate optional refinement of the base classification kind. The current classifier does not populate concrete subtypes; the field stays absent unless a future approved rule provides explicit subtype evidence.

## Web API subtype: intentionally unsupported

Web API is **not** currently emitted as a subtype. The inspection model does not expose a project-level structural fact that uniquely distinguishes ASP.NET Core Web API projects from MVC, Razor Pages, Blazor, or mixed Web applications.

The decision is documented in [ADR 0011](decisions/0011-web-api-subtype-detection.md). The evaluated candidates are deliberately rejected as subtype rules:

| Candidate evidence | Why it is insufficient |
| --- | --- |
| declared `Microsoft.NET.Sdk.Web` | Authoritative for base `kind = web`, but shared by multiple ASP.NET Core application models. |
| effective `OutputType == Exe` | Common to modern ASP.NET Core hosts and not API-specific. |
| packages such as `Microsoft.AspNetCore.OpenApi` or `Swashbuckle.AspNetCore` | Optional API tooling, removable from valid APIs, and usable by mixed/non-API Web applications. |
| Razor-related properties/items | They can prove Razor support, but cannot prove that API endpoints are absent; application models can coexist. |
| launch profiles such as a `swagger` launch URL | Optional tooling configuration outside the normalized evaluated classification facts. |
| source markers such as `MapGet`, `[ApiController]`, or `ControllerBase` | Would require source/semantic analysis, which is outside the subtype boundary for this issue. |

Consequently, a project classified as `web` keeps `classification.subtype` absent even when it has common API-oriented package hints. `classification.confidence` continues to describe the base `web` classification; no subtype confidence is fabricated.

## Inputs

The classifier consumes normalized facts produced by the inspection pipeline:

- declared project SDK names;
- effective `OutputType`;
- effective `IsTestProject`;
- effective `IsTestingPlatformApplication`;
- effective `UsingMicrosoftNETSdkWorker`;
- evaluated `PackageReference` identities used by approved classification rules.

The Core classifier has no dependency on MSBuild. `MsBuildProjectClassificationAdapter` maps `MsBuildProjectFacts` into the Core input model.

For multi-targeted projects, classification facts are evaluated in each MSBuild inner build. Worker properties and package references are merged across target frameworks, while a service-lifetime package is paired with `OutputType == Exe` only when both facts occur in the same target framework.

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
5. `Microsoft.NET.Sdk.Web` plus a strong Worker signal (`Microsoft.NET.Sdk.Worker` or `UsingMicrosoftNETSdkWorker == true`) -> `unknown` because the workload signals conflict.
6. `Microsoft.NET.Sdk.Web` -> `web`; service-lifetime package hints do not override Web.
7. `Microsoft.NET.Sdk.Worker` -> `worker`.
8. `UsingMicrosoftNETSdkWorker == true` -> `worker`.
9. `OutputType == Exe` plus `Microsoft.Extensions.Hosting.Systemd` or `Microsoft.Extensions.Hosting.WindowsServices` -> `worker`.
10. `OutputType == Exe` -> `console` when no more specific signal matched.
11. `OutputType == Library` -> `library` when no more specific signal matched.
12. otherwise -> `unknown`.

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
| `worker` | explicit Worker opt-in property | `property:UsingMicrosoftNETSdkWorker=true` | `high` |
| `worker` | executable with systemd service lifetime integration | `package:Microsoft.Extensions.Hosting.Systemd` | `medium` |
| `worker` | executable with Windows Service lifetime integration | `package:Microsoft.Extensions.Hosting.WindowsServices` | `medium` |
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

Subtype rules follow the same bar: they must use approved evaluated metadata, avoid source-code inspection and name/path heuristics, and keep the base `classification.kind` semantics unchanged.
