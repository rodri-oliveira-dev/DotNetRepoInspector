# ADR 0015: Detect Blazor WebAssembly from its dedicated project SDK

- **Status:** Accepted
- **Date:** 2026-09-29
- **Related:** #25, #155, #174

## Context

The classification contract supports an optional `classification.subtype`. Previous Web subtype investigations intentionally remained unsupported because the regular `Microsoft.NET.Sdk.Web` SDK is shared across multiple ASP.NET Core application models.

Blazor WebAssembly is different. Standalone/client Blazor WebAssembly projects explicitly declare `Microsoft.NET.Sdk.BlazorWebAssembly`, which is a workload-specific project SDK rather than a convention inferred from source files, package names, project names, or folders.

The current inspection pipeline already collects declared project SDK identities as normalized structural facts, so this signal is available without expanding the source-inspection boundary.

## Considered signals

| Candidate | Assessment |
| --- | --- |
| declared `Microsoft.NET.Sdk.BlazorWebAssembly` | Accepted. It is explicit, evaluated as project SDK identity, and specific to standalone/client Blazor WebAssembly projects. |
| package `Microsoft.AspNetCore.Components.WebAssembly` | Rejected as the primary subtype signal. Package presence alone does not establish the project workload and can be referenced outside the dedicated SDK boundary. |
| `.razor` files or Razor SDK | Rejected. Razor component source can appear in reusable libraries and server-hosted applications. |
| project/folder names such as `.Client` | Rejected naming heuristic. |
| source use of `WebAssemblyHostBuilder` | Rejected because source/semantic inspection is outside the current classification model. |

## Decision

Recognize an explicitly declared `Microsoft.NET.Sdk.BlazorWebAssembly` SDK as a deterministic high-confidence Web subtype:

- `classification.kind = web`;
- `classification.subtype = blazor-webassembly`;
- `classification.confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.BlazorWebAssembly`.

Test-project signals keep their existing higher precedence. If the Blazor WebAssembly SDK appears together with an independent Worker workload signal, classification remains conservative and returns `unknown` with conflict signals instead of emitting the subtype.

A package hint, Razor component source, or Razor SDK without the dedicated Blazor WebAssembly SDK must not emit the subtype.

## Fixtures

The fixture set includes:

1. `BlazorWebAssemblySubtypeSignals/StandaloneSdk`, which declares the dedicated SDK and must emit the supported subtype;
2. `BlazorWebAssemblySubtypeSignals/RazorLibraryAmbiguous`, which contains Razor component source under `Microsoft.NET.Sdk.Razor` and must remain a library with no subtype.

## Consequences

Blazor WebAssembly becomes the first concrete supported `classification.subtype` rule.

The public schema does not require another version bump because the optional subtype field was already introduced by #25; this change begins populating that existing field for projects with deterministic evidence.

Server-hosted Blazor remains governed by ADR 0014 and is not inferred from `Microsoft.NET.Sdk.Web`, Razor component presence, or source conventions.
