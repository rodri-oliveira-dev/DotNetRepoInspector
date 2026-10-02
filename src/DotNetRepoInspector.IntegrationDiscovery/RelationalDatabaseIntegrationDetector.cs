using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class RelationalDatabaseIntegrationDetector : IIntegrationDetector
{
    private static readonly Dictionary<string, string> ClientTechnologies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NpgsqlConnection"] = "postgresql",
            ["NpgsqlCommand"] = "postgresql",
            ["SqlConnection"] = "sqlserver",
            ["SqlCommand"] = "sqlserver",
            ["MySqlConnection"] = "mysql",
            ["MySqlCommand"] = "mysql",
            ["OracleConnection"] = "oracle",
            ["OracleCommand"] = "oracle"
        };

    private static readonly Dictionary<string, string> RegistrationTechnologies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AddNpgsqlDataSource"] = "postgresql",
            ["UseNpgsql"] = "postgresql",
            ["AddSqlServer"] = "sqlserver",
            ["UseSqlServer"] = "sqlserver",
            ["AddMySqlDataSource"] = "mysql",
            ["UseMySql"] = "mysql",
            ["AddOracle"] = "oracle",
            ["UseOracle"] = "oracle"
        };

    public string Id => "data-relational-databases";

    public ValueTask DetectAsync(IntegrationDetectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        VariableTypeMap types = CloudMessagingSyntax.FindVariableTypes(context.Root);

        foreach (ObjectCreationExpressionSyntax creation in context.Root.DescendantNodes()
                     .OfType<ObjectCreationExpressionSyntax>().OrderBy(static node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string type = CloudMessagingSyntax.SimpleName(creation.Type);
            if (!ClientTechnologies.TryGetValue(type, out string? technology) || !type.EndsWith("Connection", StringComparison.Ordinal))
            {
                continue;
            }

            ResourceEvidence resource = DataIntegrationSyntax.ConnectionStringEvidence(context, creation);
            DataIntegrationSyntax.Add(context, creation, IntegrationKind.Database, IntegrationDirection.Bidirectional,
                technology, resource, "database", [$"database:{technology}", "database:connection"]);
        }

        foreach (InvocationExpressionSyntax invocation in context.Root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>().OrderBy(static node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CloudMessagingSyntax.TryGetInvocation(invocation, out string method, out ExpressionSyntax? receiver))
            {
                continue;
            }

            if (RegistrationTechnologies.TryGetValue(method, out string? registrationTechnology))
            {
                ResourceEvidence resource = DataIntegrationSyntax.ConnectionStringEvidence(context, invocation);
                DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Bidirectional,
                    registrationTechnology, resource, "database",
                    [$"database:{registrationTechnology}", "database:registration"]);
                continue;
            }

            string? receiverType = CloudMessagingSyntax.ReceiverType(receiver, types);
            if (receiverType is null || !ClientTechnologies.TryGetValue(receiverType, out string? technology))
            {
                continue;
            }

            string? direction = method switch
            {
                "ExecuteReader" or "ExecuteReaderAsync" or "ExecuteScalar" or "ExecuteScalarAsync" => IntegrationDirection.Read,
                "ExecuteNonQuery" or "ExecuteNonQueryAsync" => IntegrationDirection.Write,
                _ => null
            };
            if (direction is not null)
            {
                DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, direction, technology,
                    ResourceEvidence.Empty, "database", [$"database:{technology}", $"database:{direction}"]);
            }
        }

        return ValueTask.CompletedTask;
    }
}
