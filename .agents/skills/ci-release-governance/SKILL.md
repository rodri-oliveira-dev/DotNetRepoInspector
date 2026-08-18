---
name: ci-release-governance
description: Use this skill when designing or reviewing DotNetRepoInspector CI, release/versioning, package publication, GitHub Action distribution, permissions, artifacts, or automated quality gates. Do not use it for ordinary production-code changes with no pipeline impact.
---

# CI and Release Governance

## Goals

Keep build, test, package, and release automation reproducible, least-privileged, and consistent with the repository's published contracts.

## Use for

- `.github/workflows/` design;
- NuGet/.NET tool packaging;
- GitHub Action packaging and version tags;
- release/versioning conventions;
- test/coverage/security gates;
- workflow permissions, secrets, artifacts, retention, and triggers.

For workflow YAML syntax and expression quoting, also use `authoring-github-workflows`.

## Rules

- Pin or deliberately version third-party actions; avoid floating behavior in security-sensitive jobs.
- Grant the minimum `GITHUB_TOKEN` permissions required by each workflow/job.
- Never place credentials in repository files or logs.
- Keep pull-request validation separate from publishing/release permissions.
- Do not publish packages/releases from untrusted fork code with write credentials.
- Avoid expensive jobs on every event unless their value justifies the cost.
- Keep CLI, JSON schema, NuGet package, and GitHub Action versioning implications explicit.
- Do not remove tests, analyzers, audit checks, or security gates merely to make CI green.

## Validation

For workflow changes, validate syntax with `actionlint` when available. For packaging changes, prefer a local pack/dry-run path before real publication. Publishing a release, NuGet package, container, or marketplace artifact always requires explicit user intent.

## Completion

Report changed triggers/permissions, build/test/package impact, validations performed, and any compatibility implications for existing consumers.
