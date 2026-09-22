# Explicit Worker SDK property signal

This fixture uses the common `Microsoft.NET.Sdk` but exposes `UsingMicrosoftNETSdkWorker=true`.

The .NET Worker SDK itself sets this evaluated property in its `Sdk.props`, making it an official build-system signal. A custom or composed SDK can therefore preserve Worker semantics even when `Microsoft.NET.Sdk.Worker` is not present in the project root's declared SDK list.

Issue #47 records this property as a strong Worker signal. Production classification intentionally remains unchanged until #152.
