# RazorGenerate overlap

This fixture contains two Web SDK Razor inputs:

- `Pages/PageSample.cshtml` contains the Razor `@page` directive and is a Razor Page.
- `Views/ViewSample.cshtml` is a regular Razor View and does not contain `@page`.

The current `dotnet msbuild -getItem:RazorGenerate` evaluation boundary does not expose either file without additional target execution, while `AddRazorSupportForMvc=true` applies to the project as a whole. The structured facts available to classification therefore contain no page-vs-view distinction; reading the `@page` directive would require Razor source inspection.

Expected classification:

- `kind = web`;
- `confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.Web`;
- `subtype` absent.
