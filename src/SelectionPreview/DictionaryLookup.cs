using System;
using System.Threading;
using SelectionPreview.Core;
using SelectionPreview.Dictionary;

namespace SelectionPreview;

/// <summary>Best-effort lookup on its own worker, with at most one queued word.</summary>
internal sealed class DictionaryLookup : IDisposable
{
    private readonly object _gate = new();
    private readonly OfflineDictionary _dictionary = new(OfflineDictionary.DefaultPath);
    private readonly Action<Probe, string> _completed;
    private (Probe Probe, string Word)? _pending;
    private Probe? _current;
    private bool _stopped;

    public DictionaryLookup(Action<Probe, string> completed)
    {
        _completed = completed;
        new Thread(Loop) { IsBackground = true, Name = "SelectionPreview.Dictionary" }.Start();
    }

    public void Submit(Probe probe, string word)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _current = probe;
            _pending = (probe, word);
            Monitor.Pulse(_gate);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending = null;
            _current = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            _pending = null;
            _current = null;
            Monitor.Pulse(_gate);
        }
    }

    private void Loop()
    {
        while (true)
        {
            (Probe Probe, string Word) job;
            lock (_gate)
            {
                while (!_stopped && _pending is null) Monitor.Wait(_gate);
                if (_stopped) return;
                job = _pending!.Value;
                _pending = null;
            }

            if (_dictionary.Lookup(job.Word) is not { } gloss) continue;
            lock (_gate)
            {
                if (_stopped || _current != job.Probe) continue;
            }
            // The UI also checks the current generation, Ctrl and visibility at delivery time.
            _completed(job.Probe, gloss);
        }
    }
}
