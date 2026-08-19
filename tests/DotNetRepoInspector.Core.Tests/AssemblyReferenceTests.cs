using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class AssemblyReferenceTests
{
    [Fact]
    public void CoreAssemblyCanBeReferenced()
    {
        Assert.Equal(
            "DotNetRepoInspector.Core",
            typeof(global::DotNetRepoInspector.Core.AssemblyMarker).Assembly.GetName().Name);
    }
}
