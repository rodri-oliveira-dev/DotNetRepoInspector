using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class AssemblyReferenceTests
{
    [Fact]
    public void CliAssemblyCanBeReferenced()
    {
        Assert.Equal(
            "DotNetRepoInspector.Cli",
            typeof(global::DotNetRepoInspector.Cli.Program).Assembly.GetName().Name);
    }
}
