# ADR 0013: Keep Razor Pages subtype unsupported without deterministic structural evidence

- **Status:** Accepted
- **Date:** 2026-09-29
- **Related:** #25, #153, #154, #173

## Context

The classification contract supports an optional `classification.subtype`, while ASP.NET Core projects already receive the base `web` classification from `Microsoft.NET.Sdk.Web`.

Issue #173 evaluates whether the current inspection model can identify Razor Pages deterministically without relying on project/folder naming conventions or broad source/semantic analysis.

Razor Pages and MVC Views share the Razor SDK pipeline. At the MSBuild layer, both are represented as Razor `.cshtml` inputs, and the Web SDK enables the same MVC/Razor build support for both application models.

## Considered signals

| Candidate | Assessment |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Authoritative for base Web classification only; shared by all major ASP.NET Core Web application models. |
| `AddRazorSupportForMvc == true` | Explicitly supports applications containing MVC views or Razor Pages and is implicitly enabled by modern Web SDK projects. |
| `RazorGenerate` items | The current `-getItem` evaluation boundary returns no default `RazorGenerate` items without additional target execution; even if collected later, generic Razor items do not carry the `@page` semantic distinction. |
| `Microsoft.NET.Sdk.Razor` | Proves Razor build capability and is also used by Razor Class Libraries. |
| `Pages/**`, `.cshtml.cs`, or project/folder names | Convention/path/name heuristics rather than authoritative application-model evidence. |
| `PageModel`, `AddRazorPages`, or `MapRazorPages` | Requires C#/semantic inspection and can coexist with MVC, APIs, or other Web models. |
| Razor `@page` directive | This is the distinguishing marker for a Razor Page, but detecting it requires reading Razor source content, which the current classification model does not do. |

No currently normalized project/MSBuild fact is both necessary and sufficient to identify the Razor Pages application model.

## Decision

Do not introduce a Razor Pages subtype rule.

For Web projects:

- keep `classification.kind = web`;
- preserve existing base Web confidence and signals;
- keep `classification.subtype` absent;
- do not infer Razor Pages from `AddRazorSupportForMvc`, `RazorGenerate`, the Razor SDK, file-system paths, or naming conventions.

The fixtures under `tests/Fixtures/RazorPagesSubtypeSignals` capture the ambiguity boundary:

1. a Web SDK project contains both a real Razor Page (with `@page`) and a regular MVC Razor View, while the current `-getItem:RazorGenerate` evaluation exposes neither without additional target execution;
2. a Razor Class Library can contain Razor Page source plus `AddRazorSupportForMvc=true` while remaining a `library`, demonstrating that project-level Razor support does not imply a Web Razor Pages subtype.

The research tests inspect fixture contents only to establish ground truth. Classification itself remains based exclusively on the existing structured facts and therefore emits no subtype.

## Consequences

The inspector deliberately remains less specific rather than treating generic Razor build metadata or path conventions as Razor Pages evidence.

A future ADR may supersede this decision if bounded Razor source inspection is explicitly approved or if a new authoritative structured signal becomes available. Any future rule must preserve `kind = web`, define precedence with other Web subtypes, and retain explicit ambiguous/non-match coverage.
