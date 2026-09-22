# v1.0.0 release readiness

**Languages:** English | [Português (Brasil)](../pt-BR/v1-release-readiness.md)

DotNetRepoInspector **v1.0.0 has been published**. This document is retained as the historical release-readiness baseline that defined the requirements for that first stable public release and continues to document the v1 contract.

The public GitHub Action refs created from that release line are available, including `v1`, `v1.0`, and `v1.0.0`. Later compatible v1 releases may advance the movable aliases while immutable full-version tags remain fixed.

## v1 baseline

| Surface | v1.0.0 baseline |
| --- | --- |
| Product version | `1.0.0` |
| Inspection schema | `1.3` (schema major `1`) |
| NuGet package | `DotNetRepoInspector` |
| .NET Tool command | `dotnet-repo-inspect` / `dotnet repo-inspect` |
| MCP NuGet package | `DotNetRepoInspector.Mcp` (`DotnetTool`, `McpServer`) |
| MCP Tool command | `dotnet-repo-inspector-mcp` / `dnx DotNetRepoInspector.Mcp@<version>` |
| Tool runtime | `net10.0` |
| GitHub Action stable alias | `v1` |
| GitHub Action immutable tag | `v1.0.0` |
| GitHub Action minor alias | `v1.0` |
| License | MIT |

The machine-readable counterpart of this table is `.github/release-readiness-v1.json`. Repository tests compare that baseline with `action.yml`, `InspectionSchema`, the CLI package metadata, the canonical schema example, and the required governance/security files.

The same manifest recognizes `DotNetRepoInspector.Mcp` as a release-ready, framework-dependent package with stdio, explicit root boundary, read-only, hermetic E2E, security, performance, `McpServer`, embedded `.mcp/server.json`, symbols, and packaged-tool smoke controls. The historical CLI/Action `v1.0.0` release has been published; the separate `DotNetRepoInspector.Mcp` package is prepared for publication but has **not** yet been published to NuGet.org. See [MCP RC readiness](mcp-release-candidate.md) for its own release status.

## Public contract included in v1

The first stable release includes these supported surfaces:

- repository/project discovery based on evaluated .NET/MSBuild metadata;
- base classification: Web, Worker, Console, Library, Test, and Unknown;
- normalized project references and Git repository metadata;
- versioned inspection JSON, with `schemaVersion 1.3` as the first v1 release baseline;
- optional `.dotnetrepoinspector.json`, exclusions, and explicit classification overrides;
- CLI/.NET Tool with deterministic stdout/stderr separation and documented exit codes;
- reusable Composite GitHub Action using the same .NET Tool;
- optional HTTP/webhook snapshot persistence with provenance and idempotency;
- compatibility validation for .NET 8/10 target repositories on Ubuntu, Windows, and macOS;
- security/privacy boundaries, OSS governance, real-repository validation, and performance guardrails.

Application subtypes and the optional policy engine remain post-v1 work. They are not part of the v1.0.0 compatibility promise.

## Compatibility boundaries

The product follows the versioning policy in [`releases.md`](releases.md).

For the v1 line:

- additive inspection-contract changes may advance schema `1.x` and require an appropriate product MINOR release;
- breaking inspection-schema changes require schema major `2` and therefore a product/Action major release rather than moving the `v1` alias;
- breaking CLI or GitHub Action contracts also require a product major release;
- a stable `v1` Action alias must never point to a release whose public contract belongs to product major `2`.

The current canonical JSON example is [`schema/examples/inspection-v1.example.json`](schema/examples/inspection-v1.example.json).

## Automated readiness gate

`tests/DotNetRepoInspector.Cli.Tests/ReleaseReadinessTests.cs` validates the v1 baseline during the normal test suite. Because both `Validate .NET` and the protected Release workflow run the full test suite, the same gate is enforced in normal CI and release dry-runs.

The gate verifies:

