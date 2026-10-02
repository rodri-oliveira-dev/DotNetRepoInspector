# Inspection JSON contract — schema 1.x

**Languages:** English | [Português (Brasil)](../../pt-BR/schema/inspection-v1.md)

`DotNetRepoInspector.Core.Contracts` defines the stable inspection result independently of MSBuild internals, GitHub Actions, persistence, or any specific delivery mechanism.

The current schema version is `1.6`.

## Top-level contract

Every payload contains these properties:

- `schemaVersion`: version of the public JSON contract.
- `repository`: repository metadata. Individual metadata fields can be absent when they are not available, not applicable, or could not be collected.
- `dotNetSdk`: SDK configuration and the version resolved by the environment.
- `projects`: normalized projects, always emitted as an array.
- `diagnostics`: repository-level diagnostics, always emitted as an array.
- `policyFindings`: optional policy evaluation results, structurally separate from inspection diagnostics. The canonical serializer always emits it as an array; older compatible `1.x` payloads may omit it.
- `integrations`: optional, normalized integration findings. The canonical `1.6` serializer always emits it as an array; older compatible `1.x` payloads may omit it.

The machine-readable schema is [`inspection-v1.schema.json`](inspection-v1.schema.json). A canonical payload is available at [`examples/inspection-v1.example.json`](examples/inspection-v1.example.json).

## Repository Git metadata

Repository metadata is normalized independently of the Git implementation:

- `name`: repository identity inferred from the `origin` remote when available, otherwise from the Git work-tree root directory name;
- `commitSha`: full commit SHA for `HEAD` when the repository has a commit;
- `branch`: short symbolic branch name when `HEAD` is attached; omitted for detached HEAD;
- `remoteUrl`: `origin` URL when available. HTTP(S) user information is removed before the value enters the normalized contract;
- `isDirty`: `true` when tracked/index/untracked working-tree changes are present, `false` when the work tree is clean, and omitted when the state could not be determined.

A directory that is not inside a Git repository remains a valid inspection target. In that case the `repository` object may contain no Git-derived properties.

## Classification and explicit overrides

`projects[].classification.kind` is the effective base classification. Normally it is produced by the deterministic classifier and `confidence` and `signals` describe the automatic decision.

Schema `1.3` added two optional fields used only when an explicit configuration override changes that effective result:

- `source`: `configuration` when the override came from the repository configuration file, or `request` when it came directly from the CLI/Action/request layer;
- `automaticKind`: the classification kind that the automatic classifier produced before the override was applied.

Schema `1.4` adds optional `subtype` as a separate refinement of `classification.kind`. `subtype` is omitted when no approved subtype evidence exists. The current engine does not implement any concrete subtype rules for Web API, MVC/Razor, Blazor, Azure Functions, or other workloads; consumers must not infer a subtype from project names, paths, source files, or the base `kind` alone.

Automatic `signals` remain in the result even when an override is active. The Inspector does not rewrite MSBuild facts to make them agree with a configured override, and automatic `confidence` is omitted rather than being presented as confidence in the manual choice.

See [`../configuration.md`](../configuration.md) for configuration format and precedence.

## Optional and absent values

The contract deliberately distinguishes absence from an explicit value:

- an omitted optional property means the value is not available, not applicable, or was not collected;
- `false` is an explicit evaluated boolean and is different from an omitted property;
- `[]` means the collection was produced and contains no entries;
- JSON `null` values are not emitted by the canonical serializer.

This distinction is particularly important for MSBuild facts such as `isTestProject` and `isPackable`, for `repository.isDirty`, for `classification.subtype` when no subtype evidence exists, and for classification provenance fields that only exist when an override is active.

## Diagnostics

Diagnostics are stable inspection facts, not operational log lines. A diagnostic contains a stable `DRIxxxx` code, one of the severities `info`, `warning`, or `error`, a human-readable stable message, and optional `source`, `details`, and `context` fields.

Automation must branch on `code` and `severity`, never on localized text. `context` contains structured, non-sensitive strings that help identify the affected component or fact. Raw child-process output, source-code content, environment variables, credentials, tokens, and other secrets must not be copied into the normalized diagnostic contract.

Diagnostic scope is represented by location in the contract. Top-level `diagnostics` contains repository/inspection-level diagnostics, while `projects[].diagnostics` contains only diagnostics for that project. Consumers must not infer a project warning/error from the CLI exit code or from a diagnostic attached to another project. Aggregate health and affected-project counts can be derived deterministically from these two scopes; see [`../diagnostics.md`](../diagnostics.md).

The stable diagnostic catalog and operational logging rules are documented in [`../diagnostics.md`](../diagnostics.md).

## Policy findings

Schema `1.5` adds top-level `policyFindings`. Policy findings represent governance decisions evaluated after normalized inspection facts have been collected; they are not inspection failures and are never inserted into repository or project `diagnostics`.

Each finding contains:

- `ruleCode`: stable `DRPxxxx` policy rule identifier;
- `severity`: `warning` or `error`;
- `message`: stable human-readable description;
- `scope`: either `repository` or `project`; project scope also carries the normalized repository-relative `projectPath`;
- optional `context`: structured string values describing the evaluated policy facts.

The initial TargetFramework policy uses rule code `DRP0001`. An empty `policyFindings` array means either no enabled policy produced a finding or no policy was enabled; consumers that need to distinguish configuration intent should use the repository configuration as the source of truth.

## Integration findings

