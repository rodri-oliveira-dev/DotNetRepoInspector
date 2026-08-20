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
        Assert.Null(result.Options.ConfigurationPath);
        Assert.False(result.Options.DisableConfigurationFile);
        Assert.Empty(result.Options.ExcludedPaths);
        Assert.Empty(result.Options.ClassificationOverrides);
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
    public void Parse_ReadsConfigurationExclusionsAndClassificationOverrides()
    {
        var result = CliOptionsParser.Parse(
            [
                ".",
                "--config",
                "config/inspector.json",
                "--exclude",
                "generated",
                "--exclude=samples/Legacy.csproj",
                "--classify",
                "src/App/App.csproj=WEB",
                "--classify=src/Worker/Worker.csproj=worker"
            ]);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Options);
        Assert.Equal("config/inspector.json", result.Options.ConfigurationPath);
        Assert.Equal(
            ["generated", "samples/Legacy.csproj"],
            result.Options.ExcludedPaths);
        Assert.Equal("web", result.Options.ClassificationOverrides["src/App/App.csproj"]);
        Assert.Equal("worker", result.Options.ClassificationOverrides["src/Worker/Worker.csproj"]);
    }

    [Fact]
    public void Parse_NoConfigDisablesRepositoryConfiguration()
    {
        var result = CliOptionsParser.Parse(["--no-config"]);

        Assert.True(result.Succeeded);
        Assert.True(result.Options?.DisableConfigurationFile);
    }

    [Fact]
    public void Parse_RejectsConfigAndNoConfigTogether()
    {
        var result = CliOptionsParser.Parse(["--config", "settings.json", "--no-config"]);

        Assert.False(result.Succeeded);
        Assert.Contains(
            "cannot be used together",
            result.Error ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsInvalidClassificationOverride()
    {
        var result = CliOptionsParser.Parse(["--classify", "src/App/App.csproj=invalid-kind"]);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "The --classify option requires a unique '<project-path>=<kind>' value using a supported kind.",
            result.Error);
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
