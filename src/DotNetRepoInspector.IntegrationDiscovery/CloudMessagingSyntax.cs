using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

internal static class CloudMessagingSyntax
{
    public static Dictionary<string, string> FindVariableTypes(CompilationUnitSyntax root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (VariableDeclarationSyntax declaration in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            string declaredType = SimpleName(declaration.Type);
            foreach (VariableDeclaratorSyntax variable in declaration.Variables)
            {
                string type = declaredType == "var"
                    ? InferCreatedType(variable.Initializer?.Value)
                    : declaredType;
                if (!string.IsNullOrEmpty(type))
                {
                    result[variable.Identifier.ValueText] = type;
                }
            }
        }

        foreach (ParameterSyntax parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is not null)
            {
                result[parameter.Identifier.ValueText] = SimpleName(parameter.Type);
            }
        }

        return result;
    }

    public static string? ReceiverType(
        ExpressionSyntax? receiver,
        IReadOnlyDictionary<string, string> variableTypes)
    {
        string? name = receiver switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            ObjectCreationExpressionSyntax creation => SimpleName(creation.Type),
            _ => null
        };
        return name is not null && variableTypes.TryGetValue(name, out string? type)
            ? type
            : receiver is ObjectCreationExpressionSyntax ? name : null;
    }

    public static bool TryGetInvocation(
        InvocationExpressionSyntax invocation,
        out string method,
        out ExpressionSyntax? receiver)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax member)
        {
            method = member.Name.Identifier.ValueText;
            receiver = member.Expression;
            return true;
        }

        method = string.Empty;
        receiver = null;
        return false;
    }

    public static ExpressionSyntax? Argument(
        InvocationExpressionSyntax invocation,
        IReadOnlyCollection<string> names,
        int positionalIndex)
    {
        ArgumentSyntax? named = invocation.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameColon is not null && names.Contains(argument.NameColon.Name.Identifier.ValueText));
        if (named is not null)
        {
            return named.Expression;
        }

        return positionalIndex >= 0 && positionalIndex < invocation.ArgumentList.Arguments.Count
            ? invocation.ArgumentList.Arguments[positionalIndex].Expression
            : null;
    }

    public static ResourceEvidence Evidence(
        IntegrationDetectionContext context,
        ExpressionSyntax? expression,
        IReadOnlyCollection<string> propertyNames)
    {
        if (expression is null)
        {
            return ResourceEvidence.Empty;
        }

        ExpressionSyntax resolved = ResolveIdentifier(context.Root, expression) ?? expression;
        foreach (AssignmentExpressionSyntax assignment in resolved.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            string? property = assignment.Left switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                _ => null
            };
            if (property is not null && propertyNames.Contains(property))
            {
                ResourceEvidence evidence = DirectEvidence(context, assignment.Right);
                if (evidence.IsPresent)
                {
                    return evidence;
                }
            }
        }

        return DirectEvidence(context, resolved);
    }

    public static ResourceEvidence ConstructionEvidence(
        IntegrationDetectionContext context,
        VariableDeclaratorSyntax variable,
        IReadOnlyCollection<string> argumentNames,
        int positionalIndex)
    {
        ExpressionSyntax? value = Unwrap(variable.Initializer?.Value);
        if (value is ObjectCreationExpressionSyntax creation)
        {
            ExpressionSyntax? expression = creation.ArgumentList is null
                ? null
                : NamedOrPositional(creation.ArgumentList.Arguments, argumentNames, positionalIndex);
            return Evidence(context, expression, argumentNames);
        }

        if (value is InvocationExpressionSyntax invocation)
        {
            return Evidence(context, Argument(invocation, argumentNames, positionalIndex), argumentNames);
        }

        return ResourceEvidence.Empty;
    }

    public static string LogicalResource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim().TrimEnd('/');
        if (trimmed.StartsWith("arn:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Split(':').Last();
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            string pathPart = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? string.Empty;
            return pathPart is "events" or "messages" or "publish" ? uri.Host.Split('.')[0] : pathPart;
        }

        int separator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf(':'));
        return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
    }

    public static void Add(
        IntegrationDetectionContext context,
        SyntaxNode source,
        string direction,
        string technology,
        ResourceEvidence resource,
        string resourceType,
        IReadOnlyList<string> signals)
    {
        string? target = resource.Target is null ? null : LogicalResource(resource.Target);
        context.Findings.TryAdd(new IntegrationFindingCandidate(
            Core.Contracts.IntegrationKind.Messaging,
            direction,
            technology,
            context.GetOneBasedLine(source),
            target is not null || resource.ConfigurationKey is not null
                ? Core.Contracts.IntegrationConfidence.High
                : Core.Contracts.IntegrationConfidence.Medium,
            signals,
            Target: string.IsNullOrEmpty(target) ? null : target,
            ResourceType: resourceType,
            ConfigurationKey: resource.ConfigurationKey));
    }

    public static ResourceEvidence DirectEvidence(
        IntegrationDetectionContext context,
        ExpressionSyntax expression)
    {
        foreach (ElementAccessExpressionSyntax access in expression.DescendantNodesAndSelf().OfType<ElementAccessExpressionSyntax>())
        {
            ExpressionSyntax? key = access.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (key is not null && context.Evidence.TryGetConfigurationKey(key, out string configurationKey))
            {
                return new ResourceEvidence(null, configurationKey);
            }
        }

        // For SDK resource factories, the logical resource is conventionally the final argument.
        InvocationExpressionSyntax? factory = expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().LastOrDefault();
        if (factory?.ArgumentList.Arguments.LastOrDefault()?.Expression is ExpressionSyntax factoryResource &&
            context.Evidence.TryGetStringLiteral(factoryResource, out string factoryValue))
        {
            return new ResourceEvidence(factoryValue, null);
        }

        foreach (LiteralExpressionSyntax literal in expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
        {
            if (context.Evidence.TryGetStringLiteral(literal, out string value))
            {
                return new ResourceEvidence(value, null);
            }
        }

        return ResourceEvidence.Empty;
    }

    public static string SimpleName(TypeSyntax type) => type switch
    {
        GenericNameSyntax generic => generic.Identifier.ValueText,
        QualifiedNameSyntax qualified => SimpleName(qualified.Right),
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        NullableTypeSyntax nullable => SimpleName(nullable.ElementType),
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => type.ToString().Split('.').Last()
    };

    private static ExpressionSyntax? ResolveIdentifier(CompilationUnitSyntax root, ExpressionSyntax expression)
    {
        if (Unwrap(expression) is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        return root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText)
            ?.Initializer?.Value;
    }

    private static string InferCreatedType(ExpressionSyntax? expression)
    {
        expression = Unwrap(expression);
        return expression switch
        {
            ObjectCreationExpressionSyntax creation => SimpleName(creation.Type),
            _ => string.Empty
        };
    }

    private static ExpressionSyntax? NamedOrPositional(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyCollection<string> names,
        int positionalIndex)
    {
        ArgumentSyntax? named = arguments.FirstOrDefault(argument =>
            argument.NameColon is not null && names.Contains(argument.NameColon.Name.Identifier.ValueText));
        return named?.Expression ??
               (positionalIndex >= 0 && positionalIndex < arguments.Count
                   ? arguments[positionalIndex].Expression
                   : null);
    }

    private static ExpressionSyntax? Unwrap(ExpressionSyntax? expression) => expression switch
    {
        AwaitExpressionSyntax awaited => Unwrap(awaited.Expression),
        ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
        CastExpressionSyntax cast => Unwrap(cast.Expression),
        _ => expression
    };
}

internal sealed record ResourceEvidence(string? Target, string? ConfigurationKey)
{
    public bool IsPresent => Target is not null || ConfigurationKey is not null;

    public static ResourceEvidence Empty { get; } = new(null, null);
}
