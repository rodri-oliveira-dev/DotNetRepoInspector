namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildPublicationFacts(
    MsBuildBooleanPropertyValue SelfContained,
    MsBuildBooleanPropertyValue PublishSelfContained,
    MsBuildBooleanPropertyValue PublishAot,
    MsBuildBooleanPropertyValue PublishTrimmed,
    MsBuildStringPropertyValue RuntimeFrameworkVersion)
{
    public static MsBuildPublicationFacts Unspecified
    {
        get;
    } = new(
        MsBuildBooleanPropertyValue.Unspecified,
        MsBuildBooleanPropertyValue.Unspecified,
        MsBuildBooleanPropertyValue.Unspecified,
        MsBuildBooleanPropertyValue.Unspecified,
        MsBuildStringPropertyValue.Unspecified);
}
