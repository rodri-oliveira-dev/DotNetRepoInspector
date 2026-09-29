namespace DotNetRepoInspector.Core.Policies;

public interface IPolicyRule
{
    string Code { get; }

    IReadOnlyList<PolicyRuleFinding> Evaluate(PolicyEvaluationContext context);
}
