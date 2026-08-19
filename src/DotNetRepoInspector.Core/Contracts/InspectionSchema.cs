namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionSchema
{
    public const string CurrentVersion = "1.0";
    public const int CurrentMajorVersion = 1;

    public static bool IsCompatibleVersion(string? schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion) ||
            !Version.TryParse(schemaVersion, out var parsedVersion))
        {
            return false;
        }

        return parsedVersion.Major == CurrentMajorVersion && parsedVersion.Minor >= 0;
    }
}
