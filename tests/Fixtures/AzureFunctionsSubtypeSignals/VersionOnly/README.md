# Azure Functions version-only non-match

This fixture deliberately sets `AzureFunctionsVersion` without an official Functions SDK/package model signal.

The property alone is not enough to infer Azure Functions.

Expected classification:

- `kind = console`;
- `subtype` absent;
- `confidence = medium`.
