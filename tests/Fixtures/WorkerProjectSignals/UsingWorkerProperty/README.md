# Explicit Worker opt-in property signal

This fixture uses the common `Microsoft.NET.Sdk` and manually sets `UsingMicrosoftNETSdkWorker=true`.

The .NET Worker SDK also sets this evaluated property in its `Sdk.props`, but an effective MSBuild scalar does not retain assignment provenance. The Inspector therefore cannot distinguish this manual assignment from the same value flowing through an SDK/import.

Issue #47 records the property as a strong **explicit opt-in** Worker signal, not as proof that `Microsoft.NET.Sdk.Worker` was imported. This fixture intentionally demonstrates that trade-off. Production classification remains unchanged until #152.
