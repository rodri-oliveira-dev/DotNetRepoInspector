# Microsoft.NET.Test.Sdk fallback signal

This fixture represents a project that directly references `Microsoft.NET.Test.Sdk` while `IsTestProject` is not available from evaluated package imports.

DotNetRepoInspector intentionally evaluates projects without performing restore. The direct `PackageReference` remains an observable structural fact even when generated NuGet imports are absent, so it is a useful fallback signal for issue #151.

The fallback must apply only when `IsTestProject` is missing. An explicit `IsTestProject=false` is treated separately as a conflict rather than being overridden by this package hint.
