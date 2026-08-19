# ADR 0001: Evaluate projects through `dotnet msbuild`

- **Status:** Accepted
- **Date:** 2026-08-19
- **Decision owners:** DotNetRepoInspector maintainers

## Context

DotNetRepoInspector must inspect the effective metadata of .NET projects. Reading `.csproj` XML directly is insufficient because effective values can come from SDK imports, `Directory.Build.props`, `Directory.Build.targets`, conditional property groups, and other MSBuild imports.

The first implementation also needs to work in CI without coupling `DotNetRepoInspector.Core` to a particular MSBuild runtime or to the GitHub Actions environment.

## Decision

The MSBuild adapter will evaluate projects out-of-process through the .NET CLI.

For each project evaluation:

1. Resolve the absolute project path and use the project directory as the process working directory.
2. Execute `dotnet --version` as a preflight SDK-resolution step.
3. If SDK resolution succeeds, execute:

   ```text
   dotnet msbuild <project> -nologo -verbosity:quiet -getProperty:<property1,property2,...>
   ```

4. Do not request build targets when only evaluation-time metadata is needed.
5. Parse the structured JSON returned by MSBuild when multiple properties are requested; support the text form returned for a single property.
6. Return normalized adapter-owned results and error codes rather than exposing `Microsoft.Build.*` types or raw process objects.

The initial adapter contract is `IMsBuildProjectEvaluator`. `DotNetRepoInspector.Core` remains unaware of process execution and MSBuild implementation details.

## SDK selection

`dotnet --version` and `dotnet msbuild` are started from the inspected project's directory. The .NET CLI therefore applies its normal `global.json` search and roll-forward behavior from that location through its ancestor directories.

The preflight result is also used to distinguish SDK resolution failures from MSBuild project-evaluation failures without parsing localized error messages.

Initial error categories are:

- `InvalidRequest`;
- `ProjectNotFound`;
- `DotNetHostNotFound`;
- `SdkResolutionFailed`;
- `MsBuildEvaluationFailed`;
- `InvalidMsBuildOutput`.

Issue #4 will expand repository-level SDK metadata (`global.json`, configured SDK, resolved SDK) without changing this evaluation boundary.

## Why not raw project XML?

Raw XML cannot reliably produce effective values after imports, SDK defaults, and conditions. XML parsing can still be used later for narrow discovery/bootstrap scenarios, but it is not the source of truth for evaluated project metadata.

## Why not `Microsoft.Build.*` in-process?

Using `Microsoft.Build`, `Microsoft.Build.Locator`, or equivalent in-process APIs would provide a rich object model, but it introduces additional concerns for the first version:

- selecting and loading an MSBuild/SDK instance into the Inspector process;
- process-wide assembly loading and SDK resolver behavior;
- compatibility when inspecting repositories that select different SDK feature bands;
- tighter coupling between the Inspector runtime and the inspected repository's build tooling;
- more complex isolation and failure recovery.

The out-of-process CLI boundary gives the inspected repository's normal .NET SDK selection rules control over evaluation and keeps those dependencies out of the Core.

## Why not execute a build/design-time target?

The initial metadata needed by the Inspector is evaluation-time data. MSBuild supports querying properties and items after evaluation without specifying a target. Running build or design-time targets would add work and side effects that are unnecessary for this stage.

If a future fact can only be produced by target execution, that behavior must be introduced explicitly and documented separately.

## Consequences

### Positive

- Effective MSBuild imports and conditions are honored.
- The same adapter behavior is usable locally and in CI.
- The Core stays independent from `Microsoft.Build.*` and `System.Diagnostics.Process`.
- SDK-resolution failures and project-evaluation failures have distinct structured error codes.
- Process cancellation can terminate the child process tree.
- Argument passing uses `ProcessStartInfo.ArgumentList`; no shell command is constructed.

### Trade-offs

- Each evaluation requires one or more child processes, which has startup cost.
- Large repositories may require batching/caching or another optimization strategy later.
- Parsing CLI output becomes an adapter responsibility.
- The target SDK must be available in the execution environment.

Performance and scalability are intentionally deferred to issue #18 so optimization is driven by measurements.

## Security note

Not requesting build targets reduces unnecessary execution, but MSBuild evaluation is not a sandbox or a security boundary. Repositories and imported build logic must still be treated according to the execution environment's trust model. The broader security review is tracked by issue #24.

## References

- Microsoft Learn — Evaluate MSBuild items and properties: https://learn.microsoft.com/visualstudio/msbuild/evaluate-items-and-properties
- Microsoft Learn — MSBuild command-line reference: https://learn.microsoft.com/visualstudio/msbuild/msbuild-command-line-reference
- Microsoft Learn — `global.json` overview: https://learn.microsoft.com/dotnet/core/tools/global-json
