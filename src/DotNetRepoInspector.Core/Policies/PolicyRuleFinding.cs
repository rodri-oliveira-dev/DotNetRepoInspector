namespace DotNetRepoInspector.Core.Policies;

public sealed record PolicyRuleFinding
{
    public PolicyRuleFinding(
        string severity,
        string message,
        PolicyFindingScope scope,
        IReadOnlyDictionary<string, string>? context = null)
    {
        if (!PolicySeverity.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "Policy severity must be warning or error.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(scope);

        Severity = severity;
        Message = message;
        Scope = scope;
        Context = context;
    }

    public string Severity
    {
        get;
    }

    public string Message
    {
        get;
    }

    public PolicyFindingScope Scope
    {
        get;
    }

    public IReadOnlyDictionary<string, string>? Context
    {
        get;
    }
}
