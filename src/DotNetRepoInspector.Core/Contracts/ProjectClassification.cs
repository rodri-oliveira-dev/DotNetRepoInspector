namespace DotNetRepoInspector.Core.Contracts;

public sealed record ProjectClassification(
    string Kind,
    string? Confidence,
    IReadOnlyList<string> Signals,
    string? Source = null,
    string? AutomaticKind = null);
