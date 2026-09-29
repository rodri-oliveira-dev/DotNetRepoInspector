# Blazor Interactive Server ground truth

This fixture represents modern Blazor server-side interactivity.

`Program.cs` establishes the hosting ground truth with `AddInteractiveServerComponents` and `AddInteractiveServerRenderMode`. These are source-level configuration signals and are intentionally outside the current classifier boundary.

Expected classification:

- `kind = web`;
- `confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.Web`;
- `subtype` absent.
