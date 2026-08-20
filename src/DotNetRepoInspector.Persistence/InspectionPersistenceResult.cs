namespace DotNetRepoInspector.Persistence;

public sealed record InspectionPersistenceResult(
    string SinkName,
    bool Succeeded,
    InspectionSinkFailure? Failure,
    PersistenceFailureMode FailureMode)
{
    public bool ShouldFailExecution =>
        !Succeeded && FailureMode == PersistenceFailureMode.Fatal;
}
