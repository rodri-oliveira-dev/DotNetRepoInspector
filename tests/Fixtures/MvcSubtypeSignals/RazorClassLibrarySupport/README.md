# Razor Class Library MVC support overlap

This fixture demonstrates that `AddRazorSupportForMvc=true` also appears in a Razor Class Library using `Microsoft.NET.Sdk.Razor`.

The property enables Razor pages/views support but does not prove that the project is a Web MVC application. The project remains a library classification and no subtype is emitted.

Expected classification:

- `kind = library`;
- `subtype` absent.
