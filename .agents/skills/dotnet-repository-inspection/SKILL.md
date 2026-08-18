---
name: dotnet-repository-inspection
description: Use this skill when changing project discovery, MSBuild evaluation, .NET SDK/framework detection, project classification, normalized inspection results, fixture repositories, or the JSON inspection contract in DotNetRepoInspector. Do not use it for unrelated repository documentation or generic C# refactoring.
---

# .NET Repository Inspection

## Goal

Implement repository inspection as deterministic extraction of evaluated .NET/MSBuild facts, followed by explicit normalization and classification. Avoid fragile heuristics that only work for template-generated projects.

## Source-of-truth hierarchy

Prefer signals in this order:

1. effective MSBuild properties/items after evaluation;
2. project SDK identity and imported SDK behavior;
3. repository SDK configuration (`global.json`) and resolved CLI SDK;
4. well-defined package/project metadata when required for a classification;
5. raw project XML only when the required fact cannot be obtained reliably from evaluated MSBuild state;
6. filename/path conventions only as non-authoritative hints.

Do not classify a project primarily because its name ends in `.Api`, `.Worker`, `.Tests`, or similar.

## Required distinctions

Keep these concepts separate:

- configured SDK version from `global.json`;
- SDK actually resolved by `dotnet`;
- `TargetFramework` / `TargetFrameworks`;
- project SDK (`Microsoft.NET.Sdk`, `Microsoft.NET.Sdk.Web`, `Microsoft.NET.Sdk.Worker`, etc.);
- `OutputType`;
- semantic project classification.

A repository can target `net8.0` while being inspected with a newer installed SDK. Do not collapse those facts into one `dotnetVersion` field.

## Classification guidance

Classification precedence must be explicit and covered by tests. A reasonable baseline is:

1. identify test-project semantics when `IsTestProject` or another authoritative test signal is evaluated;
2. identify workload-specific SDKs such as Worker or Web;
3. identify executable output as Console when no more specific workload applies;
4. identify library output/default library semantics;
5. return Unknown when evidence is insufficient or conflicting.

Treat `Microsoft.NET.Sdk.Web` as Web by default. Do not claim Web API, Razor Pages, MVC, or Blazor solely from the Web SDK; those are subtypes requiring additional reliable signals.

When adding Functions or other workloads, document the exact signals and add isolated fixtures before adding the classification.

## Discovery

- Discover supported project files deterministically.
- Ignore generated/build directories such as `bin` and `obj`.
- Do not follow arbitrary filesystem links outside the inspection root unless explicitly designed and tested.
- Normalize paths in output so results are stable across Windows/Linux where practical.
- Define behavior for duplicate discovery, inaccessible files, malformed projects, and partial failures.

## MSBuild evaluation

Account for:

- `Directory.Build.props` and `Directory.Build.targets`;
- SDK imports;
- explicit imports;
- conditional `PropertyGroup` and `ItemGroup` values;
- multi-targeting;
- project references;
- configuration/platform/global properties that can change evaluation.

Do not substitute `XDocument.Load(project.csproj)` for MSBuild evaluation when the requested value can be inherited or computed.

If choosing between invoking `dotnet msbuild -getProperty/-getItem` and loading MSBuild in-process, record the trade-off in an ADR because it affects SDK resolution, process isolation, deployment size, compatibility, and testability.

## Result model

Normalized output should be independent from the mechanism used to discover it. Avoid leaking MSBuild object types into Core contracts.

Prefer immutable/read-only models where practical. Explicitly represent optional/unknown facts instead of inventing defaults.

Once JSON is public:

- carry `schemaVersion`;
- keep deterministic property semantics;
- test serialization;
- treat field removal/rename/type changes as contract changes.

## Privacy and safety

Inspection is metadata extraction, not source-code harvesting.

Do not include:

- source file contents;
- secret/environment variable values;
- NuGet credentials;
- connection strings;
- arbitrary MSBuild property values that may contain secrets unless they are explicitly allow-listed and justified.

Prefer an allow-list of properties collected by the inspector over serializing the complete evaluated MSBuild property bag.

## Fixture testing

For every new detection rule, add the smallest fixture that proves it.

Useful fixture dimensions include:

- Web SDK;
- Worker SDK;
- default SDK library;
- default SDK executable;
- test project;
- inherited target framework;
- multi-targeting;
- conditional property;
- project reference graph;
- malformed/unsupported project;
- repository with and without `global.json`.

Assert both positive and negative cases so a new heuristic does not reclassify unrelated projects.

## Completion criteria

A change to inspection logic is complete when:

1. the source signal is documented and reliable;
2. normalization is independent of infrastructure details;
3. classification precedence remains deterministic;
4. fixture tests cover the intended case and a relevant counterexample;
5. no additional sensitive data is collected;
6. public output changes are called out explicitly.
