using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

internal static class DataIntegrationSyntax
{
    public static ResourceEvidence ConnectionStringEvidence(
        IntegrationDetectionContext context,
        SyntaxNode node)
    {
        InvocationExpressionSyntax? getConnectionString = node.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                CloudMessagingSyntax.TryGetInvocation(invocation, out string method, out _) &&
                method == "GetConnectionString");
        ExpressionSyntax? nameExpression = getConnectionString?.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (nameExpression is not null && context.Evidence.TryGetStringLiteral(nameExpression, out string name))
        {
            return new ResourceEvidence(name, $"ConnectionStrings:{name}");
        }

        return ResourceEvidence.Empty;
    }

    public static ResourceEvidence RequestEvidence(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        IReadOnlyCollection<string> propertyNames,
        int positionalIndex = 0)
    {
        ExpressionSyntax? request = CloudMessagingSyntax.Argument(
            invocation,
            ["request", .. propertyNames],
            positionalIndex);
        return CloudMessagingSyntax.Evidence(context, request, propertyNames);
    }

    public static void Add(
        IntegrationDetectionContext context,
        SyntaxNode source,
        string kind,
        string direction,
        string technology,
        ResourceEvidence resource,
        string? resourceType,
        IReadOnlyList<string> signals,
        string confidence = IntegrationConfidence.High)
    {
        string? target = resource.Target is null
            ? null
            : CloudMessagingSyntax.LogicalResource(resource.Target);
        context.Findings.TryAdd(new IntegrationFindingCandidate(
            kind,
            direction,
            technology,
            context.GetOneBasedLine(source),
            resource.IsPresent ? confidence : IntegrationConfidence.Medium,
            signals,
            Target: string.IsNullOrWhiteSpace(target) ? null : target,
            ResourceType: resourceType,
            ConfigurationKey: resource.ConfigurationKey));
    }

    public static void MapInvocationResult(
        InvocationExpressionSyntax invocation,
        VariableTypeMap types,
        Dictionary<string, ResourceEvidence> resources,
        string type,
        ResourceEvidence resource)
    {
        SyntaxNode? parent = invocation.Parent is AwaitExpressionSyntax awaited ? awaited.Parent : invocation.Parent;
        if (parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable })
        {
            return;
        }

        types.Add(variable, type);
        resources[variable.Identifier.ValueText] = resource;
    }

    public static ResourceEvidence MappedResource(
        ExpressionSyntax? receiver,
        IReadOnlyDictionary<string, ResourceEvidence> resources)
    {
        string? name = (receiver as IdentifierNameSyntax)?.Identifier.ValueText;
        return name is not null && resources.TryGetValue(name, out ResourceEvidence? resource)
            ? resource
            : ResourceEvidence.Empty;
    }
}
