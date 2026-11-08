using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Inkfold.Painting;

/// <summary>
/// A change that can be undone and redone.
/// </summary>
public interface IHistoryStep
{
    /// <summary>
    /// Gets the memory the step holds, to keep the history within a budget.
    /// </summary>
    long Bytes { get; }

    void Undo(Document document);

    void Redo(Document document);

    /// <summary>
    /// Frees what only this step holds, once it leaves the history.
    /// </summary>
    void Discard(Document document);
}

/// <summary>
/// A change to a rectangle of one layer's pixels: a stroke, a fill, a clear or a press.
/// </summary>
public sealed class PixelsStep(Layer layer, SKRectI area, SKBitmap before, SKBitmap after) : IHistoryStep
{
    public Layer Layer => layer;

    public SKBitmap Before => before;

    public long Bytes => (long)area.Width * area.Height * 8;

    public void Undo(Document document) => Put(document, before);

    public void Redo(Document document) => Put(document, after);

    public void Discard(Document document)
    {
        before.Dispose();
        after.Dispose();
    }

    private void Put(Document document, SKBitmap pixels)
    {
        lock (document.Sync)
        {
            Pixels.Paste(pixels, layer.Pixels, area.Left, area.Top);
            layer.Refresh();
        }
    }
}

/// <summary>
/// A layer added (or, with <c>removed</c>, deleted) at an index.
/// </summary>
public sealed class LayerStep(Layer layer, int index, bool removed) : IHistoryStep
{
    // Whether the step is done (it's on the undo stack) rather than undone (on the redo stack).
    private bool _applied;

    public long Bytes => removed ? (long)layer.Pixels.ByteCount : 0;

    public void Undo(Document document)
    {
        _applied = false;
        if (removed)
            Insert(document);
        else
            Remove(document);
    }

    public void Redo(Document document)
    {
        _applied = true;
        if (removed)
            Remove(document);
        else
            Insert(document);
    }

    // The layer goes only when no step left can bring it back: a delete that can't be undone any more, or
    // an add that was undone and can't be redone. An add leaving the history while a later delete of the
    // same layer is still in it must keep the layer for that delete's undo.
    public void Discard(Document document)
    {
        if (removed == _applied && !document.Layers.Contains(layer))
            layer.Dispose();
    }

    private void Insert(Document document)
    {
        lock (document.Sync)
        {
            document.Layers.Insert(index, layer);
            document.ActiveIndex = index;

            // Its file was deleted if a save ran while the layer was out of the document, so write it again.
            layer.SavedVersion = -1;
        }
    }

    private void Remove(Document document)
    {
        lock (document.Sync)
        {
            document.Layers.Remove(layer);
            document.ActiveIndex = Math.Clamp(index - 1, 0, document.Layers.Count - 1);
        }
    }
}

/// <summary>
/// A layer moved up or down the stack.
/// </summary>
public sealed class LayerMoveStep(int from, int to) : IHistoryStep
{
    public long Bytes => 0;

    public void Undo(Document document) => Move(document, to, from);

    public void Redo(Document document) => Move(document, from, to);

    public void Discard(Document document)
    {
    }

    public static void Move(Document document, int from, int to)
    {
        lock (document.Sync)
        {
            var layer = document.Layers[from];
            document.Layers.RemoveAt(from);
            document.Layers.Insert(to, layer);
            document.ActiveIndex = to;
        }
    }
}

/// <summary>
/// Several steps taken as one, such as a press of every visible layer.
/// </summary>
public sealed class StepGroup(IReadOnlyList<IHistoryStep> steps) : IHistoryStep
{
    public long Bytes
    {
        get
        {
            var bytes = 0L;
            foreach (var step in steps)
                bytes += step.Bytes;
            return bytes;
        }
    }

    public void Undo(Document document)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
            steps[i].Undo(document);
    }

    public void Redo(Document document)
    {
        foreach (var step in steps)
            step.Redo(document);
    }

    public void Discard(Document document)
    {
        foreach (var step in steps)
            step.Discard(document);
    }
}

/// <summary>
/// The undo and redo stacks of a document, kept within a memory budget.
/// </summary>
public sealed class History
{
    // A press keeps a before and an after copy of each layer, about 15 MB at the default page size.
    private const long Budget = 128L * 1024 * 1024;
    private const int MaxSteps = 100;

    private readonly List<IHistoryStep> _undo = [];
    private readonly List<IHistoryStep> _redo = [];
    private Document _document;

    public History(Document document) => _document = document;

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Push(IHistoryStep step)
    {
        foreach (var undone in _redo)
            undone.Discard(_document);
        _redo.Clear();

        _undo.Add(step);
        var bytes = 0L;
        foreach (var kept in _undo)
            bytes += kept.Bytes;

        while (_undo.Count > 1 && (_undo.Count > MaxSteps || bytes > Budget))
        {
            bytes -= _undo[0].Bytes;
            _undo[0].Discard(_document);
            _undo.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        step.Undo(_document);
        _redo.Add(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        step.Redo(_document);
        _undo.Add(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Forgets every step, as when another drawing opens.
    /// </summary>
    public void Reset(Document document)
    {
        foreach (var step in _undo)
            step.Discard(_document);
        foreach (var step in _redo)
            step.Discard(_document);
        _undo.Clear();
        _redo.Clear();
        _document = document;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
