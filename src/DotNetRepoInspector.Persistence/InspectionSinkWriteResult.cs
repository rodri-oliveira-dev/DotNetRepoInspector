namespace DotNetRepoInspector.Persistence;

public sealed class InspectionSinkWriteResult
{
    private InspectionSinkWriteResult(bool succeeded, InspectionSinkFailure? failure)
    {
        Succeeded = succeeded;
        Failure = failure;
    }

    public bool Succeeded
    {
        get;
    }

    public InspectionSinkFailure? Failure
    {
        get;
    }

    public static InspectionSinkWriteResult Success() => new(true, null);

    public static InspectionSinkWriteResult Failed(
        string code,
        string message,
        bool isTransient = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new(
            false,
            new InspectionSinkFailure(code, message, isTransient));
    }
}
