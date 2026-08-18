---
name: coverage-analysis
description: Use this skill to analyze DotNetRepoInspector test coverage, identify meaningful gaps and risk hotspots, and prioritize behavior-focused tests. Do not use it to inflate coverage percentages or weaken thresholds.
license: MIT
---

# Coverage Analysis

Coverage is evidence of execution, not proof of correctness. Prioritize confidence in inspection behavior over percentage alone.

## Use for

- identifying untested classification branches;
- finding MSBuild-evaluation edge cases with low coverage;
- assessing refactoring risk;
- deciding which fixtures/tests should be added first;
- reviewing whether coverage is superficial.

## Risk priorities

High-risk gaps include:

- classification precedence;
- SDK/framework distinction;
- conditional/inherited MSBuild properties;
- multi-targeting;
- error/partial-result behavior;
- output schema/serialization;
- path normalization and project discovery;
- privacy allow-list logic.

Lower-risk gaps can include trivial DTO accessors, generated code, and declarative configuration with no behavior.

## Rules

- Do not alter tests just to execute lines.
- Require meaningful assertions.
- Do not lower a coverage gate merely to make CI pass without explicit approval.
- Do not install global tooling when repository-local tooling already provides the needed report.
- Combine coverage data with complexity and behavioral criticality.

## Process

1. Identify the changed behavior and closest tests.
2. Use the existing repository coverage mechanism when available.
3. Map uncovered branches to user-visible or machine-contract risks.
4. Prioritize the smallest test that proves the missing behavior.
5. For inspection rules, prefer a synthetic fixture plus an assertion on normalized output.
6. Report intentionally uncovered low-risk code separately from dangerous gaps.

## Quality criterion

A useful coverage improvement reduces the probability of a false classification, broken public output, or unsafe repository inspection. A percentage increase without stronger behavioral assertions is not sufficient.
