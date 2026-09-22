# ADR 0010: Detect Worker projects beyond the declared Worker SDK

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0010-worker-project-detection-signals.md)

- **Status:** Accepted
- **Date:** 2026-09-22
- **Decision owners:** DotNetRepoInspector maintainers

## Context

The production classifier currently recognizes `worker` only when the project root declares `Microsoft.NET.Sdk.Worker`. That rule is deterministic, but it misses Worker-shaped projects that use the common `Microsoft.NET.Sdk`, custom/composed SDKs, or explicit service-hosting integration.

The official Worker SDK sets the evaluated MSBuild property `UsingMicrosoftNETSdkWorker=true` in its `Sdk.props`. This property can survive SDK composition even when the root project declaration does not expose `Microsoft.NET.Sdk.Worker` directly.

Microsoft's Worker guidance also uses `Microsoft.Extensions.Hosting`, but Generic Host is not Worker-specific: console and Web applications can use it for configuration, dependency injection, logging, lifetime management, and hosted services. Package presence alone therefore cannot distinguish a Worker from a generic hosted console application.

Service-lifetime packages are narrower. `Microsoft.Extensions.Hosting.Systemd` exists to host a .NET application as a Linux systemd service, and `Microsoft.Extensions.Hosting.WindowsServices` provides Windows Service lifetime integration. Those packages express long-running service deployment intent, but they can still be used outside the Worker template and are therefore moderate rather than authoritative signals.

## Decision

### Signal strength

| Strength | Structural signal | Decision |
| --- | --- | --- |
| Strong | declared `Microsoft.NET.Sdk.Worker` | Existing authoritative Worker SDK signal. |
| Strong | effective `UsingMicrosoftNETSdkWorker == true` | Approved fallback. This is the official flag emitted by the Worker SDK and can preserve Worker semantics through composed/custom SDKs. |
| Moderate | `OutputType == Exe` plus direct/evaluated `Microsoft.Extensions.Hosting.Systemd` | Approved service-lifetime fallback when no stronger Test/Web conflict exists. |
| Moderate | `OutputType == Exe` plus direct/evaluated `Microsoft.Extensions.Hosting.WindowsServices` | Approved service-lifetime fallback under the same constraints. |
| Weak, supporting only | `Microsoft.Extensions.Hosting` package presence | Generic Host is shared by Workers, consoles, and other host-based applications; do not classify from this alone. |
| Weak, supporting only | `OutputType == Exe`, `IsPackable=false`, server GC, JSON/config content, hosting abstractions | Common runtime/build shapes; insufficient independently or as a generic bundle. |
| Rejected | project/directory names, `.Worker` suffixes | Non-structural naming heuristic. |
| Rejected for this roadmap step | source inspection for `BackgroundService`, `IHostedService`, `AddHostedService` | Semantically useful but outside the no-source-analysis boundary of #47/#152. |

### Why Generic Host alone is not enough

The Worker template uses `Microsoft.Extensions.Hosting`, but that package provides the .NET Generic Host. A normal executable may intentionally use Generic Host only for dependency injection, configuration, logging, or graceful shutdown. Promoting every such executable to `worker` would convert an implementation technique into a workload classification and create systematic false positives.

The `HostingOnlyAmbiguous` fixture records this boundary.

### Proposed precedence for issue #152

Issue #152 should preserve all Test rules from ADR 0009 before Worker/Web decisions, then apply:

1. any approved Test signal -> `test` using the existing confidence/precedence;
2. Web SDK plus any **strong** Worker signal (`Microsoft.NET.Sdk.Worker` or `UsingMicrosoftNETSdkWorker=true`) -> `unknown` due conflicting workload evidence;
3. `Microsoft.NET.Sdk.Web` -> `web`, high confidence; service-lifetime packages do not override Web;
4. `Microsoft.NET.Sdk.Worker` -> `worker`, high confidence;
5. `UsingMicrosoftNETSdkWorker == true` -> `worker`, high confidence;
6. `OutputType == Exe` plus `Microsoft.Extensions.Hosting.Systemd` or `Microsoft.Extensions.Hosting.WindowsServices` -> `worker`, medium confidence;
7. continue with existing `OutputType == Exe` -> `console`, `OutputType == Library` -> `library`, then `unknown`.