Schema `1.6` adds the optional top-level `integrations` collection. It is populated only when Integration Discovery is explicitly enabled; the default inspection path does not read source files. Each finding carries a deterministic `id`, project-relative provenance (`projectPath`, `source.path`, and one-based `source.line`), `kind`, `direction`, recognized `technology`, `confidence`, and deterministic `signals`. Safe logical evidence can additionally include `target`, `resourceType`, `configurationKey`, and `contract`.

Every newly serialized report also includes `integrationDiscovery`: `enabled=false, completed=false` means the capability was not run; `enabled=true, completed=true` with an empty `integrations` array means it ran and found nothing. `truncated=true` records that a configured safety budget was reached. Older compatible v1 payloads without this additive metadata deserialize as not executed.

The contract never contains source bodies, payloads, request or message bodies, queries, connection strings, credentials, authentication headers, configuration values, or arbitrary evaluated properties. `configurationKey` identifies a key only; it never contains the corresponding value. Detectors must omit evidence that cannot be represented safely and use conservative confidence rather than inventing a remote endpoint or runtime topology.

## Paths

Paths in the normalized contract use `/` separators and must not contain machine-specific absolute workspace paths.

Project paths and project-reference paths are repository-root-relative. `dotNetSdk.globalJsonPath` is relative to the inspected repository root; an applicable `global.json` in an ancestor may therefore be represented with `../` segments.

The Git work-tree root discovered by the Git adapter is an internal operational value and is not serialized into the public inspection contract.

## Deterministic serialization

`InspectionJsonSerializer` canonicalizes collections before serialization:

- projects are ordered by `path`;
- project SDKs are ordered by name and version;
- target frameworks and runtime identifiers are ordered ordinally;
- project references are ordered by path;
- classification signals are ordered ordinally;
- diagnostics are ordered by severity, code, source, message, details, and canonical context;
- diagnostic context keys are ordered ordinally;
- policy findings are ordered by rule code, severity, scope, project path, message, and canonical context;
- policy finding context keys are ordered ordinally;
- integration findings are ordered by project path, source path and line, kind, direction, technology, target, and id;
- integration signals are deduplicated and ordered ordinally;
- path separators are normalized to `/`;
- property names use `camelCase`;
- optional `null` properties are omitted.

The same normalized information therefore produces byte-for-byte equivalent JSON regardless of discovery order.

## Compatibility policy

Schema versions follow a major/minor policy.

- Additive, optional fields may be introduced in a new `1.x` version.
- Schema `1.1` added the optional diagnostic `context` object and constrained diagnostic severity to the documented vocabulary.
- Schema `1.2` added the optional `repository.isDirty` boolean populated by Git metadata inspection.
- Schema `1.3` added optional `classification.source` and `classification.automaticKind` fields so explicit classification overrides remain distinguishable from automatic classification.
- Schema `1.4` adds optional `classification.subtype` as a refinement of the base classification kind without adding concrete subtype detection rules.
- Schema `1.5` adds top-level `policyFindings` so policy violations remain structurally separate from inspection diagnostics.
- Schema `1.6` adds optional top-level `integrations` for opt-in, source-derived integration evidence.
- Consumers of schema `1.x` should ignore unknown fields and preserve the documented semantics of existing fields.
- Removing or renaming a field, changing its type, making an optional field required, or changing its meaning is a breaking change and requires a new major schema version such as `2.0`.
- `InspectionSchema.IsCompatibleVersion` accepts versions with the current major version and rejects a different major version.

## Mapping from current inspection facts

The stable contract intentionally does not expose infrastructure-specific result types.

| Source fact | Normalized contract |
| --- | --- |
| Git repository identity | `repository.name` |
| Git `HEAD` commit | `repository.commitSha` |
| Git symbolic `HEAD` | `repository.branch` |
| Git `origin` remote | `repository.remoteUrl` after credential sanitization |
| Git working-tree state | `repository.isDirty` |
| SDK `GlobalJsonPath` | `dotNetSdk.globalJsonPath` after path normalization |
| SDK configured `Version` | `dotNetSdk.configured.version` |
| SDK configured `RollForward` | `dotNetSdk.configured.rollForward` |
| SDK configured `AllowPrerelease` | `dotNetSdk.configured.allowPrerelease` |
| SDK `ResolvedSdkVersion` | `dotNetSdk.resolvedVersion` |
| Project `ResolvedSdkVersion` | `projects[].resolvedSdkVersion` |
| Project `DeclaredProjectSdks` | `projects[].sdks` |
| Project `TargetFrameworks` | `projects[].targetFrameworks` |
| Project `OutputType` | `projects[].outputType` |
| Project `IsTestProject` | `projects[].isTestProject` |
| Project `IsPackable` | `projects[].isPackable` |
| Project `RuntimeIdentifiers` | `projects[].runtimeIdentifiers` |
| Automatic classifier result | `projects[].classification.kind` when no override is active; otherwise `projects[].classification.automaticKind` |
| Approved subtype evidence | optional `projects[].classification.subtype` when supported evidence exists |
| Explicit classification override | effective `projects[].classification.kind` plus `projects[].classification.source` |
| Policy rule evaluation | top-level `policyFindings[]`, separate from inspection diagnostics |
| Opt-in source analysis | top-level `integrations[]`, containing only bounded, normalized evidence |

The raw MSBuild `Properties` dictionary from the evaluation layer is intentionally excluded. It is an internal evidence source, not part of the stable public contract.

Classification, project references, and repository Git metadata retain their normalized shapes and are populated by their respective engine components without requiring infrastructure-specific types in the Core contract.
