# Inspection configuration

**Languages:** English | [Português (Brasil)](../pt-BR/configuration.md)

DotNetRepoInspector remains zero-configuration by default. A repository path is enough to run an inspection. Optional configuration exists for monorepos, generated trees, samples, and the small set of cases where a consumer intentionally needs to override the automatic classification result.

## Default configuration file

When present at the inspected repository root, `.dotnetrepoinspector.json` is loaded automatically:

```json
{
  "schemaVersion": "2",
  "exclude": [
    "generated",
    "samples/Legacy.csproj"
  ],
  "classificationOverrides": {
    "src/App/App.csproj": "web"
  },
  "integrationDiscovery": {
    "enabled": false
  },
  "policies": {
    "targetFramework": {
      "enabled": true,
      "allowed": ["net8.0", "net10.0"],
      "severity": "error"
    }
  }
}
```

`schemaVersion` is required. Configuration schema `1` remains supported for exclusions and classification overrides; schema `2` is the current version and adds the optional `policies` and `integrationDiscovery` sections. Either section in schema `1` is rejected instead of being interpreted implicitly. Unknown properties are rejected so misspelled configuration does not silently change inspection behavior.

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

When an override is applied, schema `1.3` and later makes it distinguishable from automatic classification:

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

## Integration Discovery

Integration Discovery is disabled by default. Schema `2` can enable the same bounded, syntax-only capability exposed by the CLI and Action:

```json
{
  "schemaVersion": "2",
  "integrationDiscovery": {
    "enabled": true
  }
}
```

The scanner reads supported C# source files but does not execute repository code or use the network. Its normalized findings exclude source snippets, payloads, queries, connection strings, and credentials.

## Policies

Policies are opt-in. With no configuration file, with no `policies` section, or with `targetFramework.enabled: false`, no policy rule is registered and inspection behavior is unchanged.

Configuration schema `2` introduces the first rule:

```json
{
  "schemaVersion": "2",
  "policies": {
    "targetFramework": {
      "enabled": true,
      "allowed": ["net8.0", "net10.0"],
      "severity": "error"
    }
  }
}
```

The TargetFramework rule has stable code `DRP0001`. Its default severity is `error`; `severity` may explicitly be `warning` or `error`. When enabled, `allowed` must contain at least one non-empty target framework.

The rule evaluates only normalized `ProjectInspection.TargetFrameworks` facts:

- a single-target project produces no finding when its TFM is allowed and one project-scoped finding when it is not;
- a multi-target project produces one project-scoped finding when any TFM is outside the allowlist; the finding context lists all target frameworks and the disallowed subset in deterministic order;
- a project with no known target framework produces no policy finding because the rule does not invent a violation from missing inspection facts;
- projects are evaluated in repository-relative path order, so finding order is deterministic.

Policy findings are distinct from inspection diagnostics. Enabled rules are evaluated after the normalized inspection report is built, and their results are emitted as top-level `policyFindings` in the current inspection schema `1.6` (the field was introduced in `1.5`). The CLI and GitHub Action return exit code `1` when any policy finding has severity `error`; policy warnings do not fail an otherwise healthy inspection.

## CLI configuration

The CLI exposes the same concepts directly:

```bash
dotnet repo-inspect . \
  --exclude generated \
  --exclude samples/Legacy.csproj \
  --classify src/App/App.csproj=web \
  --discover-integrations
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
    discover-integrations: "true"
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

For Integration Discovery, direct CLI/MCP/Action opt-in takes precedence over the file. If no direct value exists, `integrationDiscovery.enabled` is used; if neither exists, the effective value is `false`.

`--no-config` / Action `no-config` removes the file layer entirely. Direct exclusions and classification overrides still apply.

## Invalid configuration

Invalid repository configuration is represented in the normal inspection contract as `DRI1013` with severity `error`. Examples include:

- invalid JSON;
- unsupported configuration `schemaVersion`;
- unknown configuration properties;
- an explicit config file that does not exist;
- an absolute or escaping configured path;
- an unsupported classification kind;
- conflicting `--config` and `--no-config` semantics at the Engine boundary;
- a `policies` section under legacy schema `1`;
- an `integrationDiscovery` section under legacy schema `1`, or one without `enabled`;
- missing `targetFramework.enabled`, an enabled rule without an `allowed` list, blank allowed TFMs, or an unsupported policy severity.

The Engine returns an `InspectionReport` containing the diagnostic instead of throwing away the machine-readable result. The CLI therefore exits with code `1` and the GitHub Action preserves the same exit code while exposing the report path when available.

Command-line syntax errors detected before the Engine, such as a malformed `--classify` value, remain invalid CLI arguments and exit with code `2`.
