using FileScope.Core.Models;

namespace FileScope.App.Presentation;

public sealed record CompletedAnalysis(int Index, FileAnalysisResult Result);
public sealed record CurrentAnalysis(int Index, AnalysisProgress Progress);
public sealed record AnalysisDisplayUpdate(IReadOnlyList<CompletedAnalysis> Completed, CurrentAnalysis? Current);

/// <summary>A batch-local mailbox: retain results, replace transient progress instead of queuing UI callbacks.</summary>
public sealed class AnalysisProgressBuffer
{
    private readonly Lock _gate = new();
    private readonly List<CompletedAnalysis> _completed = [];
    private CurrentAnalysis? _current;
    private bool _closed;

    public IProgress<AnalysisProgress> Start(int index)
    {
        lock (_gate)
        {
            if (!_closed) _current = new CurrentAnalysis(index, new AnalysisProgress(AnalysisStage.OpeningFile, 0, 0));
            return new Reporter(this, index);
        }
    }

    public void Complete(int index, FileAnalysisResult result)
    {
        lock (_gate)
        {
            if (_closed) return;
            _completed.Add(new CompletedAnalysis(index, result));
            if (_current?.Index == index) _current = null;
        }
    }

    public AnalysisDisplayUpdate Drain()
    {
        lock (_gate)
        {
            var update = new AnalysisDisplayUpdate(_completed.ToArray(), _current);
            _completed.Clear();
            return update;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _current = null;
            _completed.Clear();
        }
    }

    private void Report(int index, AnalysisProgress progress)
    {
        lock (_gate)
        {
            // A retained reporter from an earlier file/batch cannot replace the current status.
            if (!_closed && _current?.Index == index) _current = new CurrentAnalysis(index, progress);
        }
    }

    private sealed class Reporter(AnalysisProgressBuffer owner, int index) : IProgress<AnalysisProgress>
    {
        public void Report(AnalysisProgress value) => owner.Report(index, value);
    }
}
