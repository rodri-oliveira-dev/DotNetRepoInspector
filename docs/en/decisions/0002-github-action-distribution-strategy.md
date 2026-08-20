# ADR 0002: Distribute the GitHub Action as a composite action over the .NET Tool

**Languages:** English | [Português (Brasil)](../../pt-BR/decisions/0002-github-action-distribution-strategy.md)

- **Status:** Accepted
- **Date:** 2026-08-19
- **Decision owners:** DotNetRepoInspector maintainers

## Context

DotNetRepoInspector already has a CLI boundary and can be packaged as the `DotNetRepoInspector` .NET Tool. The next delivery boundary is a reusable GitHub Action with the intended consumer experience:

```yaml
- name: Inspect .NET repository
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
```

The Action must not become a second implementation of repository discovery, MSBuild evaluation, classification, diagnostics, or JSON serialization. Those behaviors belong to the existing engine and CLI contracts.

The distribution strategy must balance startup cost, runner compatibility, runtime availability, release/versioning semantics, and supply-chain exposure. It also needs to work naturally for .NET repositories without requiring a Docker-only execution environment or a separate JavaScript implementation of inspection behavior.

## Decision

The public GitHub Action will be a **composite action** whose only inspection executor is an **exact, release-pinned version of the `DotNetRepoInspector` .NET Tool**.

The public `action.yml` will live at the repository root so consumers can use `rodri-oliveira-dev/DotNetRepoInspector@<ref>` directly.

The composite action is responsible only for delivery concerns:

1. validate and normalize Action inputs needed to invoke the CLI;
2. ensure a compatible .NET runtime/SDK is available for the Inspector itself;
3. install an exact `DotNetRepoInspector` package version into an Action-owned temporary tool path;
4. invoke `dotnet-repo-inspect` with the requested repository path and output file;
5. expose small automation-oriented outputs derived from the CLI result;
6. preserve the CLI exit semantics.

It must not reimplement project discovery, MSBuild interpretation, classification, diagnostics, repository metadata extraction, or the JSON contract.

## Runtime bootstrap

The Action must not depend on a particular .NET SDK version merely happening to be present in a GitHub-hosted runner image.

For the Inspector runtime, the Action will ensure the required .NET SDK/runtime is available using a pinned, trusted setup mechanism. If `actions/setup-dotnet` is used from the composite action, the dependency must be pinned to a full commit SHA in released Action revisions rather than a floating branch or major tag.

The bootstrap may avoid a download when a compatible Inspector SDK/runtime is already available, but that is an optimization rather than a contract.

This bootstrap only guarantees the runtime needed to execute DotNetRepoInspector. **SDKs required by the repository being inspected remain the caller's responsibility.** This is necessary because inspected repositories may select different SDKs through `global.json`, including SDKs other than the Inspector's own target runtime. The Action must not silently guess or install arbitrary repository SDK matrices.

## Tool installation

The Action will install the tool into a directory owned by the current Action invocation under the runner temporary directory, using `dotnet tool install --tool-path` or an equivalent isolated mechanism.

It will not install the tool globally and will not modify the inspected repository's local tool manifest. This avoids persistent user-state changes, command collisions, and repository mutations.

Each released Action revision must pin an **exact** `DotNetRepoInspector` package version. Wildcards, `latest`, implicit stable-version resolution, and implicit prerelease selection are not allowed.

The initial implementation will not expose an `inspector-version` input. Allowing callers to replace the pinned tool version would make a given Action ref capable of producing different schemas and behavior over time, weakening reproducibility. If an override is introduced later, it requires an explicit compatibility and supply-chain design.

## Package source isolation

Tool bootstrap must not trust the inspected repository's `NuGet.config` files for resolving the Inspector package.

The Action should use an isolated temporary NuGet configuration for its own tool installation, clearing inherited package sources and selecting the intended public package source explicitly. This prevents a repository-controlled or machine-level feed from shadowing the `DotNetRepoInspector` package ID/version during Action bootstrap.

This isolation applies only to installation of the Inspector tool. It must not rewrite package-source configuration used by the inspected repository itself.

## Action inputs

