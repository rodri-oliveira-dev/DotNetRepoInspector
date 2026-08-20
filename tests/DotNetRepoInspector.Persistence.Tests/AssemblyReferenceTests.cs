using Xunit;

namespace DotNetRepoInspector.Persistence.Tests;

public sealed class AssemblyReferenceTests
{
    [Fact]
    public void PersistenceAssembly_DependsOnlyOnCoreAmongInspectorAssemblies()
    {
        var inspectorReferences = typeof(InspectionSnapshotPublisher)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("DotNetRepoInspector.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["DotNetRepoInspector.Core"], inspectorReferences);
    }
}
