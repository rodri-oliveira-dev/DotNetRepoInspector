namespace DotNetRepoInspector.Cli;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int CompletedWithErrors = 1;
    public const int InvalidArguments = 2;
    public const int InspectionFailed = 3;
    public const int OutputFailed = 4;
    public const int Cancelled = 130;
}
