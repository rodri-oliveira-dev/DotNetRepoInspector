using System.Globalization;
using System.Text;

namespace DotNetRepoInspector.Performance;

internal sealed class SyntheticRepository : IDisposable
{
    private readonly string _rootPath;

    private SyntheticRepository(string rootPath)
    {
        _rootPath = rootPath;
    }

    public string RootPath => _rootPath;

    public static SyntheticRepository Create(int projectCount)
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"DotNetRepoInspector-Performance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);

        File.WriteAllText(
            Path.Combine(rootPath, "global.json"),
            """
            {
              "sdk": {
                "version": "10.0.100",
                "rollForward": "latestFeature",
                "allowPrerelease": false
              }
            }
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        File.WriteAllText(
            Path.Combine(rootPath, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var sourceRoot = Path.Combine(rootPath, "src");
        Directory.CreateDirectory(sourceRoot);

        for (var index = 0; index < projectCount; index++)
        {
            var indexText = index.ToString("D4", CultureInfo.InvariantCulture);
            var projectName = $"Project{indexText}";
            var projectDirectory = Path.Combine(sourceRoot, projectName);
            Directory.CreateDirectory(projectDirectory);

            var projectReference = string.Empty;
            if (index > 0)
            {
                var previousIndex = (index - 1).ToString("D4", CultureInfo.InvariantCulture);
                projectReference = $"""
                    <ItemGroup>
                      <ProjectReference Include="../Project{previousIndex}/Project{previousIndex}.csproj" />
                    </ItemGroup>
                  """;
            }

            var projectContent = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                {projectReference}
                </Project>
                """;

            File.WriteAllText(
                Path.Combine(projectDirectory, $"{projectName}.csproj"),
                projectContent,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return new SyntheticRepository(rootPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (IOException)
        {
            // Performance results are more valuable than temporary-directory cleanup failures.
        }
        catch (UnauthorizedAccessException)
        {
            // Performance results are more valuable than temporary-directory cleanup failures.
        }
    }
}
