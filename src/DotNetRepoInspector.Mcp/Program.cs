namespace DotNetRepoInspector.Mcp;

public static class Program
{
    public static Task<int> Main(string[] args) => McpApplication.RunAsync(args);
}
