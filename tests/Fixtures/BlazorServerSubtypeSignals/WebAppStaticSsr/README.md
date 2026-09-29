# Blazor Web App static SSR ground truth

This fixture represents a modern server-hosted Blazor Web App using Razor Components without an interactive render mode.

`Program.cs` establishes the application-model ground truth with `AddRazorComponents` and `MapRazorComponents`. The classifier intentionally does not inspect those source calls.

Expected classification:

- `kind = web`;
- `confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.Web`;
- `subtype` absent.
