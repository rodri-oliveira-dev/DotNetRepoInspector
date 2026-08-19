# Inspection JSON contract — schema 1.x

`DotNetRepoInspector.Core.Contracts` defines the stable inspection result independently of MSBuild internals, GitHub Actions, persistence, or any specific delivery mechanism.

The current schema version is `1.2`.

## Top-level contract

Every payload contains these properties:

- `schemaVersion`: version of the public JSON contract.
- `repository`: repository metadata. Individual metadata fields can be absent when they are not available, not applicable, or could not be collected.
- `dotNetSdk`: SDK configuration and the version resolved by the environment.
- `projects`: normalized projects, always emitted as an array.
- `diagnostics`: repository-level diagnostics, always emitted as an array.

The machine-readable schema is [`inspection-v1.schema.json`](inspection-v1.schema.json). A canonical payload is available at [`examples/inspection-v1.example.json`](examples/inspection-v1.example.json).

## Repository Git metadata

Repository metadata is normalized independently of the Git implementation:

- `name`: repository identity inferred from the `origin` remote when available, otherwise from the Git work-tree root directory name;
- `commitSha`: full commit SHA for `HEAD` when the repository has a commit;
- `branch`: short symbolic branch name when `HEAD` is attached; omitted for detached HEAD;
- `remoteUrl`: `origin` URL when available. HTTP(S) user information is removed before the value enters the normalized contract;
- `isDirty`: `true` when tracked/index/untracked working-tree changes are present, `false` when the work tree is clean, and omitted when the state could not be determined.

A directory that is not inside a Git repository remains a valid inspection target. In that case the `repository` object may contain no Git-derived properties.

## Optional and absent values

The contract deliberately distinguishes absence from an explicit value:

- an omitted optional property means the value is not available, not applicable, or was not collected;
- `false` is an explicit evaluated boolean and is different from an omitted property;
- `[]` means the collection was produced and contains no entries;
- JSON `null` values are not emitted by the canonical serializer.

This distinction is particularly important for MSBuild facts such as `isTestProject` and `isPackable`, and for `repository.isDirty`, where an absent property must not be converted to `false`.

## Diagnostics

Diagnostics are stable inspection facts, not operational log lines. A diagnostic contains a stable `DRIxxxx` code, one of the severities `info`, `warning`, or `error`, a human-readable stable message, and optional `source`, `details`, and `context` fields.

Automation must branch on `code` and `severity`, never on localized text. `context` contains structured, non-sensitive strings that help identify the affected component or fact. Raw child-process output, source-code content, environment variables, credentials, tokens, and other secrets must not be copied into the normalized diagnostic contract.

The stable diagnostic catalog and operational logging rules are documented in [`../diagnostics.md`](../diagnostics.md).

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
- path separators are normalized to `/`;
- property names use `camelCase`;
- optional `null` properties are omitted.

The same normalized information therefore produces byte-for-byte equivalent JSON regardless of discovery order.

## Compatibility policy

Schema versions follow a major/minor policy.

- Additive, optional fields may be introduced in a new `1.x` version.
- Schema `1.1` added the optional diagnostic `context` object and constrained diagnostic severity to the documented vocabulary.
- Schema `1.2` adds the optional `repository.isDirty` boolean populated by Git metadata inspection.
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

The raw MSBuild `Properties` dictionary from the evaluation layer is intentionally excluded. It is an internal evidence source, not part of the stable public contract.

Classification, project references, and repository Git metadata retain their normalized shapes and are populated by their respective engine issues without requiring infrastructure-specific types in the Core contract.
