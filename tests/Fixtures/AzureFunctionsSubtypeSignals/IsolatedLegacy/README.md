# Azure Functions isolated worker legacy build SDK shape

This fixture represents the pre-`Azure.Functions.Sdk` isolated-worker shape that remains relevant during migration.

The classifier requires the complete structural combination:

- effective `AzureFunctionsVersion`;
- effective `OutputType = Exe`;
- `Microsoft.Azure.Functions.Worker`;
- `Microsoft.Azure.Functions.Worker.Sdk`.

Expected classification:

- `kind = worker`;
- `subtype = azure-functions-isolated`;
- `confidence = high`.
