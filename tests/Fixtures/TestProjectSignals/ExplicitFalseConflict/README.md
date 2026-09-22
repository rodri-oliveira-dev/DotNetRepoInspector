# Explicit false test-project conflict

This fixture intentionally combines `IsTestProject=false` with a direct `Microsoft.NET.Test.Sdk` package reference.

It is the conservative ambiguity case for issue #96: package presence alone must not override an explicit negative VSTest signal. Issue #151 should therefore avoid promoting this project to `test` solely from the package reference.
