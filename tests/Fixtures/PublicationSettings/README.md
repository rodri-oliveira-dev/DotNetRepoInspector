# Publication settings fixture

This fixture verifies that publication-related properties come from evaluated MSBuild state.

- `Defined` uses explicit and inherited values and repeats runtime identifiers.
- `Absent` leaves publish-only properties unset.
- `Conditional` varies settings and runtime identifiers by target framework.

These values are evaluation evidence only. They do not represent the output of a
`dotnet publish` invocation or prove how an application is deployed.
