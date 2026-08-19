# .NET 8 compatibility fixture

This synthetic repository pins a stable .NET 8 SDK family through `global.json` and contains a single SDK-style project targeting `net8.0`.

The cross-platform compatibility gate uses it to prove that the Inspector can run on `net10.0` while the inspected repository resolves and evaluates with an installed .NET 8 SDK.
