# .NET 10 compatibility fixture

This synthetic repository pins the stable .NET 10 SDK family through `global.json` and contains a single SDK-style project targeting `net10.0`.

The cross-platform compatibility gate uses it as the current-runtime baseline and compares it with the older .NET 8 target scenario.
