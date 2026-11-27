using System;
using System.Collections.Generic;
using System.Threading;
using CtrlLookup.Core;
using CtrlLookup.Dictionary;

namespace CtrlLookup;

internal sealed record EnrichmentResult(string Text, IReadOnlyList<TextHighlight>? Highlights = null);

/// <summary>Best-effort lookup and writing assistance, with at most one queued selection.</summary>
internal sealed class SelectionEnrichment : IDisposable
{
    private readonly object _gate = new();
    private readonly OfflineDictionaries _dictionaries = new(OfflineDictionary.DefaultPath, OfflineDictionary.ChinesePath);
    private readonly LlmClient? _client = LlmClient.TryCreate();
    private readonly LookupHistory _history = new(LookupHistory.DefaultPath);
    private readonly Action<Probe, EnrichmentResult> _completed;
    private (Probe Probe, string Text)? _pending;
    private Probe? _current;
    private CancellationTokenSource? _call;
    private bool _stopped;

    public SelectionEnrichment(Action<Probe, EnrichmentResult> completed)
    {
        _completed = completed;
        new Thread(Loop) { IsBackground = true, Name = "CtrlLookup.Enrichment" }.Start();
    }

    public void Submit(Probe probe, string text)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _current = probe;
            _pending = (probe, text);
            _call?.Cancel();
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
                (Probe Probe, string Text) job;
                CancellationTokenSource? call;
                lock (_gate)
                {
                    while (!_stopped && _pending is null) Monitor.Wait(_gate);
                    if (_stopped) return;
                    job = _pending!.Value;
                    _pending = null;
                    // Register cancellation before releasing the lock, so Clear can cancel a just-dequeued job.
                    call = _call = _client is null ? null : new CancellationTokenSource();
                }

                try
                {
                    var result = Enrich(job.Probe.Mode, job.Text, call?.Token ?? CancellationToken.None);
                    if (result is null) continue;
                    lock (_gate)
                    {
                        if (_stopped || _current != job.Probe || call?.IsCancellationRequested == true) continue;
                    }
                    // The UI also checks the current generation, activation mode and visibility at delivery time.
                    _completed(job.Probe, result);
                }
                catch (Exception) { } // A failed lookup or request must not stop the worker.
                finally
                {
                    lock (_gate) _call = null;
                    call?.Dispose();
                }
            }
        }
        finally { _client?.Dispose(); }
    }

    private EnrichmentResult? Enrich(SelectionMode mode, string text, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return null;
        if (mode == SelectionMode.Writing)
        {
            if (_client is null || WritingQuery.FromSelection(text) is not { } request ||
                _client.Assist(request, cancellation) is not { } reply) return null;
            return new(reply, request.Task == WritingTask.Polish ? PolishDiff.FindHighlights(request.Text, reply) : null);
        }

        if (mode != SelectionMode.Lookup || DictionaryQuery.FromSelection(text) is not { } query) return null;
        var gloss = DictionaryTranslation.Lookup(_dictionaries, _client, query, cancellation);
        _history.Record(query, gloss);
        return gloss is null ? null : new(gloss);
    }
}
