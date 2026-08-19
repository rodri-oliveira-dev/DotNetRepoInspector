# Inspection fixtures

This directory contains synthetic .NET repository and project structures used to prove discovery, MSBuild evaluation, SDK detection, project-reference graph, classification, and compatibility behavior.

Fixtures must be intentionally minimal and must not contain real application source, credentials, or secrets.

## Isolation boundary

`Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props` in this directory intentionally stop fixture projects from inheriting DotNetRepoInspector's own root build and package configuration. Individual fixture repositories may add their own local configuration when a scenario needs to exercise inheritance explicitly.

There is intentionally **no** `global.json` at the fixture root. A `global.json` would make the `Sdk/WithoutGlobalJson` scenario impossible to represent. Tests that need to verify SDK resolution for that scenario must copy the fixture repository to an isolated temporary workspace before invoking `dotnet`, so the repository's own root `global.json` cannot be discovered by upward traversal.

## Catalog

`catalog.json` is the internal fixture inventory. It assigns a stable id to every fixture root and records the behavior that fixture is intended to prove.

| Fixture | Behavior |
| --- | --- |
| `ProjectKinds/Web` | `Microsoft.NET.Sdk.Web` |
| `ProjectKinds/Worker` | `Microsoft.NET.Sdk.Worker` |
| `ProjectKinds/Console` | executable `OutputType` |
| `ProjectKinds/Library` | default SDK library semantics |
| `ProjectKinds/Test` | authoritative `IsTestProject` metadata |
| `ProjectKinds/MultiTargeting` | multiple target frameworks |
| `MSBuildEvaluation` | inherited `Directory.Build.props` plus a conditional property |
| `ProjectReferences/Simple` | one project reference |
| `ProjectReferences/Chain` | A → B → C |
| `ProjectReferences/Circular` | A ↔ B |
| `ProjectReferences/FanOut` | A → B and A → C |
| `ProjectReferences/Conditional` | evaluated `ProjectReference` conditions |
| `ProjectReferences/Unresolved` | missing project-reference target |
| `ProjectReferences/External/Repository` | reference to an existing project outside the inspection root |
| `Sdk/WithGlobalJson` | repository-local SDK configuration |
| `Sdk/WithoutGlobalJson` | repository with no `global.json` in its fixture tree |
| `Compatibility/Net8` | Inspector `net10.0` evaluating a repository pinned to .NET 8 and targeting `net8.0` |
| `Compatibility/Net10` | current .NET 10 compatibility baseline |
| `Compatibility/PathCasing` | mixed-case path and uppercase project extension portability |
| `Compatibility/MissingSdk` | stable unavailable-SDK diagnostic behavior |
| `InvalidProject` | malformed, non-evaluable project |
| `EmptyRepository` | repository containing no project files |

## Adding a fixture

1. Create the smallest directory tree that proves one behavior.
2. Add a `README.md` in the fixture root describing the signal and expected observation.
3. Avoid package references unless the behavior cannot be represented without them.
4. Keep all project references relative.
5. Add the fixture to `catalog.json` with a unique id, relative path, and explicit `proves` values.
6. If the fixture intentionally contains invalid XML or another malformed artifact, document that fact so structural tests can exclude it from normal parsing.
7. Run the solution tests and ensure the fixture matrix tests remain deterministic on different checkout paths and operating systems.
