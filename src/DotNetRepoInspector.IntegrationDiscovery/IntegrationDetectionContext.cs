using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class IntegrationDetectionContext
{
    internal IntegrationDetectionContext(
        string projectPath,
        string sourcePath,
        SyntaxTree syntaxTree,
        CompilationUnitSyntax root,
        ISafeIntegrationEvidence evidence,
        IIntegrationFindingCollector findings)
    {
        ProjectPath = projectPath;
        SourcePath = sourcePath;
        SyntaxTree = syntaxTree;
        Root = root;
        Evidence = evidence;
        Findings = findings;
    }

    public string ProjectPath
    {
        get;
    }

    public string SourcePath
    {
        get;
    }

    public SyntaxTree SyntaxTree
    {
        get;
    }

    public CompilationUnitSyntax Root
    {
        get;
    }

    public ISafeIntegrationEvidence Evidence
    {
        get;
    }

    public IIntegrationFindingCollector Findings
    {
        get;
    }

    public int GetOneBasedLine(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
    }
}
