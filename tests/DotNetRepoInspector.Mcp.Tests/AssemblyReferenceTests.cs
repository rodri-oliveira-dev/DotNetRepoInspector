using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Mcp.Tests;

public sealed class AssemblyReferenceTests
{
    [Fact]
    public void McpAssembly_DependsOnEngineButNotCliOrLlmProviders()
    {
        var references = typeof(McpApplication)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name)
            .Where(static name => name is not null)
            .ToArray();

        Assert.Contains("DotNetRepoInspector.Engine", references);
        Assert.Contains("ModelContextProtocol", references);
        Assert.DoesNotContain("DotNetRepoInspector.Cli", references);
        Assert.DoesNotContain(references, IsLlmProviderAssembly);
    }

    [Fact]
    public void CoreAndEngine_DoNotDependOnMcp()
    {
        Assert.DoesNotContain(
            "DotNetRepoInspector.Mcp",
            typeof(InspectionReport).Assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name));
        Assert.DoesNotContain(
            "DotNetRepoInspector.Mcp",
            typeof(IRepositoryInspector).Assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name));
    }

    private static bool IsLlmProviderAssembly(string? name) =>
        name is not null &&
        (name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Anthropic", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Google.GenerativeAI", StringComparison.OrdinalIgnoreCase));
}
