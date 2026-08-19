using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.MSBuild.References;

public sealed record ProjectReferenceGraph(
    IReadOnlyList<ProjectReferenceGraphNode> Projects);

public sealed record ProjectReferenceGraphNode(
    string ProjectPath,
    IReadOnlyList<ProjectReferenceMetadata> References,
    IReadOnlyList<InspectionDiagnostic> Diagnostics);
