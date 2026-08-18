---
name: authoring-github-workflows
description: Author and review GitHub Actions workflow YAML safely, especially expression quoting and structural validation. Use for files under .github/workflows and workflow-load failures where no jobs start. Do not use for unrelated application YAML.
license: MIT
---

# Authoring GitHub Actions Workflows Safely

Adapted from the .NET Foundation `dotnet/skills` repository. See `../THIRD-PARTY-NOTICES.md`.

Valid YAML is not automatically a valid GitHub Actions workflow. GitHub expressions embedded in YAML scalars can be parsed differently than expected, and a structurally invalid workflow may fail before any job starts.

## Key rule: quote risky expression scalars

A space followed by `#` begins a YAML comment in an unquoted scalar. For example, an expression containing text such as `PR #{0}` can be silently truncated even though a generic YAML parser accepts the file.

When `name`, `run-name`, `if`, `env`, or `with` values contain `${{ }}` expressions together with literal `#`, `: `, or YAML-leading special characters, quote the whole scalar.

```yaml
# Risky
run-name: ${{ format('Evaluate PR #{0}', inputs.pr_number) }}

# Safe
run-name: "${{ format('Evaluate PR #{0}', inputs.pr_number) }}"
```

Do not escape `${{ }}` itself; quote the YAML scalar.

## Workflow

1. Identify changed files under `.github/workflows/`.
2. Inspect expression-bearing scalars for YAML comment/type/special-character hazards.
3. Keep workflow permissions and secrets review separate from syntax review; use `ci-release-governance` for those concerns.
4. Validate with `actionlint`, not only a generic YAML parser.
5. Confirm a workflow expected to run actually produces jobs rather than failing at load time.

## actionlint

Use a pinned, checksum-verified `actionlint` binary or a trusted repository-local installation mechanism. A typical validation command is:

```bash
actionlint -shellcheck= -pyflakes= .github/workflows/*.yml
```

Generic `yamllint` or YAML deserialization does not validate the GitHub Actions expression grammar.

## Common pitfalls

- unquoted expression scalars containing `#`;
- `: ` being interpreted as a mapping boundary;
- YAML coercing string-looking values such as booleans/numbers;
- believing successful YAML parsing proves Actions will load the workflow;
- broadening workflow permissions while fixing an unrelated syntax error.

## Validation checklist

- expression scalars are quoted when required;
- `actionlint` exits successfully;
- required workflow permissions remain least-privileged;
- no secrets are embedded in YAML;
- intended jobs are able to start.
