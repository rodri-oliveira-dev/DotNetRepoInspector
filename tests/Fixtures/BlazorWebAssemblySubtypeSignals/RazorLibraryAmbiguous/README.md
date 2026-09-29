# Razor component library ambiguity

This fixture contains Razor component source but declares `Microsoft.NET.Sdk.Razor`, not the Blazor WebAssembly SDK.

Razor component files, Razor SDK support, and ASP.NET Core framework references do not prove standalone browser-hosted WebAssembly execution.

Expected classification:

- `kind = library`;
- `confidence = high`;
- `subtype` absent.