A moderate service-lifetime package does not create a Web/Worker conflict because ASP.NET Core applications can also be hosted as Windows/systemd services. The explicit Web SDK remains stronger evidence in that shape.

### Exact facts for issue #152

The implementation should collect or reuse only:

- existing declared project SDK names;
- existing normalized `OutputType`;
- new effective `bool? UsingMicrosoftNETSdkWorker`;
- existing normalized `PackageReference` identities introduced by #151.

No new package-item collection is needed. `MsBuildProjectFactsEvaluator` already requests `PackageReference`; #152 only needs one additional property in the existing `dotnet msbuild -getProperty` evaluation.

Suggested stable classification signals are:

- existing `sdk:Microsoft.NET.Sdk.Worker`;
- `property:UsingMicrosoftNETSdkWorker=true`;
- `package:Microsoft.Extensions.Hosting.Systemd`;
- `package:Microsoft.Extensions.Hosting.WindowsServices`.

The Core classifier must continue to receive normalized values only and remain independent of MSBuild.

### Signals evaluated but not selected

The Worker SDK also emits the project-system capability `DotNetCoreWorker`. This is strong Worker-specific evidence, but collecting `ProjectCapability` would add another evaluated item set while duplicating the cheaper `UsingMicrosoftNETSdkWorker` property. It is not approved for #152 unless later evidence shows the property is insufficient.

`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Hosting.Abstractions`, GC properties, content-copy defaults, and framework/hosting packages were considered but remain non-authoritative.

## Fixtures and evidence

Research fixtures live under `tests/Fixtures/WorkerProjectSignals` so they do not alter the existing `ProjectKinds` smoke baseline before #152:

- `UsingWorkerProperty`: common SDK executable with the official `UsingMicrosoftNETSdkWorker=true` flag; reproduces the current `console` false negative;
- `SystemdService`: common SDK executable with explicit systemd service integration; represents the approved moderate fallback;
- `HostingOnlyAmbiguous`: executable with only `Microsoft.Extensions.Hosting`; remains `console` and demonstrates why Generic Host alone is insufficient;
- `WebConflict`: Web SDK plus the strong Worker flag; records the future conservative `unknown` conflict.

`WorkerProjectSignalResearchTests` verifies that these facts are observable through current MSBuild evaluation and documents current production behavior without changing the classifier.

## Collection cost

The incremental production cost proposed for #152 is one additional MSBuild property name, `UsingMicrosoftNETSdkWorker`, in the existing evaluation request. No additional child process, restore, source parse, or package-resolution step is required.

Service-lifetime detection reuses the normalized `PackageReference` items already collected for Test-project classification. The expected marginal memory/CPU cost is therefore bounded to one scalar property per project plus constant-time membership checks over the existing package list.

## Consequences

The follow-up can recognize SDK-composed Workers with high confidence and common-SDK service applications with medium confidence without broad Generic Host false positives.

Some real Workers that use only `Microsoft.Extensions.Hosting` and register `BackgroundService` in source will intentionally remain `console`. Detecting those would require source-level or richer semantic evidence and is outside this roadmap step.

## Alternatives considered

- **Classify any executable with `Microsoft.Extensions.Hosting` as Worker:** rejected because Generic Host is intentionally general-purpose.
- **Scan C# for `BackgroundService`, `IHostedService`, or `AddHostedService`:** rejected for this step because it introduces source-language semantic analysis.
- **Collect `ProjectCapability=DotNetCoreWorker`:** deferred because it duplicates the official Worker property at higher collection cost.
- **Use names or `.Worker` suffixes:** rejected as non-structural.
- **Require both service-lifetime package and Generic Host package:** rejected as unnecessary; the service packages already depend on hosting concepts and direct dependency shape can vary.

## References

- .NET SDK Worker `Sdk.props`: https://github.com/dotnet/sdk/blob/main/src/WebSdk/Worker/Sdk/Sdk.props
- .NET SDK Worker targets: https://github.com/dotnet/sdk/blob/main/src/WebSdk/Worker/Targets/Microsoft.NET.Sdk.Worker.targets
- Microsoft Learn — Background tasks with hosted services: https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services
- Microsoft Learn — Windows Service with BackgroundService: https://learn.microsoft.com/dotnet/core/extensions/windows-service
- Microsoft.Extensions.Hosting.Systemd package: https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Systemd
- Issue #47: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/47
- Follow-up #152: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/152
