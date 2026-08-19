using System.Diagnostics;

using DotNetRepoInspector.MSBuild.Discovery;

namespace DotNetRepoInspector.Performance;

internal sealed class TimedProjectDiscoverer : IProjectDiscoverer
{
    private readonly IProjectDiscoverer _inner;
    private TimeSpan _elapsed;

    public TimedProjectDiscoverer(IProjectDiscoverer inner)
    {
        _inner = inner;
    }

    public TimeSpan Elapsed => _elapsed;

    public IReadOnlyList<DiscoveredProject> Discover(ProjectDiscoveryRequest request) =>
        Measure(() => _inner.Discover(request));

    public IReadOnlyList<DiscoveredProject> Discover(
        ProjectDiscoveryRequest request,
        CancellationToken cancellationToken) =>
        Measure(() => _inner.Discover(request, cancellationToken));

    private IReadOnlyList<DiscoveredProject> Measure(
        Func<IReadOnlyList<DiscoveredProject>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            stopwatch.Stop();
            _elapsed += stopwatch.Elapsed;
        }
    }
}
