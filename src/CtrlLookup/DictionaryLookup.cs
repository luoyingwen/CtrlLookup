using System;
using System.Threading;
using CtrlLookup.Core;
using CtrlLookup.Dictionary;

namespace CtrlLookup;

/// <summary>Best-effort lookup on its own worker, with at most one queued query.</summary>
internal sealed class DictionaryLookup : IDisposable
{
    private readonly object _gate = new();
    private readonly OfflineDictionaries _dictionaries = new(OfflineDictionary.DefaultPath, OfflineDictionary.ChinesePath);
    private readonly LlmTranslator? _translator = LlmTranslator.TryCreate();
    private readonly LookupHistory _history = new(LookupHistory.DefaultPath);
    private readonly Action<Probe, string> _completed;
    private (Probe Probe, DictionaryQuery Query)? _pending;
    private Probe? _current;
    private CancellationTokenSource? _call;
    private bool _stopped;

    public DictionaryLookup(Action<Probe, string> completed)
    {
        _completed = completed;
        new Thread(Loop) { IsBackground = true, Name = "CtrlLookup.Dictionary" }.Start();
    }

    public void Submit(Probe probe, DictionaryQuery query)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _current = probe;
            _pending = (probe, query);
            Monitor.Pulse(_gate);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending = null;
            _current = null;
            _call?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            _pending = null;
            _current = null;
            _call?.Cancel();
            Monitor.Pulse(_gate);
        }
    }

    private void Loop()
    {
        try
        {
            while (true)
            {
                (Probe Probe, DictionaryQuery Query) job;
                lock (_gate)
                {
                    while (!_stopped && _pending is null) Monitor.Wait(_gate);
                    if (_stopped) return;
                    job = _pending!.Value;
                    _pending = null;
                }

                string? gloss;
                try { gloss = Translate(job); }
                catch (Exception) { gloss = null; }
                _history.Record(job.Query, gloss);
                if (gloss is null) continue;
                lock (_gate)
                {
                    if (_stopped || _current != job.Probe) continue;
                }
                // The UI also checks the current generation, Ctrl and visibility at delivery time.
                _completed(job.Probe, gloss);
            }
        }
        finally { _translator?.Dispose(); }
    }

    private string? Translate((Probe Probe, DictionaryQuery Query) job)
    {
        CancellationTokenSource? call = null;
        lock (_gate)
        {
            if (_stopped) return null;
            if (_translator is not null) call = _call = new CancellationTokenSource();
        }
        try
        {
            return DictionaryTranslation.Lookup(_dictionaries, _translator, job.Query, call?.Token ?? CancellationToken.None);
        }
        finally
        {
            lock (_gate)
            {
                if (call is not null && ReferenceEquals(_call, call)) _call = null;
            }
            call?.Dispose();
        }
    }
}
