namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildStringPropertyValue(
    MsBuildPropertyValueState State,
    string? Value)
{
    public static MsBuildStringPropertyValue Evaluated(string value) =>
        new(MsBuildPropertyValueState.Evaluated, value);

    public static MsBuildStringPropertyValue Unspecified
    {
        get;
    } =
        new(MsBuildPropertyValueState.Unspecified, null);

    public static MsBuildStringPropertyValue Unknown
    {
        get;
    } =
        new(MsBuildPropertyValueState.Unknown, null);

    public static MsBuildStringPropertyValue ContextDependent
    {
        get;
    } =
        new(MsBuildPropertyValueState.ContextDependent, null);
}
