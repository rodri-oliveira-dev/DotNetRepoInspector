namespace DotNetRepoInspector.Core.Contracts;

public static class PublicationPropertyValueState
{
    public const string Evaluated = "evaluated";
    public const string Unspecified = "unspecified";
    public const string Unknown = "unknown";
    public const string ContextDependent = "context-dependent";

    public static bool IsDefined(string? value) =>
        value is Evaluated or Unspecified or Unknown or ContextDependent;
}

public sealed record PublicationBooleanPropertyValue(string State, bool? Value)
{
    public static PublicationBooleanPropertyValue Unspecified
    {
        get;
    } =
        new(PublicationPropertyValueState.Unspecified, null);
}

public sealed record PublicationStringPropertyValue(string State, string? Value)
{
    public static PublicationStringPropertyValue Unspecified
    {
        get;
    } =
        new(PublicationPropertyValueState.Unspecified, null);
}
