# Web SDK implicit Razor support

This fixture records that a modern `Microsoft.NET.Sdk.Web` project evaluates `AddRazorSupportForMvc=true` even when the project file does not explicitly opt into MVC.

That property configures Razor support used by MVC views or Razor Pages and therefore does not identify the MVC application model.

Expected classification:

- `kind = web`;
- `confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.Web`;
- `subtype` absent.
