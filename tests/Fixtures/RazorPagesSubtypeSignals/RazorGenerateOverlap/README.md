# RazorGenerate overlap

This fixture contains two Web SDK Razor inputs:

- `Pages/PageSample.cshtml` contains the Razor `@page` directive and is a Razor Page.
- `Views/ViewSample.cshtml` is a regular Razor View and does not contain `@page`.

MSBuild exposes both files through the same `RazorGenerate` item type, while `AddRazorSupportForMvc=true` applies to the project as a whole. Neither evaluated signal distinguishes Razor Pages from MVC Views without reading Razor source content.

Expected classification:

- `kind = web`;
- `confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.Web`;
- `subtype` absent.