The v1 Action contract is expected to expose the following high-level inputs:

| Input | Required | Default | Meaning |
| --- | --- | --- | --- |
| `path` | No | `.` | Repository path to inspect, relative to the workspace when not absolute. |
| `output` | No | Action-owned temporary file | Destination for the inspection JSON. |
| `verbosity` | No | `normal` | Operational logging level: `normal`, `verbose`, or `debug`. |

The Action must not perform an implicit checkout. Consumers control `actions/checkout` and its permissions/ref semantics explicitly before invoking the Inspector.

Additional policy inputs should not be added merely to wrap CLI behavior. In particular, v1 will preserve the CLI's existing exit semantics rather than invent a second Action-specific interpretation of inspection errors.

## Action outputs

The v1 Action contract is expected to expose small outputs suitable for downstream automation:

| Output | Meaning |
| --- | --- |
| `report-path` | Absolute path to the generated inspection JSON when a report exists. |
| `schema-version` | Schema version read from the generated report when available. |
| `inspector-version` | Exact .NET Tool version pinned by this Action release. |
| `exit-code` | Exit code returned by the CLI. |

The complete inspection JSON will **not** be duplicated into a GitHub Action output. Reports can be materially larger than normal step outputs, and a file is the canonical machine-readable boundary already supported by the CLI. Downstream steps should read `report-path`.

Reading `schemaVersion` from the generated JSON to populate an output is delivery-layer plumbing; it must not become an independent JSON serialization or schema implementation.

## Exit behavior

The Action will preserve the stable CLI exit codes rather than collapsing them into a generic success/failure model.

The wrapper should capture the CLI exit code, publish any outputs that can still be determined, and then complete with the same non-zero status when the CLI fails. A report produced with error diagnostics therefore remains distinguishable from a fatal failure according to the CLI contract.

This keeps local CLI execution and GitHub Actions execution behaviorally aligned.

## Versioning strategy

The GitHub Action and .NET Tool are released from the same repository and will use one product release version.

For a full release `v1.2.3`:

- the immutable full Action release tag is `v1.2.3`;
- the release revision pins `DotNetRepoInspector` package version `1.2.3` exactly;
- the moving compatibility aliases `v1` and, when maintained, `v1.2` point to the latest compatible full release;
- release automation must not move a compatibility alias until the corresponding exact package has been published and validated.

Consumers that prioritize update convenience can use `@v1`. Consumers that prioritize reproducibility can use an immutable full release tag or a full commit SHA.

### Relationship to the JSON schema

The Action major version is a compatibility boundary for both the Action interface and the public inspection contract consumed through it.

- Action `v1` may only pin Inspector releases whose output remains compatible with schema major version `1`.
- Additive/backward-compatible Inspector and schema changes may be released within Action `v1` according to semantic versioning.
- A breaking Action input/output change requires a new Action major version.
- A breaking inspection schema change also requires a new Action major version, even if the `action.yml` inputs themselves did not change.

This prevents a moving `v1` tag from silently crossing a machine-readable contract boundary.

## Caching and startup cost

The first Action version will **not** add its own `actions/cache` layer for the installed tool.

The tool version is exact, and the .NET/NuGet client may reuse caches already present in the environment. Adding an Action-managed cache introduces cache-key, poisoning, invalidation, and cross-platform concerns that are not justified without measurements.

If Action startup time becomes material, measure the runtime bootstrap and tool installation separately before introducing a cache or changing distribution strategy. A self-contained release artifact remains a valid future optimization if measurements show that .NET/tool bootstrap dominates execution time.

## Permissions and security

The inspection Action itself requires no GitHub API call and no GitHub token for a local checkout. It must not request write permissions and must not require a secret for normal inspection.

Released Action revisions must follow these supply-chain rules:

- pin nested third-party/first-party actions to full commit SHAs;
- pin the Inspector package to an exact version;
- isolate the Inspector package source from repository-controlled NuGet configuration;
- do not execute package versions selected from floating ranges;
- publish an Action release only after the corresponding package has passed build, test, packaging, and installation validation;
- keep publication credentials out of pull-request validation workflows.

