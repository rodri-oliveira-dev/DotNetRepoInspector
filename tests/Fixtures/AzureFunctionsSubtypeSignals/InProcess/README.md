# Azure Functions in-process

This fixture represents the .NET in-process Azure Functions model.

The classifier requires both the effective `AzureFunctionsVersion` property and the official `Microsoft.NET.Sdk.Functions` package, with library output semantics.

Expected classification:

- `kind = library`;
- `subtype = azure-functions-in-process`;
- `confidence = high`.
