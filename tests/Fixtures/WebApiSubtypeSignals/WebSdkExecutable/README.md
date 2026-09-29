# Web SDK executable ambiguity

This fixture captures the strongest project-file shape commonly associated with an ASP.NET Core Web API without using source-code analysis: `Microsoft.NET.Sdk.Web` plus executable output.

The same evaluated shape is valid for MVC, Razor Pages, Blazor/server-side Web hosts, and mixed applications. It therefore proves only the base `web` classification.

Expected classification:
- `kind = web`;
- `confidence = high`;
- signal `sdk:Microsoft.NET.Sdk.Web`;
- `subtype` absent.

No Web API subtype should be inferred from this fixture.
