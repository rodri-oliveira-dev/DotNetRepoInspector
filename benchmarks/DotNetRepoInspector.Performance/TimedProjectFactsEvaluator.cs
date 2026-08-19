using System.Diagnostics;

using DotNetRepoInspector.MSBuild.Evaluation;

namespace DotNetRepoInspector.Performance;

internal sealed class TimedProjectFactsEvaluator : IMsBuildProjectFactsEvaluator
{
    private readonly IMsBuildProjectFactsEvaluator _inner;
    private TimeSpan _elapsed;
    private int _evaluationCount;

    public TimedProjectFactsEvaluator(IMsBuildProjectFactsEvaluator inner)
    {
        _inner = inner;
    }

    public TimeSpan Elapsed => _elapsed;

    public int EvaluationCount => _evaluationCount;

    public async Task<MsBuildProjectFactsResult> EvaluateAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        _evaluationCount++;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await _inner.EvaluateAsync(projectPath, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            _elapsed += stopwatch.Elapsed;
        }
    }
}
