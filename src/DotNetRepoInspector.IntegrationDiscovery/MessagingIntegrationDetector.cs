using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class MessagingIntegrationDetector : IIntegrationDetector
{
    private static readonly HashSet<string> CustomMessagingTypes =
        new(StringComparer.Ordinal)
        {
            "IEventBus",
            "IMessageBus",
            "IMessageConsumer",
            "IMessagePublisher"
        };

    public string Id => "messaging-provider-neutral";

    public ValueTask DetectAsync(
        IntegrationDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, TypeDescriptor> receivers = FindReceivers(context.Root);
        Dictionary<string, ResourceEvidence> sendEndpoints = FindMassTransitSendEndpoints(
            context,
            context.Root,
            receivers);

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

            TypeDescriptor? receiverType = GetReceiverType(receiver, receivers);
            if (TryAddRabbitMq(context, invocation, methodName, receiverType) ||
                TryAddMassTransit(context, invocation, methodName, receiver, receiverType, sendEndpoints) ||
                TryAddKafka(context, invocation, methodName, receiverType) ||
                TryAddNServiceBus(context, invocation, methodName, receiverType) ||
                TryAddCustomMessaging(context, invocation, methodName, receiverType))
            {
                continue;
            }
        }

        AddNServiceBusHandlers(context, cancellationToken);
        return ValueTask.CompletedTask;
    }

    private static bool TryAddRabbitMq(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string methodName,
        TypeDescriptor? receiverType)
    {
        if (receiverType is null ||
            receiverType.Name is not ("IModel" or "IChannel" or "ModelBase" or "Channel"))
        {
            return false;
        }

        if (methodName is "BasicPublish" or "BasicPublishAsync")
        {
            ResourceEvidence exchange = GetArgumentEvidence(
                context,
                invocation,
                "exchange",
                0);
            ResourceEvidence routingKey = GetArgumentEvidence(
                context,
                invocation,
                "routingKey",
                1);
            ResourceEvidence resource = exchange.IsPresent ? exchange : routingKey;
            string resourceType = exchange.IsPresent ? "exchange" : "routing-key";
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Publish,
                "rabbitmq",
                resource,
                resourceType,
                contract: null,
                ["messaging:rabbitmq", "rabbitmq:basic-publish"]);
            return true;
        }

        if (methodName is "BasicConsume" or "BasicConsumeAsync")
        {
            ResourceEvidence queue = GetArgumentEvidence(context, invocation, "queue", 0);
            string? consumerType = GetLastArgumentType(invocation, context.Root, "consumer");
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Consume,
                "rabbitmq",
                queue,
                "queue",
                consumerType,
                ["messaging:rabbitmq", "rabbitmq:basic-consume"]);
            return true;
        }

        return false;
    }

    private static bool TryAddMassTransit(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string methodName,
        ExpressionSyntax? receiver,
        TypeDescriptor? receiverType,
        Dictionary<string, ResourceEvidence> sendEndpoints)
    {
        if ((methodName is "Publish" or "PublishAsync") &&
            receiverType?.Name == "IPublishEndpoint")
        {
            string? contract = GetFirstGenericTypeName(invocation);
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Publish,
                "masstransit",
                ResourceEvidence.Empty,
                resourceType: null,
                contract,
                ["masstransit:publish", "messaging:masstransit"]);
            return true;
        }

        if ((methodName is "Send" or "SendAsync") &&
            receiverType?.Name == "ISendEndpoint")
        {
            ResourceEvidence endpoint = ResourceEvidence.Empty;
            if (receiver is IdentifierNameSyntax identifier &&
                sendEndpoints.TryGetValue(
                    identifier.Identifier.ValueText,
                    out ResourceEvidence? mapped) &&
                mapped is not null)
            {
                endpoint = mapped;
            }
            string? contract = GetFirstGenericTypeName(invocation);
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Publish,
                "masstransit",
                endpoint,
                "queue",
                contract,
                ["masstransit:send", "messaging:masstransit"]);
            return true;
        }

        if (methodName == "ReceiveEndpoint")
        {
            ResourceEvidence queue = GetArgumentEvidence(context, invocation, "queueName", 0);
            string? consumer = FindNestedGenericContract(invocation, "Consumer");
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Consume,
                "masstransit",
                queue,
                "queue",
                consumer,
                ["masstransit:receive-endpoint", "messaging:masstransit"]);
            return true;
        }

        if (methodName == "Consumer" &&
            !IsNestedInInvocation(invocation, "ReceiveEndpoint"))
        {
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Consume,
                "masstransit",
                ResourceEvidence.Empty,
                resourceType: null,
                GetFirstGenericTypeName(invocation),
                ["masstransit:consumer", "messaging:masstransit"]);
            return true;
        }

        return false;
    }

    private static bool TryAddKafka(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string methodName,
        TypeDescriptor? receiverType)
    {
        if (receiverType?.Name == "IProducer" &&
            (methodName is "Produce" or "ProduceAsync"))
        {
            ResourceEvidence topic = GetArgumentEvidence(context, invocation, "topic", 0);
            string? contract = receiverType.GenericArguments.Count > 0
                ? receiverType.GenericArguments[^1]
                : null;
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Publish,
                "kafka",
                topic,
                "topic",
                contract,
                ["kafka:produce", "messaging:kafka"]);
            return true;
        }

        if (receiverType?.Name == "IConsumer" && methodName == "Subscribe")
        {
            ResourceEvidence topic = GetArgumentEvidence(context, invocation, "topic", 0);
            string? contract = receiverType.GenericArguments.Count > 0
                ? receiverType.GenericArguments[^1]
                : null;
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Consume,
                "kafka",
                topic,
                "topic",
                contract,
                ["kafka:subscribe", "messaging:kafka"]);
            return true;
        }

        return false;
    }

    private static bool TryAddNServiceBus(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string methodName,
        TypeDescriptor? receiverType)
    {
        bool nServiceBusReceiver = receiverType?.Name is
            "IEndpointInstance" or
            "IMessageHandlerContext" or
            "IMessageSession";
        if (nServiceBusReceiver && (methodName is "Send" or "SendAsync"))
        {
            ResourceEvidence destination = invocation.ArgumentList.Arguments.Count >= 2
                ? GetArgumentEvidence(context, invocation, "destination", 0)
                : GetNamedArgumentEvidence(context, invocation, "destination");
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Publish,
                "nservicebus",
                destination,
                "queue",
                GetFirstGenericTypeName(invocation),
                ["messaging:nservicebus", "nservicebus:send"]);
            return true;
        }

        if (nServiceBusReceiver && (methodName is "Publish" or "PublishAsync"))
        {
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Publish,
                "nservicebus",
                ResourceEvidence.Empty,
                resourceType: null,
                GetFirstGenericTypeName(invocation),
                ["messaging:nservicebus", "nservicebus:publish"]);
            return true;
        }

        if (methodName == "RouteToEndpoint")
        {
            ResourceEvidence destination = GetArgumentEvidence(
                context,
                invocation,
                "destination",
                invocation.ArgumentList.Arguments.Count - 1);
            string? contract = FindTypeOfContract(invocation);
            AddFinding(
                context,
                invocation,
                IntegrationDirection.Publish,
                "nservicebus",
                destination,
                "queue",
                contract,
                ["messaging:nservicebus", "nservicebus:route"]);
            return true;
        }

        return false;
    }

    private static bool TryAddCustomMessaging(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string methodName,
        TypeDescriptor? receiverType)
    {
        if (receiverType is null || !CustomMessagingTypes.Contains(receiverType.Name))
        {
            return false;
        }

        string? direction = methodName switch
        {
            "Publish" or "PublishAsync" or "Send" or "SendAsync" =>
                IntegrationDirection.Publish,
            "Consume" or "ConsumeAsync" or "Subscribe" or "SubscribeAsync" =>
                IntegrationDirection.Consume,
            _ => null
        };
        if (direction is null)
        {
            return false;
        }

        ResourceEvidence resource = invocation.ArgumentList.Arguments.Count >= 2
            ? GetArgumentEvidence(context, invocation, "target", 0)
            : ResourceEvidence.Empty;
        string? contract = GetFirstGenericTypeName(invocation);
        AddFinding(
            context,
            invocation,
            direction,
            "unknown",
            resource,
            resourceType: null,
            contract,
            ["messaging:custom-abstraction", $"messaging:{direction}"]);
        return true;
    }

    private static void AddNServiceBusHandlers(
        IntegrationDetectionContext context,
        CancellationToken cancellationToken)
    {
        foreach (ClassDeclarationSyntax declaration in context.Root
                     .DescendantNodes()
                     .OfType<ClassDeclarationSyntax>()
                     .OrderBy(static declaration => declaration.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenericNameSyntax? handlerInterface = declaration.BaseList?.Types
                .SelectMany(static baseType => baseType.Type
                    .DescendantNodesAndSelf()
                    .OfType<GenericNameSyntax>())
                .OfType<GenericNameSyntax>()
                .FirstOrDefault(static generic =>
                    generic.Identifier.ValueText == "IHandleMessages" &&
                    generic.TypeArgumentList.Arguments.Count == 1);
            if (handlerInterface is null)
            {
                continue;
            }

            string contract = GetSimpleTypeName(handlerInterface.TypeArgumentList.Arguments[0]);
            context.Findings.TryAdd(new IntegrationFindingCandidate(
                IntegrationKind.Messaging,
                IntegrationDirection.Consume,
                "nservicebus",
                context.GetOneBasedLine(declaration),
                IntegrationConfidence.High,
                ["messaging:nservicebus", "nservicebus:handler"],
                Contract: contract));
        }
    }

    private static Dictionary<string, TypeDescriptor> FindReceivers(CompilationUnitSyntax root)
    {
        var result = new Dictionary<string, TypeDescriptor>(StringComparer.Ordinal);
        foreach (VariableDeclarationSyntax declaration in root
                     .DescendantNodes()
                     .OfType<VariableDeclarationSyntax>())
        {
            TypeDescriptor descriptor = DescribeType(declaration.Type);
            foreach (VariableDeclaratorSyntax variable in declaration.Variables)
            {
                if (descriptor.Name != "var")
                {
                    result[variable.Identifier.ValueText] = descriptor;
                }
                else if (TryInferType(variable.Initializer?.Value, out TypeDescriptor? inferred))
                {
                    result[variable.Identifier.ValueText] = inferred;
                }
            }
        }

        foreach (ParameterSyntax parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is not null)
            {
                result[parameter.Identifier.ValueText] = DescribeType(parameter.Type);
            }
        }

        return result;
    }

    private static bool TryInferType(
        ExpressionSyntax? expression,
        out TypeDescriptor descriptor)
    {
        descriptor = TypeDescriptor.Unknown;
        expression = UnwrapExpression(expression);
        if (expression is ObjectCreationExpressionSyntax creation)
        {
            TypeDescriptor created = DescribeType(creation.Type);
            descriptor = created.Name switch
            {
                "ProducerBuilder" => created with { Name = "IProducer" },
                "ConsumerBuilder" => created with { Name = "IConsumer" },
                _ => created
            };
            return descriptor != TypeDescriptor.Unknown;
        }

        if (expression is not InvocationExpressionSyntax invocation ||
            !TryGetInvocation(invocation, out string methodName, out _))
        {
            return false;
        }

        if (methodName is "CreateModel" or "CreateChannel" or "CreateChannelAsync")
        {
            descriptor = new TypeDescriptor("IChannel", Array.Empty<string>());
            return true;
        }

        GenericNameSyntax? builder = invocation.DescendantNodesAndSelf()
            .OfType<GenericNameSyntax>()
            .FirstOrDefault(static generic =>
                generic.Identifier.ValueText is "ProducerBuilder" or "ConsumerBuilder");
        if (methodName == "Build" && builder is not null)
        {
            descriptor = new TypeDescriptor(
                builder.Identifier.ValueText == "ProducerBuilder" ? "IProducer" : "IConsumer",
                builder.TypeArgumentList.Arguments.Select(GetSimpleTypeName).ToArray());
            return true;
        }

        if (methodName == "GetSendEndpoint")
        {
            descriptor = new TypeDescriptor("ISendEndpoint", Array.Empty<string>());
            return true;
        }

        return false;
    }

    private static Dictionary<string, ResourceEvidence> FindMassTransitSendEndpoints(
        IntegrationDetectionContext context,
        CompilationUnitSyntax root,
        Dictionary<string, TypeDescriptor> receivers)
    {
        var result = new Dictionary<string, ResourceEvidence>(StringComparer.Ordinal);
        foreach (VariableDeclaratorSyntax variable in root
                     .DescendantNodes()
                     .OfType<VariableDeclaratorSyntax>())
        {
            ExpressionSyntax? expression = UnwrapExpression(variable.Initializer?.Value);
            if (expression is not InvocationExpressionSyntax invocation ||
                !TryGetInvocation(invocation, out string methodName, out _) ||
                methodName != "GetSendEndpoint")
            {
                continue;
            }

            receivers[variable.Identifier.ValueText] =
                new TypeDescriptor("ISendEndpoint", Array.Empty<string>());
            ResourceEvidence evidence = GetArgumentEvidence(context, invocation, "address", 0);
            result[variable.Identifier.ValueText] = evidence with
            {
                Target = TargetFromEndpointAddress(evidence.Target)
            };
        }

        return result;
    }

    private static ResourceEvidence GetArgumentEvidence(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string parameterName,
        int positionalIndex)
    {
        ResourceEvidence named = GetNamedArgumentEvidence(context, invocation, parameterName);
        if (named.IsPresent)
        {
            return named;
        }

        if (positionalIndex < 0 || positionalIndex >= invocation.ArgumentList.Arguments.Count)
        {
            return ResourceEvidence.Empty;
        }

        return GetResourceEvidence(
            context,
            invocation.ArgumentList.Arguments[positionalIndex].Expression);
    }

    private static ResourceEvidence GetNamedArgumentEvidence(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string parameterName)
    {
        ArgumentSyntax? argument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText == parameterName);
        return argument is null
            ? ResourceEvidence.Empty
            : GetResourceEvidence(context, argument.Expression);
    }

    private static ResourceEvidence GetResourceEvidence(
        IntegrationDetectionContext context,
        SyntaxNode expression)
    {
        foreach (ElementAccessExpressionSyntax elementAccess in expression
                     .DescendantNodesAndSelf()
                     .OfType<ElementAccessExpressionSyntax>())
        {
            if (!IsConfigurationReceiver(elementAccess.Expression))
            {
                continue;
            }

            ExpressionSyntax? keyExpression = elementAccess.ArgumentList.Arguments
                .FirstOrDefault()?.Expression;
            if (keyExpression is not null &&
                context.Evidence.TryGetConfigurationKey(keyExpression, out string key))
            {
                return new ResourceEvidence(null, key);
            }
        }

        foreach (LiteralExpressionSyntax literal in expression
                     .DescendantNodesAndSelf()
                     .OfType<LiteralExpressionSyntax>())
        {
            if (context.Evidence.TryGetStringLiteral(literal, out string value))
            {
                return new ResourceEvidence(value, null);
            }
        }

        return ResourceEvidence.Empty;
    }

    private static void AddFinding(
        IntegrationDetectionContext context,
        SyntaxNode source,
        string direction,
        string technology,
        ResourceEvidence resource,
        string? resourceType,
        string? contract,
        IReadOnlyList<string> signals)
    {
        string confidence = resource.IsPresent
            ? IntegrationConfidence.High
            : contract is not null
                ? IntegrationConfidence.Medium
                : technology == "unknown"
                    ? IntegrationConfidence.Low
                    : IntegrationConfidence.Medium;
        context.Findings.TryAdd(new IntegrationFindingCandidate(
            IntegrationKind.Messaging,
            direction,
            technology,
            context.GetOneBasedLine(source),
            confidence,
            signals,
            Target: resource.Target,
            ResourceType: resourceType,
            ConfigurationKey: resource.ConfigurationKey,
            Contract: contract));
    }

    private static TypeDescriptor? GetReceiverType(
        ExpressionSyntax? receiver,
        Dictionary<string, TypeDescriptor> receivers)
    {
        string? receiverName = receiver switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null
        };
        return receiverName is not null && receivers.TryGetValue(receiverName, out TypeDescriptor? descriptor)
            ? descriptor
            : null;
    }

    private static string? GetLastArgumentType(
        InvocationExpressionSyntax invocation,
        SyntaxNode root,
        string parameterName)
    {
        ArgumentSyntax? argument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText == parameterName) ??
            invocation.ArgumentList.Arguments.LastOrDefault();
        if (argument?.Expression is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        return FindReceivers((CompilationUnitSyntax)root)
            .GetValueOrDefault(identifier.Identifier.ValueText)?.Name;
    }

    private static string? FindNestedGenericContract(
        InvocationExpressionSyntax invocation,
        string methodName) =>
        invocation.ArgumentList.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(nested => TryGetInvocation(nested, out string name, out _) && name == methodName)
            .Select(GetFirstGenericTypeName)
            .FirstOrDefault(static contract => contract is not null);

    private static string? FindTypeOfContract(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.DescendantNodes()
            .OfType<TypeOfExpressionSyntax>()
            .Select(static typeOf => GetSimpleTypeName(typeOf.Type))
            .FirstOrDefault();

    private static bool IsNestedInInvocation(
        InvocationExpressionSyntax invocation,
        string methodName) =>
        invocation.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(ancestor =>
                TryGetInvocation(ancestor, out string ancestorName, out _) &&
                ancestorName == methodName);

    private static ExpressionSyntax? UnwrapExpression(ExpressionSyntax? expression) =>
        expression switch
        {
            AwaitExpressionSyntax awaitExpression => UnwrapExpression(awaitExpression.Expression),
            ParenthesizedExpressionSyntax parenthesized => UnwrapExpression(parenthesized.Expression),
            CastExpressionSyntax cast => UnwrapExpression(cast.Expression),
            _ => expression
        };

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

    private static TypeDescriptor DescribeType(TypeSyntax type) =>
        type switch
        {
            GenericNameSyntax generic => new TypeDescriptor(
                generic.Identifier.ValueText,
                generic.TypeArgumentList.Arguments.Select(GetSimpleTypeName).ToArray()),
            QualifiedNameSyntax qualified => DescribeType(qualified.Right),
            AliasQualifiedNameSyntax alias => new TypeDescriptor(
                alias.Name.Identifier.ValueText,
                Array.Empty<string>()),
            NullableTypeSyntax nullable => DescribeType(nullable.ElementType),
            IdentifierNameSyntax identifier => new TypeDescriptor(
                identifier.Identifier.ValueText,
                Array.Empty<string>()),
            _ => new TypeDescriptor(GetSimpleTypeName(type), Array.Empty<string>())
        };

    private static string GetSimpleTypeName(TypeSyntax type) => DescribeTypeWithoutFallback(type).Name;

    private static TypeDescriptor DescribeTypeWithoutFallback(TypeSyntax type) =>
        type switch
        {
            GenericNameSyntax generic => new TypeDescriptor(
                generic.Identifier.ValueText,
                generic.TypeArgumentList.Arguments.Select(GetSimpleTypeName).ToArray()),
            QualifiedNameSyntax qualified => DescribeTypeWithoutFallback(qualified.Right),
            AliasQualifiedNameSyntax alias => new TypeDescriptor(
                alias.Name.Identifier.ValueText,
                Array.Empty<string>()),
            NullableTypeSyntax nullable => DescribeTypeWithoutFallback(nullable.ElementType),
            IdentifierNameSyntax identifier => new TypeDescriptor(
                identifier.Identifier.ValueText,
                Array.Empty<string>()),
            _ => new TypeDescriptor(type.ToString().Split('.').Last(), Array.Empty<string>())
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

    private static string? TargetFromEndpointAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        string withoutQuery = address.Split('?', 2)[0].TrimEnd('/');
        int separator = Math.Max(withoutQuery.LastIndexOf('/'), withoutQuery.LastIndexOf(':'));
        string target = separator >= 0 ? withoutQuery[(separator + 1)..] : withoutQuery;
        return string.IsNullOrWhiteSpace(target) ? null : target;
    }

    private sealed record TypeDescriptor(string Name, IReadOnlyList<string> GenericArguments)
    {
        public static TypeDescriptor Unknown
        {
            get;
        } =
            new(string.Empty, Array.Empty<string>());
    }

    private sealed record ResourceEvidence(string? Target, string? ConfigurationKey)
    {
        public bool IsPresent => Target is not null || ConfigurationKey is not null;

        public static ResourceEvidence Empty { get; } = new(null, null);
    }
}
