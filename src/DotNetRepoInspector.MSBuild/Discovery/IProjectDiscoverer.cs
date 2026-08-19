namespace DotNetRepoInspector.MSBuild.Discovery;

public interface IProjectDiscoverer
{
    IReadOnlyList<DiscoveredProject> Discover(ProjectDiscoveryRequest request);

    IReadOnlyList<DiscoveredProject> Discover(
        ProjectDiscoveryRequest request,
        CancellationToken cancellationToken);
}
