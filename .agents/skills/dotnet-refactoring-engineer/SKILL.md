---
name: dotnet-refactoring-engineer
description: Use this skill to review or refactor DotNetRepoInspector C#/.NET code for clarity, maintainability, testability, performance, API design, dependency direction, and safe use of modern .NET. Do not use it for cosmetic rewrites without a concrete engineering benefit.
---

# .NET Refactoring Engineer

## Principles

Before changing code:

1. understand observable behavior;
2. identify the concrete problem;
3. prefer small, verifiable changes;
4. avoid abstractions without an existing variation or boundary;
5. preserve public contracts unless the task explicitly changes them;
6. keep refactoring separate from functional behavior when practical;
7. use tests as protection for behavior, not implementation details.

A refactoring should improve at least one measurable quality: clarity, cohesion, coupling, testability, complexity, correctness, resource use, or maintainability.

## Project-specific boundaries

Respect the intended dependency direction:

```text
Cli -> MSBuild -> Core
```

`Core` must not acquire dependencies on GitHub, databases, environment-specific CI APIs, or concrete process execution. Keep MSBuild types and process details in the MSBuild adapter.

When work changes repository discovery or classification semantics, also use `dotnet-repository-inspection`.

## C#/.NET guidance

- Use clear, capability-oriented names.
- Propagate `CancellationToken` for meaningful asynchronous operations.
- Do not block async work with `.Result` or `.Wait()`.
- Avoid global mutable state.
- Keep disposal/lifetime of processes, streams, and MSBuild resources explicit.
- Preserve stack traces when rethrowing with `throw;`.
- Prefer typed result models over loosely typed dictionaries at Core boundaries.
- Avoid generic `Helper`, `Utils`, or `Manager` classes when a specific responsibility can be named.
- Do not add a dependency merely to replace straightforward BCL functionality.

## Performance

Repository inspection may process many projects. Watch for:

- evaluating the same project repeatedly;
- starting unnecessary `dotnet`/MSBuild processes;
- unbounded parallel process execution;
- reading entire files when only metadata is required;
- materializing large graphs repeatedly;
- non-deterministic concurrency affecting output order.

Do not optimize without evidence, but design algorithms so repository size is considered explicitly.

## Error handling

Differentiate errors such as:

- unsupported project;
- malformed project;
- MSBuild evaluation failure;
- SDK not installed/resolvable;
- inaccessible filesystem entry;
- partial repository result.

Do not swallow failures or collapse every condition into a generic exception when callers need actionable diagnostics.

## Tests

- Test observable behavior.
- Prefer fixture-based tests for inspection semantics.
- Add characterization tests before risky refactors when behavior is not already protected.
- Avoid sleeps, external network dependencies, mutable global state, and order-dependent tests.
- Do not expose internal implementation merely to make it easier to test.

## Validation

Use the smallest relevant build/test target first, then broader validation when the change crosses project boundaries. Respect `.editorconfig`, analyzers, and Central Package Management.

## Completion report

Summarize the behavior preserved or changed, files affected, technical reason, tests run, and any remaining risk. Do not present purely stylistic churn as an architectural improvement.
