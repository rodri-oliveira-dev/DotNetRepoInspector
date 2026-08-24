# Releases and versioning

**Languages:** English | [Português (Brasil)](../pt-BR/releases.md)

DotNetRepoInspector uses one product version for the .NET Tool and the reusable GitHub Action. Official publication is deliberately separate from normal CI and is performed only through the protected `Release` workflow.

## Product version

Product releases follow [Semantic Versioning 2.0.0](https://semver.org/):

- **PATCH** fixes behavior without breaking a public contract;
- **MINOR** adds backward-compatible product behavior or public contract capabilities;
- **MAJOR** is required for breaking CLI, Action, or inspection-contract behavior.

Prerelease identifiers such as `1.1.0-rc.1` are supported. Build metadata (`+...`) is intentionally not accepted for official product releases so the NuGet version, Git tag, release title, and Action version remain identical and unambiguous.

The exact product version entered in the protected Release workflow is authoritative for the NuGet package, manifest, GitHub Release, and immutable tag. The workflow validates it as Semantic Versioning and derives the tag by prefixing the version with `v`. A full immutable Action ref such as `v1.4.2` selects that exact package directly. For moving aliases such as `v1` or `v1.4`, and for full commit SHAs, the Action resolves the ref to its release commit, requires exactly one immutable full Semantic Version tag on that commit, and installs that exact package. Unreleased or ambiguous refs are rejected instead of falling back to a wildcard or latest version.

## Product version versus `schemaVersion`

The product version and the inspection JSON `schemaVersion` are related compatibility contracts, but they are not the same number.

- implementation fixes that do not change the JSON contract may be a product PATCH and keep `schemaVersion` unchanged;
- additive, backward-compatible inspection-contract changes increment the schema minor version and require at least a product MINOR release;
- a breaking inspection-contract change increments the schema major version and requires a product MAJOR release;
- a breaking GitHub Action input/output change also requires a product/Action MAJOR release, even when the JSON schema itself is unchanged.

A moving Action major tag such as `v1` must never cross from schema major `1` to schema major `2`.

## GitHub Action tags

For a stable release `1.4.2`, publication uses:

- immutable full tag `v1.4.2`;
- moving major alias `v1`;
- moving minor alias `v1.4`.

The immutable tag identifies the exact release commit. The moving aliases are updated only after the exact NuGet package has been published and the GitHub Release is ready to publish.

Prereleases such as `1.5.0-rc.1` receive only the immutable full tag. They never move stable major/minor aliases.

Consumers prioritizing convenience can use `@v1`; consumers prioritizing maximum reproducibility should use `@v1.4.2` or a full commit SHA.

## Release artifacts

Every release build produces the same validated candidate set before publication:

- `DotNetRepoInspector.<version>.nupkg`;
- `release-manifest.json`;
- `SHA256SUMS`.

The manifest records:

- product version and immutable tag;
- exact 40-character source commit SHA;
- `schemaVersion` observed from a real packaged-tool smoke inspection;
- SHA-256 of the `.nupkg`;
- Action aliases that are eligible to move for the version.

The release build passes the package through the existing .NET Tool validator, including metadata/content checks, global and local installation, `--help`, `--version`, and a real repository inspection. The package is therefore validated before it can enter the publication job.

## Normal pull requests

Normal validation never has package-publication credentials or release write permissions.

When release/packaging automation itself changes, `.github/workflows/release.yml` also runs on the pull request in **dry-run mode**. It performs restore, formatting verification, build, tests, exact-version pack, package smoke validation, manifest/checksum generation, and artifact upload. The publication job is skipped because it can run only for an explicit `workflow_dispatch` request with `publish=true`.

The normal `Validate .NET` workflow continues to produce its CI-versioned `.nupkg` independently and never publishes it.

## Protected publication setup

Before the first official release, repository maintainers must configure a GitHub Environment named **`release`**. Recommended protection:

1. require at least one maintainer approval;
2. restrict deployment branches/tags so publication is initiated from `main` only;
3. define environment/repository variable `NUGET_USER` with the NuGet.org account name used by Trusted Publishing.

NuGet.org must also contain a Trusted Publishing policy for package `DotNetRepoInspector` that trusts this repository, the `release.yml` workflow, and preferably the `release` environment.

No long-lived NuGet API key belongs in GitHub Secrets. The publication job requests an OIDC identity token and `NuGet/login` exchanges it for a short-lived NuGet API key.

## Starting an official release

Publication is intentionally a maintainer action, not a side effect of merging a PR.

1. Choose the next Semantic Version according to the compatibility rules above.
2. Merge only after the normal CI and release dry-run are green.
3. Open **Actions → Release → Run workflow** on `main`.
4. Enter the exact version to publish.
5. Set `publish` to `true`.
6. Approve the protected `release` environment when prompted.

The workflow derives the immutable release tag automatically by prefixing the validated version with `v` (for example, version `1.4.2` produces tag `v1.4.2`). A workflow dispatch with `publish=false` is a safe manual dry-run and never enters the publication job.

## Publication sequence

The protected job intentionally orders irreversible operations:

1. download and re-verify the exact artifact produced by the build job;
2. create GitHub artifact/SLSA provenance attestations for the package, manifest, and checksum file;
3. create or resume a **draft** GitHub Release for the immutable full version tag and attach the artifacts;
4. authenticate to NuGet.org using Trusted Publishing/OIDC;
5. publish the exact `.nupkg` with duplicate-safe behavior;
6. publish the GitHub Release;
7. for stable releases only, move `v<major>` and `v<major>.<minor>` to the release commit.

This ordering prevents a stable moving Action alias from pointing to a release whose NuGet package was not published successfully.

## Partial failure and reruns

Publishing across GitHub and NuGet.org is not transactional. The workflow therefore supports a narrow recovery path:

- an existing full version tag is accepted only when it resolves to the same release commit;
- an existing GitHub Release may be resumed only while it is still a draft;
- release assets are re-uploaded with replacement enabled;
- NuGet publication uses duplicate-safe behavior;
- an already published GitHub Release is treated as immutable and the workflow refuses to mutate it;
- stable aliases move only at the final successful stage.

If a partial release needs human repair, maintainers should inspect the draft release, NuGet package state, manifest commit, and workflow logs before rerunning. Never retarget an immutable full version tag to another commit.

## Release notes and changelog

GitHub Releases are the canonical product changelog. `.github/release.yml` categorizes GitHub-generated release notes into breaking changes, features, fixes, security, documentation, dependencies, and other changes. A PR can opt out of generated notes with the `skip-changelog` label.

Release notes may be edited while the release is still a draft. Once published, the immutable full tag and attached release evidence define what was shipped.

## Provenance and signing

Official release assets are covered by GitHub artifact attestations generated with `actions/attest`, providing build provenance bound to the release workflow identity and subjects' digests. NuGet publication uses OIDC Trusted Publishing rather than a stored API key.

Certificate-backed NuGet author signing is **not** introduced by the initial release automation. It requires a separate certificate/key lifecycle, renewal and incident-recovery design. It can be added later without changing the product versioning model. The current release evidence is the immutable source tag/commit, release manifest/checksums, GitHub provenance attestation, and NuGet Trusted Publishing identity.

## Permissions

The workflow defaults to `contents: read`. Only the protected publication job receives the additional permissions required to create tags/releases and attestations:

- `contents: write`;
- `id-token: write`;
- `attestations: write`;
- `artifact-metadata: write`.

Pull-request code never executes with those publication permissions.

## Related documentation

- [GitHub Action](github-action.md)
- [CLI / .NET Tool](cli.md)
- [Inspection schema](schema/inspection-v1.md)
- [Security and privacy](security.md)
- [ADR 0002: GitHub Action distribution strategy](decisions/0002-github-action-distribution-strategy.md)