Repository inspection itself is not a sandbox. ADR 0001 establishes that project evaluation uses `dotnet msbuild`; imported MSBuild logic and repository-controlled build configuration must therefore be treated according to the trust level of the checked-out code. Workflows must not use this Action to inspect untrusted code in a privileged context such as a secrets-bearing workflow without an explicit security review.

## Alternatives considered

### JavaScript/TypeScript Action that invokes the CLI

A JavaScript Action can run directly on Linux, Windows, and macOS runners and generally has low wrapper startup overhead. It is not selected because the actual inspection still requires the .NET Inspector runtime or a separately distributed native artifact.

Using JavaScript only to download and invoke the same CLI would add another build/runtime/toolchain, dependency graph, bundled `dist` artifact, and release surface without removing the .NET execution requirement or providing inspection capability that the composite action cannot provide.

A JavaScript wrapper may be reconsidered if future Action-specific behavior becomes complex enough to justify a dedicated implementation layer, but inspection logic must still remain in the .NET engine/CLI.

### Docker container Action

A Docker Action provides strong packaging consistency and can include all runtime dependencies. It is rejected for the primary Action because GitHub Docker container actions execute only on Linux runners and add image download/startup overhead.

That conflicts directly with the project's Windows/macOS compatibility goal.

### Self-contained binaries attached to releases

Publishing self-contained Inspector binaries would remove the .NET runtime prerequisite for the Action and could reduce bootstrap work after download.

It is not selected initially because it creates a release matrix across operating systems and architectures, requires asset selection and integrity handling, increases artifact size, and creates a second distribution path alongside the already validated .NET Tool package.

This option remains open if measured Action startup cost justifies the additional release complexity.

### Composite Action requiring callers to preinstall the Inspector

Requiring the consumer to install a specific tool version before the Action would minimize Action implementation work but would make the reusable step incomplete and easy to misconfigure. It also weakens the relationship between an Action release and the Inspector version that actually executes.

The selected design therefore owns Inspector bootstrap while leaving repository-specific SDK installation explicit in the consumer workflow.

## Consequences

### Positive

- The Action reuses the exact same CLI and engine as local execution.
- No classification or inspection behavior is duplicated in YAML, shell, PowerShell, or JavaScript.
- The distribution model supports Linux, Windows, and macOS runners.
- Action release refs deterministically select an Inspector version and schema compatibility boundary.
- The inspected repository is not mutated by tool installation.
- Normal inspection requires no GitHub token or write permission.
- The package-source boundary reduces dependency-confusion/shadowing risk during tool bootstrap.

### Trade-offs

- First use may need to install a compatible .NET runtime/SDK and download the tool package.
- NuGet package availability is part of Action availability.
- The caller still needs to make repository-selected SDKs available when they are not already installed.
- Cross-platform composite glue must be tested on all supported runner operating systems.
- Action aliases such as `v1` require disciplined release/tag management.

## Implementation boundary

Issue #14 will implement `action.yml`, the cross-platform bootstrap/invocation glue, Action CI tests, and end-user usage documentation according to this ADR.

Changing from the selected composite/.NET Tool distribution model, allowing arbitrary tool-version overrides, or changing the Action/schema major-version relationship requires a new ADR or an explicit superseding decision.

## References

- GitHub Docs — About custom actions: https://docs.github.com/actions/concepts/workflows-and-actions/custom-actions
- GitHub Docs — Creating a composite action: https://docs.github.com/actions/tutorials/create-actions/create-a-composite-action
- GitHub Docs — Metadata syntax for GitHub Actions: https://docs.github.com/actions/reference/workflows-and-actions/metadata-syntax
- GitHub Docs — Managing custom actions: https://docs.github.com/actions/how-tos/create-and-publish-actions/manage-custom-actions
- GitHub Docs — Releasing and maintaining actions: https://docs.github.com/actions/how-tos/create-and-publish-actions/release-and-maintain-actions
- Microsoft Learn — `dotnet tool install`: https://learn.microsoft.com/dotnet/core/tools/dotnet-tool-install
- [ADR 0001: Evaluate projects through `dotnet msbuild`](0001-msbuild-evaluation-strategy.md)
