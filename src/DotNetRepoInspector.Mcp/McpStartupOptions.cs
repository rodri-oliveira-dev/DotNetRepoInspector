namespace DotNetRepoInspector.Mcp;

public sealed record McpStartupOptions(RepositoryRoot RepositoryRoot)
{
    public static McpStartupOptionsParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? root = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--root", StringComparison.Ordinal))
            {
                if (root is not null || index + 1 >= args.Count)
                {
                    return McpStartupOptionsParseResult.Failure(
                        "The MCP server requires exactly one --root <path> argument.");
                }

                root = args[++index];
                continue;
            }

            if (argument.StartsWith("--root=", StringComparison.Ordinal))
            {
                if (root is not null)
                {
                    return McpStartupOptionsParseResult.Failure(
                        "The MCP server requires exactly one --root <path> argument.");
                }

                root = argument["--root=".Length..];
                continue;
            }

            return McpStartupOptionsParseResult.Failure(
                "An unknown MCP server argument was supplied.");
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return McpStartupOptionsParseResult.Failure(
                "The MCP server requires an explicit --root <path> argument.");
        }

        try
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (!Directory.Exists(fullRoot))
            {
                return McpStartupOptionsParseResult.Failure(
                    "The configured repository root does not exist or is not a directory.");
            }

            var normalizedRoot = RepositoryRootCanonicalizer.NormalizeExistingDirectory(root);

            return McpStartupOptionsParseResult.Success(
                new McpStartupOptions(new RepositoryRoot(normalizedRoot)));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return McpStartupOptionsParseResult.Failure(
                "The configured repository root is not a valid path.");
        }
    }
}

public sealed record McpStartupOptionsParseResult(
    McpStartupOptions? Options,
    string? Error)
{
    public bool Succeeded => Options is not null;

    public static McpStartupOptionsParseResult Success(McpStartupOptions options) =>
        new(options, null);

    public static McpStartupOptionsParseResult Failure(string error) =>
        new(null, error);
}
