using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class HttpIntegrationDetector : IIntegrationDetector
{
    private static readonly HashSet<string> HttpMethodNames =
        new(StringComparer.Ordinal)
        {
            "DeleteAsync",
            "GetAsync",
            "PatchAsync",
            "PostAsync",
            "PutAsync",
            "SendAsync"
        };

    public string Id => "http-outbound";

    public ValueTask DetectAsync(
        IntegrationDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        HashSet<string> httpClientReceivers = FindVariablesOfType(context.Root, "HttpClient");
        HashSet<string> factoryReceivers = FindVariablesOfType(context.Root, "IHttpClientFactory");
        AddInferredHttpClientVariables(context.Root, httpClientReceivers, factoryReceivers);

        foreach (InvocationExpressionSyntax invocation in context.Root
                     .DescendantNodes()
                     .OfType<InvocationExpressionSyntax>()
                     .OrderBy(static invocation => invocation.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetInvocation(invocation, out string methodName, out ExpressionSyntax? receiver))
            {
                continue;
            }

            if (methodName == "AddHttpClient")
            {
                AddRegistrationFinding(
                    context,
                    invocation,
                    IntegrationTechnology.HttpClient,
                    "http:add-http-client");
                continue;
            }

            if (methodName == "AddRefitClient")
            {
                AddRegistrationFinding(
                    context,
                    invocation,
                    IntegrationTechnology.Refit,
                    "refit:add-refit-client");
                continue;
            }

            if (methodName == "For" && IsRestServiceReceiver(receiver))
            {
                AddRestServiceFinding(context, invocation);
                continue;
            }

            if (methodName == "CreateClient" &&
                IsRecognizedReceiver(receiver, factoryReceivers))
            {
                AddFactoryFinding(context, invocation);
                continue;
            }

            if (HttpMethodNames.Contains(methodName) &&
                IsHttpClientReceiver(receiver, httpClientReceivers, factoryReceivers))
            {
                AddHttpMethodFinding(context, invocation, receiver, methodName);
            }
        }

        foreach (AssignmentExpressionSyntax assignment in context.Root
                     .DescendantNodes()
                     .OfType<AssignmentExpressionSyntax>()
                     .OrderBy(static assignment => assignment.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddStandaloneBaseAddressFinding(context, assignment, httpClientReceivers);
        }

        return ValueTask.CompletedTask;
    }

    private static void AddRegistrationFinding(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string technology,
        string primarySignal)
    {
        string? contract = GetFirstGenericTypeName(invocation);
        string? namedClient = TryGetFirstSafeLiteral(context, invocation.ArgumentList.Arguments);
        SyntaxNode correlationScope =
            invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>() ?? (SyntaxNode)invocation;
        EndpointEvidence endpoint = FindBaseAddressEvidence(context, correlationScope);
        string? target = FirstNonEmpty(
            namedClient,
            TargetFromConfigurationKey(endpoint.ConfigurationKey),
            TargetFromContract(contract),
            endpoint.Host);
        string confidence = Confidence(target, namedClient, endpoint.ConfigurationKey, endpoint.Host, contract);
        var signals = new List<string> { primarySignal };

        if (namedClient is not null)
        {
            signals.Add("http:named-client");
        }

        if (contract is not null)
        {
            signals.Add("http:typed-client");
        }

        AddEndpointSignals(signals, endpoint);
        if (endpoint != EndpointEvidence.Empty)
        {
            signals.Add("http:base-address");
        }

        context.Findings.TryAdd(new IntegrationFindingCandidate(
            IntegrationKind.Http,
            IntegrationDirection.Outbound,
            technology,
            context.GetOneBasedLine(invocation),
            confidence,
            signals,
            Target: target,
            ConfigurationKey: endpoint.ConfigurationKey,
            Contract: contract));
    }

    private static void AddRestServiceFinding(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation)
    {
        string? contract = GetFirstGenericTypeName(invocation);
        EndpointEvidence endpoint = FindEndpointEvidence(context, invocation.ArgumentList.Arguments);
        string? target = FirstNonEmpty(
            TargetFromConfigurationKey(endpoint.ConfigurationKey),
            TargetFromContract(contract),
            endpoint.Host);
        var signals = new List<string> { "refit:rest-service-for" };
        AddEndpointSignals(signals, endpoint);

        context.Findings.TryAdd(new IntegrationFindingCandidate(
            IntegrationKind.Http,
            IntegrationDirection.Outbound,
            IntegrationTechnology.Refit,
            context.GetOneBasedLine(invocation),
            Confidence(target, null, endpoint.ConfigurationKey, endpoint.Host, contract),
            signals,
            Target: target,
            ConfigurationKey: endpoint.ConfigurationKey,
            Contract: contract));
    }

    private static void AddFactoryFinding(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation)
    {
        string? namedClient = TryGetFirstSafeLiteral(context, invocation.ArgumentList.Arguments);
        context.Findings.TryAdd(new IntegrationFindingCandidate(
            IntegrationKind.Http,
            IntegrationDirection.Outbound,
            IntegrationTechnology.HttpClient,
            context.GetOneBasedLine(invocation),
            namedClient is null ? IntegrationConfidence.Low : IntegrationConfidence.High,
            namedClient is null
                ? ["http:create-client"]
                : ["http:create-client", "http:named-client"],
            Target: namedClient));
    }

    private static void AddHttpMethodFinding(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        ExpressionSyntax? receiver,
        string methodName)
    {
        EndpointEvidence endpoint = FindHttpMethodEndpoint(context, invocation, methodName);
        string? namedClient = TryGetNamedClientFromReceiver(context, receiver);
        string? target = FirstNonEmpty(
            namedClient,
            TargetFromConfigurationKey(endpoint.ConfigurationKey),
            endpoint.Host);
        var signals = new List<string> { "http:method-call" };

        if (namedClient is not null)
        {
            signals.Add("http:named-client");
        }

        AddEndpointSignals(signals, endpoint);
        context.Findings.TryAdd(new IntegrationFindingCandidate(
            IntegrationKind.Http,
            IntegrationDirection.Outbound,
            IntegrationTechnology.HttpClient,
            context.GetOneBasedLine(invocation),
            target is null ? IntegrationConfidence.Low : IntegrationConfidence.High,
            signals,
            Target: target,
            ConfigurationKey: endpoint.ConfigurationKey));
    }

    private static void AddStandaloneBaseAddressFinding(
        IntegrationDetectionContext context,
        AssignmentExpressionSyntax assignment,
        HashSet<string> httpClientReceivers)
    {
        MemberAccessExpressionSyntax? memberAccess = assignment.Left as MemberAccessExpressionSyntax;
        bool isBaseAddress = memberAccess?.Name.Identifier.ValueText == "BaseAddress" ||
                             assignment.Left is IdentifierNameSyntax identifier &&
                             identifier.Identifier.ValueText == "BaseAddress";
        if (!isBaseAddress || IsInsideClientRegistration(assignment))
        {
            return;
        }

        bool recognizedReceiver = memberAccess is not null &&
                                  IsRecognizedReceiver(memberAccess.Expression, httpClientReceivers) ||
                                  assignment.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>() is
                                  { Type: { } objectType } &&
                                  GetSimpleTypeName(objectType) == "HttpClient";
        if (!recognizedReceiver)
        {
            return;
        }

        EndpointEvidence endpoint = FindEndpointEvidence(context, assignment.Right);
        string? target = FirstNonEmpty(
            TargetFromConfigurationKey(endpoint.ConfigurationKey),
            endpoint.Host);
        var signals = new List<string> { "http:base-address" };
        AddEndpointSignals(signals, endpoint);
        context.Findings.TryAdd(new IntegrationFindingCandidate(
            IntegrationKind.Http,
            IntegrationDirection.Outbound,
            IntegrationTechnology.HttpClient,
            context.GetOneBasedLine(assignment),
            target is null ? IntegrationConfidence.Low : IntegrationConfidence.High,
            signals,
            Target: target,
            ConfigurationKey: endpoint.ConfigurationKey));
    }

    private static HashSet<string> FindVariablesOfType(
        SyntaxNode root,
        string expectedType)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (VariableDeclarationSyntax declaration in root
                     .DescendantNodes()
                     .OfType<VariableDeclarationSyntax>())
        {
            if (GetSimpleTypeName(declaration.Type) != expectedType)
            {
                continue;
            }

            foreach (VariableDeclaratorSyntax variable in declaration.Variables)
            {
                result.Add(variable.Identifier.ValueText);
            }
        }

        foreach (ParameterSyntax parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is not null && GetSimpleTypeName(parameter.Type) == expectedType)
            {
                result.Add(parameter.Identifier.ValueText);
            }
        }

        return result;
    }

    private static void AddInferredHttpClientVariables(
        SyntaxNode root,
        HashSet<string> httpClients,
        HashSet<string> factories)
    {
        foreach (VariableDeclaratorSyntax variable in root
                     .DescendantNodes()
                     .OfType<VariableDeclaratorSyntax>())
        {
            ExpressionSyntax? value = variable.Initializer?.Value;
            if (value is ObjectCreationExpressionSyntax creation &&
                GetSimpleTypeName(creation.Type) == "HttpClient")
            {
                httpClients.Add(variable.Identifier.ValueText);
                continue;
            }

            if (value is InvocationExpressionSyntax invocation &&
                TryGetInvocation(invocation, out string methodName, out ExpressionSyntax? receiver) &&
                methodName == "CreateClient" &&
                IsRecognizedReceiver(receiver, factories))
            {
                httpClients.Add(variable.Identifier.ValueText);
            }
        }
    }

    private static EndpointEvidence FindBaseAddressEvidence(
        IntegrationDetectionContext context,
        SyntaxNode scope)
    {
        foreach (AssignmentExpressionSyntax assignment in scope
                     .DescendantNodesAndSelf()
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "BaseAddress")
            {
                return FindEndpointEvidence(context, assignment.Right);
            }
        }

        return EndpointEvidence.Empty;
    }

    private static EndpointEvidence FindHttpMethodEndpoint(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string methodName)
    {
        if (methodName == "SendAsync" &&
            invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is
            ObjectCreationExpressionSyntax requestCreation &&
            GetSimpleTypeName(requestCreation.Type) == "HttpRequestMessage" &&
            requestCreation.ArgumentList is { Arguments.Count: >= 2 })
        {
            return FindEndpointEvidence(context, requestCreation.ArgumentList.Arguments[1].Expression);
        }

        return FindEndpointEvidence(context, invocation.ArgumentList.Arguments);
    }

    private static EndpointEvidence FindEndpointEvidence(
        IntegrationDetectionContext context,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (ArgumentSyntax argument in arguments)
        {
            EndpointEvidence evidence = FindEndpointEvidence(context, argument.Expression);
            if (evidence != EndpointEvidence.Empty)
            {
                return evidence;
            }
        }

        return EndpointEvidence.Empty;
    }

    private static EndpointEvidence FindEndpointEvidence(
        IntegrationDetectionContext context,
        SyntaxNode scope)
    {
        foreach (ElementAccessExpressionSyntax elementAccess in scope
                     .DescendantNodesAndSelf()
                     .OfType<ElementAccessExpressionSyntax>())
        {
            if (!IsConfigurationReceiver(elementAccess.Expression))
            {
                continue;
            }

            ExpressionSyntax? argument = elementAccess.ArgumentList.Arguments
                .FirstOrDefault()?.Expression;
            if (argument is not null &&
                context.Evidence.TryGetConfigurationKey(argument, out string key))
            {
                return new EndpointEvidence(key, null);
            }
        }

        foreach (InvocationExpressionSyntax invocation in scope
                     .DescendantNodesAndSelf()
                     .OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetInvocation(
                    invocation,
                    out string methodName,
                    out ExpressionSyntax? configurationReceiver) ||
                methodName is not ("GetSection" or "GetValue" or "GetRequiredSection"))
            {
                continue;
            }

            if (!IsConfigurationReceiver(configurationReceiver))
            {
                continue;
            }

            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                if (context.Evidence.TryGetConfigurationKey(argument.Expression, out string key))
                {
                    return new EndpointEvidence(key, null);
                }
            }
        }

        foreach (LiteralExpressionSyntax literal in scope
                     .DescendantNodesAndSelf()
                     .OfType<LiteralExpressionSyntax>())
        {
            if (TryGetHttpHost(literal.Token.ValueText, out string host))
            {
                return new EndpointEvidence(null, host);
            }
        }

        return EndpointEvidence.Empty;
    }

    private static string? TryGetFirstSafeLiteral(
        IntegrationDetectionContext context,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (ArgumentSyntax argument in arguments)
        {
            if (context.Evidence.TryGetStringLiteral(argument.Expression, out string value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? TryGetNamedClientFromReceiver(
        IntegrationDetectionContext context,
        ExpressionSyntax? receiver)
    {
        if (receiver is not InvocationExpressionSyntax invocation ||
            !TryGetInvocation(invocation, out string methodName, out _) ||
            methodName != "CreateClient")
        {
            return null;
        }

        return TryGetFirstSafeLiteral(context, invocation.ArgumentList.Arguments);
    }

    private static bool IsHttpClientReceiver(
        ExpressionSyntax? receiver,
        HashSet<string> httpClients,
        HashSet<string> factories) =>
        IsRecognizedReceiver(receiver, httpClients) ||
        receiver is ObjectCreationExpressionSyntax creation &&
        GetSimpleTypeName(creation.Type) == "HttpClient" ||
        receiver is InvocationExpressionSyntax invocation &&
        TryGetInvocation(invocation, out string methodName, out ExpressionSyntax? factoryReceiver) &&
        methodName == "CreateClient" &&
        IsRecognizedReceiver(factoryReceiver, factories);

    private static bool IsRecognizedReceiver(
        ExpressionSyntax? receiver,
        HashSet<string> receiverNames) =>
        receiver switch
        {
            IdentifierNameSyntax identifier => receiverNames.Contains(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax member => receiverNames.Contains(member.Name.Identifier.ValueText),
            _ => false
        };

    private static bool IsRestServiceReceiver(ExpressionSyntax? receiver) =>
        receiver switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "RestService",
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == "RestService",
            _ => false
        };

    private static bool IsConfigurationReceiver(ExpressionSyntax? receiver)
    {
        string? name = receiver switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null
        };

        return name is not null &&
               (name.Equals("config", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("configuration", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInsideClientRegistration(SyntaxNode node)
    {
        ExpressionStatementSyntax? statement = node.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        return statement?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(static invocation =>
                TryGetInvocation(invocation, out string methodName, out _) &&
                methodName is "AddHttpClient" or "AddRefitClient") == true;
    }

    private static bool TryGetInvocation(
        InvocationExpressionSyntax invocation,
        out string methodName,
        out ExpressionSyntax? receiver)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax member:
                methodName = member.Name.Identifier.ValueText;
                receiver = member.Expression;
                return true;
            case IdentifierNameSyntax identifier:
                methodName = identifier.Identifier.ValueText;
                receiver = null;
                return true;
            case GenericNameSyntax generic:
                methodName = generic.Identifier.ValueText;
                receiver = null;
                return true;
            default:
                methodName = string.Empty;
                receiver = null;
                return false;
        }
    }

    private static string? GetFirstGenericTypeName(InvocationExpressionSyntax invocation)
    {
        SimpleNameSyntax? name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name,
            SimpleNameSyntax simpleName => simpleName,
            _ => null
        };

        return name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count > 0
            ? GetSimpleTypeName(generic.TypeArgumentList.Arguments[0])
            : null;
    }

    private static string GetSimpleTypeName(TypeSyntax type) =>
        type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            QualifiedNameSyntax qualified => GetSimpleTypeName(qualified.Right),
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            NullableTypeSyntax nullable => GetSimpleTypeName(nullable.ElementType),
            _ => type.ToString().Split('.').Last()
        };

    private static string? TargetFromConfigurationKey(string? key)
    {
        if (key is null)
        {
            return null;
        }

        string target = key.Split(':', 2)[0];
        return string.IsNullOrWhiteSpace(target) ? null : target;
    }

    private static string? TargetFromContract(string? contract)
    {
        if (string.IsNullOrWhiteSpace(contract))
        {
            return null;
        }

        string candidate = contract;
        if (candidate.Length > 1 &&
            candidate[0] == 'I' &&
            char.IsUpper(candidate[1]))
        {
            candidate = candidate[1..];
        }

        foreach (string suffix in new[] { "Client", "Api", "Service" })
        {
            if (candidate.EndsWith(suffix, StringComparison.Ordinal) &&
                candidate.Length > suffix.Length)
            {
                candidate = candidate[..^suffix.Length];
                break;
            }
        }

        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static bool TryGetHttpHost(string value, out string host)
    {
        host = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        host = uri.IdnHost;
        return true;
    }

    private static string Confidence(
        string? target,
        string? namedClient,
        string? configurationKey,
        string? host,
        string? contract)
    {
        if (target is null)
        {
            return IntegrationConfidence.Low;
        }

        return namedClient is not null || configurationKey is not null || host is not null
            ? IntegrationConfidence.High
            : contract is not null
                ? IntegrationConfidence.Medium
                : IntegrationConfidence.Low;
    }

    private static void AddEndpointSignals(
        List<string> signals,
        EndpointEvidence endpoint)
    {
        if (endpoint.ConfigurationKey is not null)
        {
            signals.Add("config:key");
        }

        if (endpoint.Host is not null)
        {
            signals.Add("http:literal-host");
        }

    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private sealed record EndpointEvidence(string? ConfigurationKey, string? Host)
    {
        public static EndpointEvidence Empty { get; } = new(null, null);
    }

    private static class IntegrationTechnology
    {
        public const string HttpClient = "httpclient";
        public const string Refit = "refit";
    }
}
