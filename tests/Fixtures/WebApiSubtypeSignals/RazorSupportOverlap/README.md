# Razor support overlap

This fixture is a counterexample for negative Web API inference. It explicitly enables Razor support while retaining the same Web SDK/executable base shape.

Razor support does not prove that API endpoints are absent because ASP.NET Core application models can coexist in the same project. Conversely, the absence of this property would not prove that a project is a Web API.

Expected classification:
- `kind = web`;
- `confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.Web`;
- `subtype` absent.