1. the product version in `.github/release-readiness-v1.json` defines the initial v1 baseline, while official release versions are supplied and validated by the Release workflow;
2. product major, Action major alias, and inspection schema major are aligned for the v1 baseline;
3. `InspectionSchema.CurrentVersion` is exactly the baseline schema version;
4. the CLI project remains a packable .NET Tool with the expected package ID, command, target framework, license, README, and repository URL;
5. the canonical schema example advertises the same `schemaVersion`;
6. required license, security, contribution, conduct, issue/PR templates, and release documentation are present;
7. the MCP project remains a packable .NET Tool and `McpServer` with the expected package identity, command, manifest, and framework-dependent strategy;
8. the public READMEs no longer contain pre-v1 statements that describe the schema as hypothetical or not final.

This gate does not validate external account configuration on GitHub or NuGet.org; those checks remain administrative prerequisites.

## Repository-side checks before publication

Before starting the official release, confirm on `main`:

- `Validate .NET` is green;
- the Release workflow dry-run is green for `1.0.0`;
- build/analyzers have zero warnings and errors;
- the complete test suite passes;
- package validation installs the exact `DotNetRepoInspector.1.0.0.nupkg` globally and locally and verifies `--help`, `--version`, and a real inspection;
- MCP validation inspects the exact `.nupkg`/`.snupkg`, installs the tool, resolves it through local-source `dnx`, and performs a real stdio `inspect_repository` call;
- the release candidate contains `release-manifest.json` and `SHA256SUMS`;
- the manifest points to the exact release commit and reports schema `1.3`;
- GitHub Action and compatibility smoke tests are green on Ubuntu, Windows, and macOS.

A manual safe dry-run can be started with **Actions → Release → Run workflow**, version `1.0.0`, `publish=false`. The publication job must be skipped.

## Historical administrative prerequisites for the first publication

For the original v1.0.0 publication, these account-level prerequisites were intentionally outside repository code:

1. Create a protected GitHub Environment `release`.
2. Require an approval for that environment and restrict deployment to `main` as appropriate for the repository.
3. Define `NUGET_USER` as a repository/environment variable with the NuGet.org account used for publishing.
4. On NuGet.org, configure **Trusted Publishing** for both `DotNetRepoInspector` and `DotNetRepoInspector.Mcp`, owner `rodri-oliveira-dev`, repository `DotNetRepoInspector`, workflow file `release.yml`, and preferably environment `release`.
5. Confirm that both package IDs, especially the new `DotNetRepoInspector.Mcp`, are available/owned by the intended NuGet account before the first publication.

No long-lived NuGet API key should be added to GitHub Secrets. The workflow uses OIDC/Trusted Publishing.

## Historical v1.0.0 publication procedure

The original protected publication procedure was:

1. open **Actions → Release → Run workflow** on `main`;
2. enter version `1.0.0`;
3. set `publish=true`;
4. approve the protected `release` environment;
5. allow the workflow to publish the exact candidate built in the same run.

The workflow derives tag `v1.0.0` automatically from version `1.0.0`. The protected workflow is responsible for creating the immutable `v1.0.0` release/tag, publishing the NuGet package, publishing the GitHub Release, generating attestations, and only then moving the stable Action aliases `v1` and `v1.0`.

## v1.0.0 post-publication verification record

The release was designed to be verified independently after the workflow succeeded:

```bash
dotnet tool install --global DotNetRepoInspector --version 1.0.0
dotnet repo-inspect --version
```

The reported version must be `1.0.0`.

Also verify:

- GitHub Release `v1.0.0` is public and points to the intended commit;
- release assets contain the package, manifest, and checksums;
- provenance/attestations are present;
- `v1` and `v1.0` resolve to the same commit as `v1.0.0`;
- a disposable workflow can execute `uses: rodri-oliveira-dev/DotNetRepoInspector@v1` successfully;
- the NuGet package page exposes the expected MIT license, README, repository link, and version.

Only after these checks should the roadmap #30 be considered to have satisfied its final “versioned and reproducible release” criterion.

## Partial failure

GitHub Release publication, NuGet publication, and moving Action aliases are not a transaction. If the protected workflow fails after any irreversible step, follow the recovery rules in [`releases.md`](releases.md). Never retarget the immutable `v1.0.0` tag to a different commit.
