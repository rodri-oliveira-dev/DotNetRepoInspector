# Inspection engine

`DotNetRepoInspector.Engine` is the reusable application layer that performs a complete repository inspection in one operation. It is intentionally independent of the CLI, GitHub Actions, persistence, and policy/reporting integrations.

## API

The engine exposes `IRepositoryInspector`:

```csharp
Task<InspectionReport> InspectAsync(
    RepositoryInspectionRequest request,
    CancellationToken cancellationToken = default);
```

`RepositoryInspectionRequest` contains the repository root and optional repository-relative directories to exclude from discovery.

The default `RepositoryInspector` composes the current adapters in this order:

1. repository Git metadata;
2. .NET SDK configuration and resolved SDK;
3. project discovery;
4. evaluated MSBuild facts for each discovered project;
5. deterministic project classification;
6. evaluated `ProjectReference` graph;
7. normalized repository- and project-level diagnostics;
8. stable `InspectionReport` construction.

Project evaluation is currently sequential. This keeps process pressure bounded and output behavior straightforward while the engine is still establishing its baseline; parallel evaluation can be evaluated later with measurements rather than introduced implicitly.

## Failure semantics

The engine distinguishes failures that make the inspection request impossible from failures that affect only part of the evidence.

### Fatal failures

Fatal failures stop the operation instead of producing a misleading partial report:

- the repository root argument is empty or invalid;
- the requested repository root does not exist;
- project discovery cannot establish the project inventory;
- cancellation is requested.

Cancellation is never translated into an inspection diagnostic. `OperationCanceledException` propagates to the caller.

### Partial failures

Partial failures preserve all information that can still be inspected:

- Git metadata is unavailable or incomplete: repository fields remain absent and a repository-level warning may be emitted;
- SDK inspection fails: the failure becomes a repository-level diagnostic, while project evaluation is still attempted where possible;
- one project fails MSBuild evaluation: that project remains in `projects` with its path/name and a project-level error diagnostic; other projects continue normally;
- a `ProjectReference` target is missing: the edge is retained and `DRI1003` is attached to the source project.

This distinction allows automation to inspect the normalized diagnostics and decide its own policy without losing valid project facts.

## Determinism

For the same repository state and toolchain, the normalized report is deterministic:

- discovered projects are processed and emitted in ordinal path order;
- project SDKs, target frameworks, runtime identifiers, and references are normalized and ordered;
- diagnostics are ordered before report construction;
- the public serializer performs its own canonicalization as the final contract boundary.

Machine-specific absolute project paths are kept inside adapters and orchestration only. Project paths and reference paths in the public report remain repository-relative with `/` separators.

## Cancellation

The supplied `CancellationToken` is propagated to Git processes, SDK inspection, project discovery, and every MSBuild project evaluation. Filesystem discovery checks cancellation while traversing directories and files so a large repository can be interrupted without waiting for the full scan to finish.
