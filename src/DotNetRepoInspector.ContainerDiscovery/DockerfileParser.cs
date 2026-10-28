using System.Text.RegularExpressions;

namespace DotNetRepoInspector.ContainerDiscovery;

internal static partial class DockerfileParser
{
    private const int MaximumReferenceLength = 512;

    public static ParseResult Parse(string sourcePath, string content, CancellationToken cancellationToken)
    {
        var images = new List<DockerImageEvidence>();
        var diagnostics = new List<ContainerDiscoveryDiagnostic>();
        var arguments = new Dictionary<string, ArgumentValue>(StringComparer.Ordinal);
        var stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool encounteredFrom = false;

        foreach (LogicalInstruction instruction in ReadInstructions(content, diagnostics, sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (instruction.Keyword.Equals("ARG", StringComparison.OrdinalIgnoreCase))
            {
                if (!encounteredFrom)
                {
                    ParseArgument(instruction.Value, arguments);
                }

                continue;
            }

            if (!instruction.Keyword.Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            encounteredFrom = true;
            FromParseResult parsed = ParseFrom(instruction.Value, arguments, stages);
            if (parsed.Malformed)
            {
                diagnostics.Add(new ContainerDiscoveryDiagnostic(
                    ContainerDiscoveryDiagnosticCodes.DockerfileParseFailed,
                    "malformed-from",
                    sourcePath,
                    instruction.Line));
            }

            images.Add(new DockerImageEvidence(
                sourcePath,
                instruction.Line,
                parsed.ImageReference,
                parsed.Registry,
                parsed.Repository,
                parsed.Tag,
                parsed.Digest,
                parsed.StageAlias,
                parsed.ReferenceKind,
                parsed.Resolved
                    ? DockerImageResolutionStatus.Resolved
                    : DockerImageResolutionStatus.Unresolved,
                parsed.DotNetFamily,
                parsed.IsOfficialDotNetImage));

            if (parsed.StageAlias is not null)
            {
                if (!stages.Add(parsed.StageAlias))
                {
                    diagnostics.Add(new ContainerDiscoveryDiagnostic(
                        ContainerDiscoveryDiagnosticCodes.DockerfileParseFailed,
                        "duplicate-stage-alias",
                        sourcePath,
                        instruction.Line));
                }
            }
        }

        return new ParseResult(images, diagnostics);
    }

    private static IEnumerable<LogicalInstruction> ReadInstructions(
        string content,
        List<ContainerDiscoveryDiagnostic> diagnostics,
        string sourcePath)
    {
        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var value = new System.Text.StringBuilder();
        int startLine = 0;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (startLine == 0)
            {
                startLine = index + 1;
            }

            bool continues = HasContinuation(line);
            if (continues)
            {
                line = line[..^1].TrimEnd();
            }

            if (value.Length > 0 && line.Length > 0)
            {
                value.Append(' ');
            }

            value.Append(line);
            if (continues)
            {
                continue;
            }

            string logicalLine = value.ToString();
            value.Clear();
            int separator = IndexOfWhitespace(logicalLine);
            string keyword = separator < 0 ? logicalLine : logicalLine[..separator];
            string instructionValue = separator < 0 ? string.Empty : logicalLine[separator..].Trim();
            yield return new LogicalInstruction(keyword, instructionValue, startLine);
            startLine = 0;
        }

        if (value.Length > 0)
        {
            diagnostics.Add(new ContainerDiscoveryDiagnostic(
                ContainerDiscoveryDiagnosticCodes.DockerfileParseFailed,
                "incomplete-line-continuation",
                sourcePath,
                startLine));
        }
    }

    private static string StripComment(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith('#'))
        {
            return string.Empty;
        }

        for (int index = 1; index < line.Length; index++)
        {
            if (line[index] == '#' && char.IsWhiteSpace(line[index - 1]))
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool HasContinuation(string line)
    {
        int slashCount = 0;
        for (int index = line.Length - 1; index >= 0 && line[index] == '\\'; index--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private static int IndexOfWhitespace(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static void ParseArgument(string value, Dictionary<string, ArgumentValue> arguments)
    {
        int equalsIndex = value.IndexOf('=');
        string name = (equalsIndex < 0 ? value : value[..equalsIndex]).Trim();
        if (!ArgumentNameRegex().IsMatch(name))
        {
            return;
        }

        if (IsSensitiveArgumentName(name))
        {
            arguments[name] = ArgumentValue.Unknown;
            return;
        }

        if (equalsIndex < 0)
        {
            arguments[name] = ArgumentValue.Unknown;
            return;
        }

        string defaultValue = value[(equalsIndex + 1)..].Trim();
        SubstitutionResult substitution = Substitute(defaultValue, arguments);
        arguments[name] = substitution.Resolved && substitution.Value.Length <= MaximumReferenceLength
            ? new ArgumentValue(true, substitution.Value)
            : ArgumentValue.Unknown;
    }

    private static bool IsSensitiveArgumentName(string name) =>
        name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PRIVATE_KEY", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("ACCESS_KEY", StringComparison.OrdinalIgnoreCase);

    private static FromParseResult ParseFrom(
        string value,
        IReadOnlyDictionary<string, ArgumentValue> arguments,
        HashSet<string> stages)
    {
        string[] tokens = WhitespaceRegex().Split(value.Trim())
            .Where(static token => token.Length > 0)
            .ToArray();
        int index = 0;
        bool malformed = false;

        while (index < tokens.Length && tokens[index].StartsWith("--", StringComparison.Ordinal))
        {
            if (!tokens[index].StartsWith("--platform=", StringComparison.OrdinalIgnoreCase) ||
                tokens[index].Length == "--platform=".Length)
            {
                malformed = true;
            }

            index++;
        }

        if (index >= tokens.Length)
        {
            return FromParseResult.Unresolved(null, null, malformed: true);
        }

        string expression = tokens[index++];
        string? stageAlias = null;
        if (index < tokens.Length)
        {
            if (index + 1 == tokens.Length - 1 &&
                tokens[index].Equals("AS", StringComparison.OrdinalIgnoreCase) &&
                StageNameRegex().IsMatch(tokens[index + 1]))
            {
                stageAlias = tokens[index + 1];
                index += 2;
            }
            else
            {
                malformed = true;
            }
        }

        if (index != tokens.Length || expression.Length > MaximumReferenceLength)
        {
            malformed = true;
        }

        SubstitutionResult substitution = Substitute(expression, arguments);
        if (!substitution.Resolved || malformed)
        {
            return FromParseResult.Unresolved(stageAlias, null, malformed);
        }

        string image = substitution.Value;
        if (image.Equals("scratch", StringComparison.OrdinalIgnoreCase))
        {
            return new FromParseResult(
                true,
                malformed,
                "scratch",
                null,
                null,
                null,
                null,
                stageAlias,
                DockerImageReferenceKind.Scratch,
                null,
                false);
        }

        if (stages.Contains(image))
        {
            return new FromParseResult(
                true,
                malformed,
                image,
                null,
                null,
                null,
                null,
                stageAlias,
                DockerImageReferenceKind.Stage,
                null,
                false);
        }

        if (!TryParseExternalImage(
                image,
                out string? registry,
                out string? repository,
                out string? tag,
                out string? digest))
        {
            return FromParseResult.Unresolved(stageAlias, null, malformed: true);
        }

        string? family = null;
        bool official = registry is not null &&
                        registry.Equals("mcr.microsoft.com", StringComparison.OrdinalIgnoreCase) &&
                        TryGetDotNetFamily(repository!, out family);

        return new FromParseResult(
            true,
            malformed,
            image,
            registry,
            repository,
            tag,
            digest,
            stageAlias,
            DockerImageReferenceKind.External,
            official ? family : null,
            official);
    }

    private static SubstitutionResult Substitute(
        string expression,
        IReadOnlyDictionary<string, ArgumentValue> arguments)
    {
        var result = new System.Text.StringBuilder();
        for (int index = 0; index < expression.Length; index++)
        {
            char character = expression[index];
            if (character != '$')
            {
                result.Append(character);
                continue;
            }

            if (index + 1 >= expression.Length)
            {
                return SubstitutionResult.Unresolved;
            }

            string name;
            if (expression[index + 1] == '{')
            {
                int end = expression.IndexOf('}', index + 2);
                if (end < 0)
                {
                    return SubstitutionResult.Unresolved;
                }

                name = expression[(index + 2)..end];
                index = end;
            }
            else
            {
                Match match = ArgumentAtStartRegex().Match(expression[(index + 1)..]);
                if (!match.Success)
                {
                    return SubstitutionResult.Unresolved;
                }

                name = match.Value;
                index += name.Length;
            }

            if (!ArgumentNameRegex().IsMatch(name) ||
                !arguments.TryGetValue(name, out ArgumentValue? argument) ||
                !argument.Resolved)
            {
                return SubstitutionResult.Unresolved;
            }

            result.Append(argument.Value);
            if (result.Length > MaximumReferenceLength)
            {
                return SubstitutionResult.Unresolved;
            }
        }

        return new SubstitutionResult(true, result.ToString());
    }

    private static bool TryParseExternalImage(
        string image,
        out string? registry,
        out string? repository,
        out string? tag,
        out string? digest)
    {
        registry = null;
        repository = null;
        tag = null;
        digest = null;

        if (image.Length is 0 or > MaximumReferenceLength || image.Any(char.IsWhiteSpace))
        {
            return false;
        }

        string nameAndTag = image;
        int atIndex = image.IndexOf('@');
        if (atIndex >= 0)
        {
            if (atIndex != image.LastIndexOf('@'))
            {
                return false;
            }

            digest = image[(atIndex + 1)..];
            if (!Sha256DigestRegex().IsMatch(digest))
            {
                return false;
            }

            nameAndTag = image[..atIndex];
        }

        int lastSlash = nameAndTag.LastIndexOf('/');
        int lastColon = nameAndTag.LastIndexOf(':');
        string name = nameAndTag;
        if (lastColon > lastSlash)
        {
            tag = nameAndTag[(lastColon + 1)..];
            if (!TagRegex().IsMatch(tag))
            {
                return false;
            }

            name = nameAndTag[..lastColon];
        }

        string[] parts = name.Split('/');
        if (parts.Any(static part => !RepositoryPartRegex().IsMatch(part)))
        {
            return false;
        }

        bool hasRegistry = parts.Length > 1 &&
                           (parts[0].Contains('.', StringComparison.Ordinal) ||
                            parts[0].Contains(':', StringComparison.Ordinal) ||
                            parts[0].Equals("localhost", StringComparison.OrdinalIgnoreCase));
        if (hasRegistry)
        {
            registry = parts[0];
            repository = string.Join('/', parts.Skip(1));
        }
        else
        {
            repository = name;
        }

        return registry is not { Length: > 256 } &&
               repository.Length is > 0 and <= MaximumReferenceLength;
    }

    private static bool TryGetDotNetFamily(string repository, out string? family)
    {
        family = repository switch
        {
            "dotnet/sdk" => DotNetContainerImageFamily.Sdk,
            "dotnet/aspnet" => DotNetContainerImageFamily.AspNet,
            "dotnet/runtime" => DotNetContainerImageFamily.Runtime,
            "dotnet/runtime-deps" => DotNetContainerImageFamily.RuntimeDeps,
            _ => null
        };
        return family is not null;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentNameRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentAtStartRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex StageNameRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("^sha256:[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256DigestRegex();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex("^[a-z0-9]+(?:[._-][a-z0-9]+)*(?::[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPartRegex();

    internal sealed record ParseResult(
        IReadOnlyList<DockerImageEvidence> Images,
        IReadOnlyList<ContainerDiscoveryDiagnostic> Diagnostics);

    private sealed record LogicalInstruction(string Keyword, string Value, int Line);

    private sealed record ArgumentValue(bool Resolved, string Value)
    {
        public static ArgumentValue Unknown { get; } = new(false, string.Empty);
    }

    private sealed record SubstitutionResult(bool Resolved, string Value)
    {
        public static SubstitutionResult Unresolved { get; } = new(false, string.Empty);
    }

    private sealed record FromParseResult(
        bool Resolved,
        bool Malformed,
        string? ImageReference,
        string? Registry,
        string? Repository,
        string? Tag,
        string? Digest,
        string? StageAlias,
        string ReferenceKind,
        string? DotNetFamily,
        bool IsOfficialDotNetImage)
    {
        public static FromParseResult Unresolved(
            string? stageAlias,
            string? imageReference,
            bool malformed) =>
            new(
                false,
                malformed,
                imageReference,
                null,
                null,
                null,
                null,
                stageAlias,
                DockerImageReferenceKind.External,
                null,
                false);
    }
}
