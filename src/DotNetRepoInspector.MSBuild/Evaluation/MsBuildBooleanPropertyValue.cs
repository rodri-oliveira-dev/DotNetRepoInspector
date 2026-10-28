namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildBooleanPropertyValue(
    MsBuildPropertyValueState State,
    bool? Value)
{
    public static MsBuildBooleanPropertyValue Evaluated(bool value) =>
        new(MsBuildPropertyValueState.Evaluated, value);

    public static MsBuildBooleanPropertyValue Unspecified
    {
        get;
    } =
        new(MsBuildPropertyValueState.Unspecified, null);

    public static MsBuildBooleanPropertyValue Unknown
    {
        get;
    } =
        new(MsBuildPropertyValueState.Unknown, null);

    public static MsBuildBooleanPropertyValue ContextDependent
    {
        get;
    } =
        new(MsBuildPropertyValueState.ContextDependent, null);
}
