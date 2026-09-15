# Security Policy

**Languages:** English | [Português (Brasil)](SECURITY.pt-BR.md)

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability that could expose secrets, private repository data, arbitrary code execution, or another user's environment.

Prefer GitHub's private vulnerability reporting / Security Advisories for this repository when the **Report a vulnerability** option is available under the repository **Security** tab. Include:

- the affected DotNetRepoInspector version or commit;
- the execution mode (CLI, .NET Tool, or GitHub Action);
- reproduction steps or a minimal repository when safe to share privately;
- the security impact and any known preconditions;
- whether credentials or private data may already have been exposed.

If private vulnerability reporting is not available, contact the maintainer through the contact methods on the GitHub profile and request a private reporting channel before sharing exploit details or secrets.

The maintainer will acknowledge a complete report as soon as practical, reproduce and assess the issue, coordinate a fix, and publish remediation information after affected users have a reasonable opportunity to update.

## Supported versions

Before the first stable public release, security fixes target the current `main` branch. After stable releases begin, the project will document supported release lines here and prioritize fixes for the latest supported major/minor versions.

## Security model

DotNetRepoInspector inspects evaluated MSBuild metadata. **MSBuild evaluation is not a sandbox.** Only inspect untrusted repositories in an isolated, ephemeral, non-privileged environment that does not contain credentials or data the inspected repository must not access.

The detailed collection scope, MSBuild trust model, GitHub Action permissions, environment hardening, logging rules, and sink credential guidance are documented in [`docs/en/security.md`](docs/en/security.md) and [`docs/pt-BR/security.md`](docs/pt-BR/security.md).

## Automated repository security controls

Repository changes are evaluated by complementary controls rather than a single scanner:

- GitHub CodeQL default setup is the authoritative source of CodeQL alerts shown under **Security > Code scanning**;
- the versioned `.github/workflows/codeql.yml` workflow independently verifies that C# can be extracted and analyzed using the repository's normal Release build contract, and retains its SARIF as a short-lived workflow artifact instead of uploading a competing CodeQL analysis;
- Dependency Review evaluates dependency changes introduced by pull requests and blocks high-severity vulnerable additions;
- Dependabot keeps package, container, SDK, and workflow dependencies current according to the versioned repository policy;
- CI, container validation, and package/release checks provide additional build and distribution safeguards.

The versioned CodeQL workflow uses read-only repository permissions, SHA-pinned actions, the SDK selected by `global.json`, and a manual build so its extraction path does not introduce a second compilation contract. Its SARIF upload to Code Scanning is intentionally disabled while GitHub CodeQL default setup remains enabled, because GitHub does not accept advanced-setup CodeQL uploads concurrently with default setup.
