# Compatibility policy

**Languages:** English | [Português (Brasil)](../pt-BR/compatibility.md)

This document defines what DotNetRepoInspector currently guarantees across .NET SDK versions and operating systems.

## Inspector runtime

DotNetRepoInspector itself is built for **`net10.0`**. The repository `global.json` selects a stable .NET 10 SDK (`10.0.100` with `latestFeature` roll-forward and prerelease disabled).

The runtime used to execute the Inspector is independent from the SDK selected by the repository being inspected. A `net10.0` Inspector can inspect a repository whose projects target `net8.0` as long as the SDK required to evaluate that repository is installed.

## Mandatory support matrix

The following combinations are part of the required CI compatibility gate:

| Host OS | Inspector runtime | Target SDK family | Target framework | Guarantee |
| --- | --- | --- | --- | --- |
| Linux | `net10.0` | .NET 8 | `net8.0` | Required |
| Linux | `net10.0` | .NET 10 | `net10.0` | Required |
| Windows | `net10.0` | .NET 8 | `net8.0` | Required |
| Windows | `net10.0` | .NET 10 | `net10.0` | Required |
| macOS | `net10.0` | .NET 8 | `net8.0` | Required |
| macOS | `net10.0` | .NET 10 | `net10.0` | Required |

CI installs .NET 8 and .NET 10 SDKs side by side and proves that the inspected repository's `global.json` controls SDK resolution independently from the Inspector runtime.

`net9.0` and other SDK/TFM combinations are not part of the mandatory matrix. They may be inspectable when a compatible SDK is installed, but they are not a release-gated compatibility guarantee until added to this matrix.

## What "supported" means

Support means that DotNetRepoInspector can:

1. discover supported project files;
2. resolve the repository SDK according to `global.json` and normal `dotnet` SDK resolution rules;
3. evaluate the project with `dotnet msbuild`;
4. extract the normalized facts used by the inspection contract;
5. produce deterministic JSON and structured diagnostics.

Support **does not** mean that the Inspector restores, builds, tests, publishes, or runs the inspected application. A project may therefore be inspectable even when application-specific workloads or runtime dependencies required for a full build are unavailable.

## SDK availability

The SDK required by the inspected repository must be installed on the host when its `global.json` cannot roll forward to another compatible installed SDK.

If SDK resolution fails, the Inspector keeps the failure machine-readable and emits diagnostic **`DRI1002` (`DotNetSdkUnavailable`)** with `error` severity. The CLI can still produce a report for this partial inspection and returns its documented non-zero partial-result exit code.

## Paths, casing, and line endings

Machine-readable project paths are repository-relative and normalized to `/` separators, including on Windows.

Path casing is preserved. DotNetRepoInspector does not case-fold public paths; filesystem lookup behavior still follows the host operating system. Tests therefore use exact repository-relative casing rather than assuming that all hosts are case-insensitive.

Both LF and CRLF project/configuration files are valid inspection inputs. The repository itself normalizes tracked project/configuration files, but inspected repositories are not required to use the same line-ending policy.

## Preview SDKs

Preview .NET SDKs are **not** part of the mandatory compatibility matrix.

A preview SDK may be used on a best-effort basis when all of the following are true:

- the preview SDK is explicitly installed on the host;
- the inspected repository selects it through normal `global.json` semantics;
- prerelease SDK resolution is explicitly allowed when required.

Preview behavior is not a release gate and may change with upstream SDK/MSBuild previews. A preview becomes guaranteed only when it is intentionally added to the CI matrix.

## CI validation

The `Validate .NET` workflow contains a cross-platform compatibility matrix for Ubuntu, Windows, and macOS. Each matrix entry:

- installs .NET 8 and the Inspector's .NET 10 SDK side by side;
- builds the Inspector with .NET 10;
- inspects synthetic `net8.0` and `net10.0` repositories;
- verifies the SDK selected for each inspected repository;
- verifies normalized path separators and preserved casing;
- verifies a consistent `DRI1002` diagnostic for an unavailable SDK;
- evaluates a temporary project written with CRLF line endings.
