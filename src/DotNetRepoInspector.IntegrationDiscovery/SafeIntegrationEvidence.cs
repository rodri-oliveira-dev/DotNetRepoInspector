using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public interface ISafeIntegrationEvidence
{
    bool TryGetStringLiteral(ExpressionSyntax expression, out string value);

    bool TryGetConfigurationKey(ExpressionSyntax expression, out string key);
}

internal sealed class SafeIntegrationEvidence : ISafeIntegrationEvidence
{
    private const int MaximumEvidenceLength = 256;

    public bool TryGetStringLiteral(ExpressionSyntax expression, out string value)
    {
        ArgumentNullException.ThrowIfNull(expression);

        value = string.Empty;
        if (expression is not LiteralExpressionSyntax literal ||
            literal.RawKind != (int)SyntaxKind.StringLiteralExpression ||
            !IsSafeLiteral(literal.Token.ValueText))
        {
            return false;
        }

        value = literal.Token.ValueText;
        return true;
    }

    public bool TryGetConfigurationKey(ExpressionSyntax expression, out string key)
    {
        key = string.Empty;
        if (!TryGetStringLiteral(expression, out var value) ||
            !value.All(static character =>
                char.IsLetterOrDigit(character) ||
                character is ':' or '.' or '_' or '-' or '[' or ']'))
        {
            return false;
        }

        key = value;
        return true;
    }

    private static bool IsSafeLiteral(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumEvidenceLength &&
        !value.Contains('\r') &&
        !value.Contains('\n') &&
        !value.Contains('\0') &&
        !value.Contains(';') &&
        !value.Contains('=') &&
        !value.Contains('?');
}
