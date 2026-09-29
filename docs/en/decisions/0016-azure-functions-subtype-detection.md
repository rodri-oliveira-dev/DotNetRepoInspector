# ADR 0016: Detect Azure Functions from official SDK and runtime model signals

- **Status:** Accepted
- **Date:** 2026-09-29
- **Related:** #25, #156

## Context

Azure Functions currently has two relevant .NET execution models:

1. **isolated worker**, where function code runs in a separate .NET worker process;
2. **in-process**, where function code runs inside the Functions host process.

At the time of this decision, Microsoft recommends the dedicated `Azure.Functions.Sdk` project SDK for supported isolated-worker projects. The previous isolated-worker project shape remains relevant during migration and uses `Microsoft.NET.Sdk`, `AzureFunctionsVersion`, `OutputType=Exe`, `Microsoft.Azure.Functions.Worker`, and `Microsoft.Azure.Functions.Worker.Sdk`.

The in-process model remains supported until 2026-11-10. Its .NET project shape uses `AzureFunctionsVersion` and the official `Microsoft.NET.Sdk.Functions` build package with library output semantics.

The inspector already collects declared project SDKs and evaluated package references. This ADR promotes `AzureFunctionsVersion` to an explicit normalized classification fact so model detection does not depend on arbitrary raw-property access.

## Model decisions

### Isolated worker — current SDK model

Accepted as deterministic.

Required evidence:

- declared project SDK `Azure.Functions.Sdk`.

Classification:

- `classification.kind = worker`;
- `classification.subtype = azure-functions-isolated`;
- `classification.confidence = high`;
- signal `sdk:Azure.Functions.Sdk`.

The SDK identity is authoritative enough by itself because it is a Functions-specific project SDK. The required `Microsoft.Azure.Functions.Worker` package remains part of a valid project shape, but package presence is not required as a second classifier signal.

### Isolated worker — legacy build-package model

Accepted as deterministic only when the complete official shape is present.

Required evidence:

- effective `AzureFunctionsVersion`;
- effective `OutputType = Exe`;
- evaluated package `Microsoft.Azure.Functions.Worker`;
- evaluated package `Microsoft.Azure.Functions.Worker.Sdk`.

Classification:

- `classification.kind = worker`;
- `classification.subtype = azure-functions-isolated`;
- `classification.confidence = high`.

Neither Worker package is authoritative by itself.

### In-process model

Accepted as deterministic only when the official runtime/build pair is present.

Required evidence:

- effective `AzureFunctionsVersion`;
- effective `OutputType = Library`;
- evaluated package `Microsoft.NET.Sdk.Functions`.

Classification:

- `classification.kind = library`;
- `classification.subtype = azure-functions-in-process`;
- `classification.confidence = high`.

The package alone is not authoritative. The runtime property and library output semantics are required to avoid treating an incidental package reference as an application-model decision.

## Rejected or ambiguous signals

The following are not sufficient by themselves:

- `AzureFunctionsVersion`;
- `Microsoft.Azure.Functions.Worker`;
- `Microsoft.Azure.Functions.Worker.Sdk`;
- `Microsoft.NET.Sdk.Functions`;
- `host.json`, `local.settings.json`, project/folder names, or source attributes such as `Function` / `FunctionName`.

Source and path conventions remain outside the classification boundary.

If isolated-worker and in-process model-specific package sets are present together, classification returns `unknown` with `conflict:azure-functions-model` and no subtype.

Existing authoritative test-project signals keep higher precedence than Azure Functions detection.

## Fixtures

The fixture set covers:

- current isolated worker with `Azure.Functions.Sdk`;
- legacy isolated worker with the official Worker/Worker.Sdk package combination;
- in-process with `AzureFunctionsVersion` plus `Microsoft.NET.Sdk.Functions`;
- `AzureFunctionsVersion` without model evidence as a non-match;
- a deliberately mixed isolated/in-process package set as an ambiguous conflict.

The current-SDK fixture is parsed with a controlled evaluator stub so CI does not depend on downloading the external MSBuild project SDK merely to prove declared SDK identity. Microsoft.NET.Sdk-based fixtures use the normal MSBuild evaluation path.

## Consequences

Azure Functions becomes the second supported subtype family after Blazor WebAssembly.

No schema-version bump is required because `classification.subtype` is already optional in schema 1.4. `AzureFunctionsVersion` is an internal normalized classification fact and is not added to the public inspection JSON.

The in-process rule should be revisited after its Microsoft support end date, but its structural detection remains useful for inspecting existing repositories and migration inventories.
