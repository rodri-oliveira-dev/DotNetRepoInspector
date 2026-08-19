using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class AssemblyReferenceTests
{
    [Fact]
    public void MsBuildAssemblyCanBeReferenced()
    {
        Assert.Equal(
            "DotNetRepoInspector.MSBuild",
            typeof(global::DotNetRepoInspector.MSBuild.AssemblyMarker).Assembly.GetName().Name);
    }
}
