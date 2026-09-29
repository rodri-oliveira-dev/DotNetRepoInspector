namespace DotNetRepoInspector.Core.Policies;

public sealed record PolicyFinding
{
    public PolicyFinding(
        string ruleCode,
        string severity,
        string message,
        PolicyFindingScope scope,
        IReadOnlyDictionary<string, string>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleCode);

        if (!PolicySeverity.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "Policy severity must be warning or error.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(scope);

        RuleCode = ruleCode;
        Severity = severity;
        Message = message;
        Scope = scope;
        Context = context;
    }

    public string RuleCode { get; }

    public string Severity { get; }

    public string Message { get; }

    public PolicyFindingScope Scope { get; }

    public IReadOnlyDictionary<string, string>? Context { get; }
}
