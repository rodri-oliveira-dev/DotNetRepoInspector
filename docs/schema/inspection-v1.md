# Inspection JSON contract — schema 1.x

`DotNetRepoInspector.Core.Contracts` defines the stable inspection result independently of MSBuild internals, GitHub Actions, persistence, or any specific delivery mechanism.

The current schema version is `1.0`.

## Top-level contract

Every payload contains these properties:

- `schemaVersion`: version of the public JSON contract.
- `repository`: repository metadata. Individual metadata fields can be absent when they have not been collected yet.
- `dotNetSdk`: SDK configuration and the version resolved by the environment.
- `projects`: normalized projects, always emitted as an array.
- `diagnostics`: repository-level diagnostics, always emitted as an array.

The machine-readable schema is [`inspection-v1.schema.json`](inspection-v1.schema.json). A canonical payload is available at [`examples/inspection-v1.example.json`](examples/inspection-v1.example.json).

## Optional and absent values

The contract deliberately distinguishes absence from an explicit value:

- an omitted optional property means the value is not available, not applicable, or was not collected;
- `false` is an explicit evaluated boolean and is different from an omitted property;
- `[]` means the collection was produced and contains no entries;
- JSON `null` values are not emitted by the canonical serializer.

This distinction is particularly important for MSBuild facts such as `isTestProject` and `isPackable`, where an absent property must not be converted to `false`.

## Paths

Paths in the normalized contract use `/` separators and must not contain machine-specific absolute workspace paths.

Project paths and project-reference paths are repository-root-relative. `dotNetSdk.globalJsonPath` is relative to the inspected repository root; an applicable `global.json` in an ancestor may therefore be represented with `../` segments.

## Deterministic serialization

`InspectionJsonSerializer` canonicalizes collections before serialization:

- projects are ordered by `path`;
- project SDKs are ordered by name and version;
- target frameworks and runtime identifiers are ordered ordinally;
- project references are ordered by path;
- classification signals are ordered ordinally;
- diagnostics are ordered by severity, code, source, message, and details;
- path separators are normalized to `/`;
- property names use `camelCase`;
- optional `null` properties are omitted.

The same normalized information therefore produces byte-for-byte equivalent JSON regardless of discovery order.

## Compatibility policy

Schema versions follow a major/minor policy.

- Additive, optional fields may be introduced in a new `1.x` version.
- Consumers of schema `1.x` should ignore unknown fields and preserve the documented semantics of existing fields.
- Removing or renaming a field, changing its type, making an optional field required, or changing its meaning is a breaking change and requires a new major schema version such as `2.0`.
- `InspectionSchema.IsCompatibleVersion` accepts versions with the current major version and rejects a different major version.

## Mapping from current inspection facts

The stable contract intentionally does not expose infrastructure-specific result types.

| Source fact | Normalized contract |
| --- | --- |
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

Classification, project references, repository Git metadata, and richer diagnostics already have reserved normalized shapes and will be populated by their respective engine issues without requiring MSBuild types in the Core contract.
