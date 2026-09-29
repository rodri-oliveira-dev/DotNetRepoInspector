# ADR 0014: Keep Blazor Web App and server-side subtypes unsupported without deterministic structural evidence

- **Status:** Accepted
- **Date:** 2026-09-29
- **Related:** #25, #153, #154, #173, #155

## Context

The classification contract supports an optional `classification.subtype`, while server-hosted ASP.NET Core applications already receive the base `web` classification from `Microsoft.NET.Sdk.Web`.

Issue #155 evaluates two related variants: modern Blazor Web App and server-side Blazor hosting (Interactive Server and classic Blazor Server). The goal is to determine whether the current structured project/MSBuild facts can identify either variant without source-code analysis or naming/path heuristics.

Modern Blazor Web App uses the standard ASP.NET Core Web SDK. Interactive Server is enabled through application code such as `AddInteractiveServerComponents` and `AddInteractiveServerRenderMode`. Classic Blazor Server likewise uses the Web SDK and configures server-side Blazor through application code.

## Considered signals

| Candidate | Assessment |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Authoritative for base Web classification only; shared by Blazor, MVC, Razor Pages, APIs, and mixed apps. |
| `.razor` files exposed as `Content` | Proves Razor component source exists, but Razor components can be hosted by mixed ASP.NET Core apps and Razor Class Libraries. |
| `RazorComponent` | Created by Razor SDK targets from `.razor` content after the basic evaluation boundary; component compilation does not identify the hosting/render mode. |
| implicit `Microsoft.AspNetCore.App` framework reference | Shared by ASP.NET Core Web projects and not Blazor-specific. |
| `AddRazorComponents` / `MapRazorComponents` | Strong evidence of a Razor Components app, but only available through source/semantic inspection. |
| `AddInteractiveServerComponents` / `AddInteractiveServerRenderMode` | Strong evidence of modern Interactive Server hosting, but only available through source/semantic inspection. |
| `AddServerSideBlazor` / `MapBlazorHub` | Strong evidence of classic Blazor Server hosting, but only available through source/semantic inspection. |
| `Components/**`, `App.razor`, `Routes.razor`, `_Host.cshtml` | Template/path conventions rather than authoritative project metadata. |

Blazor WebAssembly is intentionally excluded from this ADR and is evaluated in #174 because the standalone client project has a distinct SDK boundary.

## Decision

Do not introduce a Blazor Web App subtype or a Blazor Server/server-side subtype.

For both variants:

- keep `classification.kind = web`;
- preserve the existing base Web confidence and signal;
- keep `classification.subtype` absent;
- do not infer hosting from `.razor` file presence, Razor SDK target items, framework references, template paths, or names.

The fixtures under `tests/Fixtures/BlazorServerSubtypeSignals` establish ground truth through source files that contain the relevant hosting APIs. The MSBuild research tests deliberately compare that ground truth with the structured evaluation boundary: `.razor` source is observable as generic `Content`, while `RazorComponent` is not materialized by the basic evaluation call and the classifier receives no hosting-mode fact.

## Consequences

The inspector remains conservative: a Web project can contain Razor components and even be a real Blazor Web App or Interactive Server app without receiving a subtype.

A future ADR may supersede this decision if bounded source/semantic inspection becomes an approved classification input or if the SDK introduces an authoritative hosting-mode project property. Any future rule must preserve `kind = web`, define precedence with other Web subtypes, and keep mixed-app ambiguity explicit.