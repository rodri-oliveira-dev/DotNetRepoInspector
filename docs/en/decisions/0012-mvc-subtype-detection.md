# ADR 0012: Keep MVC subtype unsupported without deterministic structural evidence

- **Status:** Accepted
- **Date:** 2026-09-29
- **Related:** #25, #153, #154

## Context

The classification contract supports an optional `classification.subtype`, and Web projects are already identified from the declared `Microsoft.NET.Sdk.Web` SDK.

Issue #154 evaluates whether the current inspection model can refine `kind = web` into an MVC subtype without project/directory-name heuristics, source-code inspection, or broad semantic analysis.

MVC is especially easy to over-detect because ASP.NET Core shares Razor and MVC infrastructure across MVC views, Razor Pages, APIs, Razor Class Libraries, and mixed applications.

## Considered signals

| Candidate | Assessment |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Authoritative for base Web classification only; it is shared across ASP.NET Core application models. |
| `AddRazorSupportForMvc == true` | Not MVC-specific. The Razor SDK uses it for MVC views or Razor Pages, and modern Web SDK projects set it implicitly. |
| `Microsoft.NET.Sdk.Razor` plus `AddRazorSupportForMvc == true` | Can describe a Razor Class Library rather than a Web application. |
| `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` | Optional Razor runtime-compilation capability and not proof of MVC controller/view usage. |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | MVC infrastructure usable by controller/API scenarios and not proof of view-based MVC. |
| `Views/**`, `Controllers/**`, project names, or folder names | Convention-based path/name heuristics and compatible with mixed applications. |
| controller inheritance, `AddControllersWithViews`, conventional routes, or actions returning views | Potentially meaningful only through source/semantic analysis, which is outside scope. |

No available evaluated project/MSBuild fact is both necessary and sufficient for the MVC controller/view application model.

## Decision

Do not introduce an MVC subtype rule.

For Web projects:

- keep `classification.kind = web`;
- preserve the existing base Web confidence and signals;
- keep `classification.subtype` absent;
- do not promote `AddRazorSupportForMvc`, Razor SDK usage, MVC/Razor packages, names, folders, or optional tooling into subtype evidence.

The fixtures under `tests/Fixtures/MvcSubtypeSignals` capture two important ambiguity boundaries:

1. a modern Web SDK project evaluates `AddRazorSupportForMvc=true` implicitly while remaining only base `web`;
2. a Razor Class Library can explicitly set the same property while remaining a `library`, demonstrating that the property is not evidence of a Web MVC application.

Core regression tests also ensure common MVC/Razor package hints do not populate a subtype.

## Consequences

The inspector deliberately remains less specific instead of emitting a fragile MVC label. Consumers can rely on the base Web classification without interpreting shared Razor/MVC infrastructure as proof of the application model.

A future ADR may supersede this decision if the inspection model gains an authoritative structured MVC fact or if bounded semantic analysis becomes an explicitly supported input. Any such rule must preserve `kind = web`, define deterministic precedence with other Web subtypes, and include positive and ambiguous fixtures.
