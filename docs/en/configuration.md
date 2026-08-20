# Inspection configuration

**Languages:** English | [Português (Brasil)](../pt-BR/configuration.md)

DotNetRepoInspector remains zero-configuration by default. A repository path is enough to run an inspection. Optional configuration exists for monorepos, generated trees, samples, and the small set of cases where a consumer intentionally needs to override the automatic classification result.

## Default configuration file

When present at the inspected repository root, `.dotnetrepoinspector.json` is loaded automatically:

```json
{
  "schemaVersion": "1",
  "exclude": [
    "generated",
    "samples/Legacy.csproj"
  ],
  "classificationOverrides": {
    "src/App/App.csproj": "web"
  }
}
```

`schemaVersion` is required and the current configuration schema is `1`. Unknown properties are rejected so misspelled configuration does not silently change inspection behavior.

All configured paths are relative to the inspected repository root and must remain inside that root. Absolute paths and paths that escape through `..` are invalid. Paths use repository-relative semantics; `/` is recommended in versioned configuration.

### Exclusions

`exclude` is an optional array. Each entry may identify:

- a directory, in which case its entire subtree is skipped during project discovery;
- an exact project path, in which case that project is removed from the discovered project set.

The initial configuration contract intentionally does not implement glob or regular-expression matching. Exact repository-relative paths make the behavior deterministic and portable across runners.

Built-in discovery exclusions such as normal build-output directories remain in effect independently of this file.

### Classification overrides

`classificationOverrides` is an optional object whose keys are repository-relative project paths and whose values are one of:

- `web`
- `worker`
- `console`
- `library`
- `test`
- `unknown`

An override changes only the **effective interpretation of the project classification**. It does not alter SDKs, target frameworks, `OutputType`, test metadata, packability, runtime identifiers, references, or any other fact collected through MSBuild.

When an override is applied, schema `1.3` makes it distinguishable from automatic classification:

```json
"classification": {
  "kind": "web",
  "signals": [
    "output-type:library"
  ],
  "source": "configuration",
  "automaticKind": "library"
}
```

The automatic signals remain present. `automaticKind` records the classifier's original result, `kind` contains the effective override, `source` identifies where the override came from, and automatic `confidence` is not reused as confidence for a manual decision.

If an override references a project that is not discovered, the inspection continues and emits `DRI1014` with severity `warning`. This makes stale configuration visible without turning it into an inspection failure.

## CLI configuration

The CLI exposes the same concepts directly:

```bash
dotnet repo-inspect . \
  --exclude generated \
  --exclude samples/Legacy.csproj \
  --classify src/App/App.csproj=web
```

Use a non-default configuration file with:

```bash
dotnet repo-inspect . --config config/inspector.json
```

Disable automatic loading of `.dotnetrepoinspector.json` with:

```bash
dotnet repo-inspect . --no-config
```

`--config` and `--no-config` cannot be used together. `--exclude` and `--classify` are repeatable.

## GitHub Action configuration

The reusable Action exposes the same concepts. `exclude` and `classify` accept newline-separated values:

```yaml
- name: Inspect .NET repository
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    exclude: |
      generated
      samples/Legacy.csproj
    classify: |
      src/App/App.csproj=web
```

A custom config file can be supplied through `config`; `no-config: "true"` disables automatic loading of the default file.

The Action forwards these values to the same CLI/Engine configuration contract. It does not implement a second configuration parser or classification layer.

## Precedence

Configuration is resolved deterministically:

1. built-in Inspector behavior provides the zero-config baseline;
2. `.dotnetrepoinspector.json`, or the explicit file selected by `--config` / Action `config`, contributes exclusions and classification overrides;
3. direct request values from CLI/Action are applied last.

Exclusions are additive: direct `--exclude` / Action `exclude` values are combined with file exclusions.

For classification, a direct `--classify` / Action `classify` entry for the same project replaces the file's entry. In the resulting JSON its `classification.source` is `request`; a file-only override uses `configuration`.

`--no-config` / Action `no-config` removes the file layer entirely. Direct exclusions and classification overrides still apply.

## Invalid configuration

Invalid repository configuration is represented in the normal inspection contract as `DRI1013` with severity `error`. Examples include:

- invalid JSON;
- unsupported configuration `schemaVersion`;
- unknown configuration properties;
- an explicit config file that does not exist;
- an absolute or escaping configured path;
- an unsupported classification kind;
- conflicting `--config` and `--no-config` semantics at the Engine boundary.

The Engine returns an `InspectionReport` containing the diagnostic instead of throwing away the machine-readable result. The CLI therefore exits with code `1` and the GitHub Action preserves the same exit code while exposing the report path when available.

Command-line syntax errors detected before the Engine, such as a malformed `--classify` value, remain invalid CLI arguments and exit with code `2`.
