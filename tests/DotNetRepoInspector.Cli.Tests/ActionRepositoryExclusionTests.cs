using System.Diagnostics;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class ActionRepositoryExclusionTests
{
    [Fact]
    public async Task RepositoryExclusion_UsesCompleteRepositoryIdentifier()
    {
        var result = await InvokePowerShellAsync($"""
            . '{PowerShellLiteral(RepositoryExclusionScriptPath)}'
            $matching = Test-RepositoryExcluded `
              -Repository 'rodri-oliveira-dev/DotNetRepoInspector' `
              -ExcludedRepositories "example/Other`nrodri-oliveira-dev/DotNetRepoInspector"
            $similar = Test-RepositoryExcluded `
              -Repository 'rodri-oliveira-dev/DotNetRepoInspector.Fixtures' `
              -ExcludedRepositories 'rodri-oliveira-dev/DotNetRepoInspector'
            "$matching|$similar"
            """);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("True|False", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RepositoryExclusion_RejectsPartialRepositoryIdentifier()
    {
        var result = await InvokePowerShellAsync($"""
            . '{PowerShellLiteral(RepositoryExclusionScriptPath)}'
            try {{
                Test-RepositoryExcluded `
                  -Repository 'rodri-oliveira-dev/DotNetRepoInspector' `
                  -ExcludedRepositories 'DotNetRepoInspector'
            }}
            catch {{
                [Console]::Error.WriteLine($_.Exception.Message)
                exit 1
            }}
            """);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "must use the full owner/repository identifier",
            result.StandardError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeScript_SkipsExplicitlyExcludedRepositoryBeforeInspection()
    {
        string temporaryDirectory = Directory.CreateTempSubdirectory("DotNetRepoInspector-Action-").FullName;

        try
        {
            string outputPath = Path.Combine(temporaryDirectory, "github-output.txt");
            string workspace = Path.Combine(temporaryDirectory, "workspace");
            string runnerTemp = Path.Combine(temporaryDirectory, "runner-temp");
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(runnerTemp);

            var result = await InvokePowerShellAsync(
                $"& '{PowerShellLiteral(ActionInvokeScriptPath)}'",
                new Dictionary<string, string?>
                {
                    ["GITHUB_OUTPUT"] = outputPath,
                    ["GITHUB_REPOSITORY"] = "rodri-oliveira-dev/DotNetRepoInspector",
                    ["GITHUB_WORKSPACE"] = workspace,
                    ["RUNNER_TEMP"] = runnerTemp,
                    ["DRI_INPUT_EXCLUDE_REPOSITORIES"] =
                        "example/Other" + Environment.NewLine + "rodri-oliveira-dev/DotNetRepoInspector"
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                "explicitly excluded from fleet inventory",
                result.StandardOutput,
                StringComparison.Ordinal);

            Dictionary<string, string> outputs = ReadGitHubOutputs(outputPath);
            Assert.Equal("true", outputs["repository-excluded"]);
            Assert.Equal("0", outputs["exit-code"]);
            Assert.Equal(string.Empty, outputs["report-path"]);
            Assert.Equal(string.Empty, outputs["schema-version"]);
            Assert.Equal(string.Empty, outputs["inspector-version"]);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AllowsDirectInspectionOfDotNetRepoInspectorRepository()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(new RepositoryInspector(), "1.0.0-test");

        var exitCode = await application.RunAsync(
            [RepositoryRoot],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(CliExitCodes.InspectionFailed, exitCode);
        Assert.NotEqual(CliExitCodes.InvalidArguments, exitCode);

        InspectionReport report = InspectionJsonSerializer.Deserialize(output.ToString());
        Assert.Contains(
            report.Projects,
            static project => project.Path == "src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj");
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string ActionInvokeScriptPath =>
        Path.Combine(RepositoryRoot, ".github", "action", "invoke.ps1");

    private static string RepositoryExclusionScriptPath =>
        Path.Combine(RepositoryRoot, ".github", "action", "repository-exclusion.ps1");

    private static string PowerShellExecutable =>
        OperatingSystem.IsWindows() ? "powershell" : "pwsh";

    private static async Task<PowerShellResult> InvokePowerShellAsync(
        string command,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(PowerShellExecutable)
        {
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                command
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (environment is not null)
        {
            foreach (KeyValuePair<string, string?> pair in environment)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value;
            }
        }

        Assert.True(process.Start(), "PowerShell process could not be started.");
        string standardOutput = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string standardError = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return new PowerShellResult(process.ExitCode, standardOutput, standardError);
    }

    private static string PowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static Dictionary<string, string> ReadGitHubOutputs(string outputPath)
    {
        string[] lines = File.ReadAllLines(outputPath);
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int delimiterIndex = line.IndexOf("<<", StringComparison.Ordinal);
            Assert.True(delimiterIndex > 0, $"Invalid GitHub output line '{line}'.");

            string name = line[..delimiterIndex];
            string delimiter = line[(delimiterIndex + 2)..];
            index++;

            var valueLines = new List<string>();
            while (index < lines.Length && !string.Equals(lines[index], delimiter, StringComparison.Ordinal))
            {
                valueLines.Add(lines[index]);
                index++;
            }

            Assert.True(index < lines.Length, $"Output '{name}' was not terminated.");
            outputs[name] = string.Join(Environment.NewLine, valueLines);
        }

        return outputs;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DotNetRepoInspector.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the DotNetRepoInspector repository root.");
    }

    private sealed record PowerShellResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
