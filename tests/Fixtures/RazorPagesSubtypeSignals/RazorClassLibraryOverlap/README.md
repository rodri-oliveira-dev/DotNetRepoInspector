# Razor Class Library overlap

This fixture demonstrates that a Razor Class Library can expose `RazorGenerate` items and `AddRazorSupportForMvc=true` without being a Web Razor Pages application.

The `Pages/LibraryPage.cshtml` file contains `@page`, but the project itself is a library. The current classifier intentionally does not inspect Razor source content to promote that page-level fact into a project subtype.

Expected classification:

- `kind = library`;
- `confidence = high`;
- signal `property:OutputType=Library`;
- `subtype` absent.
