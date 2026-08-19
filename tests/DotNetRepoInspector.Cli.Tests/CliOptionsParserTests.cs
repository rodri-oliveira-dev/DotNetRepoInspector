using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class CliOptionsParserTests
{
    [Fact]
    public void Parse_DefaultsRepositoryPathToCurrentDirectory()
    {
        var result = CliOptionsParser.Parse(Array.Empty<string>());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Options);
        Assert.Equal(".", result.Options.RepositoryPath);
        Assert.Null(result.Options.OutputPath);
        Assert.Equal(CliVerbosity.Normal, result.Options.Verbosity);
    }

    [Fact]
    public void Parse_ReadsRepositoryOutputAndVerbosity()
    {
        var result = CliOptionsParser.Parse(
            ["../repository", "--output", "inspection.json", "--verbose"]);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Options);
        Assert.Equal("../repository", result.Options.RepositoryPath);
        Assert.Equal("inspection.json", result.Options.OutputPath);
        Assert.Equal(CliVerbosity.Verbose, result.Options.Verbosity);
    }

    [Fact]
    public void Parse_SupportsInlineOutputValue()
    {
        var result = CliOptionsParser.Parse(["--output=inspection.json"]);

        Assert.True(result.Succeeded);
        Assert.Equal("inspection.json", result.Options?.OutputPath);
    }

    [Fact]
    public void Parse_DebugOverridesVerbose()
    {
        var result = CliOptionsParser.Parse(["--verbose", "--debug"]);

        Assert.True(result.Succeeded);
        Assert.Equal(CliVerbosity.Debug, result.Options?.Verbosity);
    }

    [Fact]
    public void Parse_RejectsMissingOutputPath()
    {
        var result = CliOptionsParser.Parse(["--output"]);

        Assert.False(result.Succeeded);
        Assert.Contains(
            "requires a file path",
            result.Error ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsMultipleRepositoryPaths()
    {
        var result = CliOptionsParser.Parse(["first", "second"]);

        Assert.False(result.Succeeded);
        Assert.Contains(
            "Only one repository path",
            result.Error ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DoesNotEchoUnknownOptionValuesInErrors()
    {
        const string sensitiveArgument = "--token=do-not-log-this";

        var result = CliOptionsParser.Parse([sensitiveArgument]);
        var error = result.Error ?? string.Empty;

        Assert.False(result.Succeeded);
        Assert.Equal("An unknown command-line option was provided.", error);
        Assert.DoesNotContain("do-not-log-this", error, StringComparison.Ordinal);
    }
}
