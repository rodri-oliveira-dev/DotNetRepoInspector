# Azure Functions isolated worker with Azure.Functions.Sdk

This fixture represents the current recommended .NET isolated-worker project shape.

The explicit `Azure.Functions.Sdk` project SDK is authoritative for the Azure Functions isolated-worker subtype. The classifier does not need project names, source inspection, or package-only inference.

Expected classification:

- `kind = worker`;
- `subtype = azure-functions-isolated`;
- `confidence = high`;
- signal `sdk:Azure.Functions.Sdk`.
