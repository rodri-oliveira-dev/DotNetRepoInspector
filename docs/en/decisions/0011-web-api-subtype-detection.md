# ADR 0011: Keep Web API subtype unsupported without deterministic structural evidence

- **Status:** Accepted
- **Date:** 2026-09-28
- **Related:** #25, #153

## Context

The classification contract now supports an optional `classification.subtype`, while the existing base classifier already identifies Web projects from the declared `Microsoft.NET.Sdk.Web` project SDK.

Issue #153 evaluates whether the current inspection model can refine `kind = web` into a Web API subtype without project/directory-name heuristics, source-code inspection, or broad semantic analysis.

The subtype must be reproducible from structured facts and must not turn optional conventions into authoritative evidence.

## Considered signals

| Candidate | Assessment |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Strong evidence for base Web classification only. MVC, Razor Pages, Blazor/server-side hosts, APIs, and mixed applications can share it. |
| `OutputType == Exe` | Common host shape, not API-specific. |
| `Microsoft.AspNetCore.OpenApi` | Optional API-oriented tooling. A valid API can omit it, and a mixed Web application can include it. |
| `Swashbuckle.AspNetCore` or similar Swagger tooling | Optional documentation/tooling convention, not an application-model contract. |
| Razor properties/items | Can demonstrate Razor capability, but cannot prove that API endpoints are absent. Mixed applications are valid. |
| `launchSettings.json` with Swagger-related values | Optional development tooling configuration, not an evaluated normalized project fact. |
| `MapGet`, `MapPost`, `[ApiController]`, `ControllerBase` | Potentially meaningful only through source/semantic analysis, which is outside scope. |
| project/directory names such as `.Api` | Explicitly prohibited heuristic. |

No candidate is both necessary and sufficient for the Web API application model within the current project/MSBuild fact boundary.

## Decision

Do not introduce a Web API subtype rule.

For projects classified from `Microsoft.NET.Sdk.Web`:

- keep `classification.kind = web`;
- keep the existing `high` confidence and `sdk:Microsoft.NET.Sdk.Web` base signal;
- keep `classification.subtype` absent;
- do not promote OpenAPI/Swagger package presence, Razor absence/presence, launch profiles, names, or paths into subtype heuristics.

The research fixtures under `tests/Fixtures/WebApiSubtypeSignals` record both the ambiguous Web SDK executable shape and the fact that Razor support can overlap with API-capable Web applications. Core regression tests also ensure common API-tooling package hints do not populate a subtype.

## Consequences

The inspector intentionally produces less-specific output rather than a fragile Web API label. Consumers can rely on `kind = web` without mistaking conventions for an application-model guarantee.

A future ADR may supersede this decision if the inspection model gains an authoritative structured fact or if bounded semantic analysis becomes an explicitly supported classification input. Such a change must preserve base-kind compatibility and include deterministic positive and negative fixtures.
