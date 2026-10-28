namespace DotNetRepoInspector.Core.Contracts;

public sealed record ProjectPublicationMetadata(
    PublicationBooleanPropertyValue SelfContained,
    PublicationBooleanPropertyValue PublishSelfContained,
    PublicationBooleanPropertyValue PublishAot,
    PublicationBooleanPropertyValue PublishTrimmed,
    PublicationStringPropertyValue RuntimeFrameworkVersion)
{
    public static ProjectPublicationMetadata Unspecified
    {
        get;
    } = new(
        PublicationBooleanPropertyValue.Unspecified,
        PublicationBooleanPropertyValue.Unspecified,
        PublicationBooleanPropertyValue.Unspecified,
        PublicationBooleanPropertyValue.Unspecified,
        PublicationStringPropertyValue.Unspecified);
}
