---
name: test-anti-patterns
description: Use this skill to audit DotNetRepoInspector tests for weak assertions, flakiness, over-mocking, implementation coupling, order dependence, sleeps, magic data, and artificial coverage. Do not use it merely to write a new test from scratch.
license: MIT
---

# Test Anti-Patterns

## Critical anti-patterns

- **No meaningful assertion:** code executes but no behavior is verified.
- **Tautological assertion:** the test validates its own setup or repeats implementation logic.
- **Coverage touching:** code is called only to increase executed-line count.
- **Over-mocking:** the test mostly verifies mock configuration rather than repository-inspection behavior.
- **Implementation coupling:** harmless internal refactors break the test.
- **Timing/environment flakiness:** tests depend on sleeps, wall-clock time, fixed ports, network services, machine-specific SDK state, or execution order.
- **Magic fixture data:** important project/MSBuild properties have no clear connection to the behavior being tested.

## Project-specific guidance

Prefer synthetic fixture repositories for MSBuild/discovery behavior. Keep each fixture small enough that its purpose is obvious from the project files themselves.

Do not let fixture projects accidentally inherit DotNetRepoInspector's own root build configuration. Preserve the isolation boundary under `tests/Fixtures/`.

When a behavior depends on installed SDK resolution, separate hermetic tests of parsing/normalization from environment-dependent integration tests and label the latter clearly.

## Review process

1. Identify the behavior the test claims to protect.
2. Check whether assertions prove that behavior and relevant counterexamples.
3. Check for dependence on implementation details or local machine state.
4. Classify findings by regression risk.
5. Fix the smallest root cause rather than deleting assertions or adding retries/sleeps.

A strong test fails for a meaningful regression and remains stable under irrelevant refactoring.
