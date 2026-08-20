## Summary

Describe the problem and the focused change made by this pull request.

## Related issue

Closes #

## Validation

List the commands/tests you ran. If something could not be validated locally, explain why.

```text
# Example:
dotnet format ... --verify-no-changes
dotnet build ... --warnaserror
dotnet test ...
```

## Checklist

Check the items that apply. Leave non-applicable items unchecked rather than adding artificial work.

- [ ] The change is focused and does not include unrelated refactoring or generated build outputs.
- [ ] Formatting, build/analyzers, and relevant tests pass locally or the limitation is explained above.
- [ ] Inspection/classification behavior changes include a minimal synthetic fixture and regression test.
- [ ] Public JSON/schema, diagnostics, CLI, Action, package, or persistence contract changes include compatibility analysis and contract tests when applicable.
- [ ] Public documentation changes are synchronized between English and Portuguese (Brazil) when applicable.
- [ ] New or changed agent skills are focused, compatible with `AGENTS.md`, and update third-party notices when required.
- [ ] No credentials, secrets, private repository contents, customer data, or sensitive exploit details are included.
- [ ] Security-sensitive changes follow `SECURITY.md` and preserve least-privilege behavior.
