# Azure Functions mixed-model ambiguity

This fixture deliberately combines the legacy isolated-worker package set with the in-process build package.

The classifier must not choose one execution model when both model-specific signals are present.

Expected classification:

- `kind = unknown`;
- `subtype` absent;
- conflict signal `conflict:azure-functions-model`.
