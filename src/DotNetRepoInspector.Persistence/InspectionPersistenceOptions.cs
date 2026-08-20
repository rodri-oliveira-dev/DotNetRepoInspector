namespace DotNetRepoInspector.Persistence;

public sealed record InspectionPersistenceOptions
{
    public static InspectionPersistenceOptions Default => new();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public PersistenceFailureMode FailureMode { get; init; } = PersistenceFailureMode.NonFatal;

    internal void Validate()
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Timeout),
                Timeout,
                "Persistence timeout must be greater than zero.");
        }

        if (!Enum.IsDefined(FailureMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(FailureMode),
                FailureMode,
                "Persistence failure mode is not supported.");
        }
    }
}
