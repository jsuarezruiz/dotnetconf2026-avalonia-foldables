using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Inkfold.Painting;
using SkiaSharp;

namespace Inkfold;

/// <summary>
/// The drawing that's open and the brush in hand, shared by the canvas and every tool panel on screen.
/// </summary>
public sealed class Studio
{
    private const int MaxRecentColors = 8;

    private readonly DispatcherTimer _autosave;
    private readonly List<SKColor> _recentColors = [];
    private int _seed = Environment.TickCount;
    private bool _isPressing;

    // The latest save, finished or not: deleting a drawing and listing the gallery wait for it.
    private Task _lastSave = Task.CompletedTask;

    // The tool to go back to once the picker has picked a color.
    private ToolKind? _beforePicker;

    public Studio(Gallery gallery)
    {
        Gallery = gallery;
        Tools = ToolInfo.CreateAll();
        Tool = Tools[0];

        var latest = gallery.List().FirstOrDefault();
        Document = (latest is null ? null : TryLoad(latest.Id)) ?? Document.CreateBlank();
        History = new History(Document);
        History.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);

        _autosave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _autosave.Tick += (_, _) =>
        {
            _autosave.Stop();
            _ = SaveAsync(Document);
        };
    }

    /// <summary>
    /// Raised when the tool, color, size, opacity, symmetry, press or undo state changes.
    /// </summary>
    public event EventHandler? SettingsChanged;

    /// <summary>
    /// Raised when paint changes on any layer.
    /// </summary>
    public event EventHandler? PaintChanged;

    /// <summary>
    /// Raised when layers are added, removed, moved, shown, hidden or selected.
    /// </summary>
    public event EventHandler? LayersChanged;

    /// <summary>
    /// Raised when another drawing opens.
    /// </summary>
    public event EventHandler? DocumentOpened;

    /// <summary>
    /// Raised after a press, with each pressed layer and its paint from before, for the animation.
    /// </summary>
    public event EventHandler<PressedEventArgs>? Pressed;

    /// <summary>
    /// Raised when the brush's size or opacity is changed by hand, so its new dab can be shown.
    /// </summary>
    public event EventHandler? BrushAdjusted;

    /// <summary>
    /// Raised with a short message for the person drawing.
    /// </summary>
    public event EventHandler<string>? Notice;

    public Gallery Gallery { get; }

    public ToolInfo[] Tools { get; }

    public ToolInfo Tool { get; private set; }

    public SKColor Color { get; private set; } = new(0x1D, 0x1B, 0x2A);

    public IReadOnlyList<SKColor> RecentColors => _recentColors;

    public bool Symmetry { get; private set; }

    /// <summary>
    /// Gets whether closing and opening the device will press the page.
    /// </summary>
    public bool IsPressArmed { get; private set; }

    /// <summary>
    /// Gets whether the press is printing. The page takes no paint meanwhile: a thumb landing on the screen
    /// as the device opens would change the page under the print, and the print would be left out.
    /// </summary>
    public bool IsPressing => _isPressing;

    public Document Document { get; private set; }

    public History History { get; }

    public void SelectTool(ToolKind kind)
    {
        if (kind == ToolKind.Picker && Tool.Kind != ToolKind.Picker)
            _beforePicker = Tool.Kind;
        Tool = Tools.First(tool => tool.Kind == kind);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetColor(SKColor color)
    {
        Color = color.WithAlpha(255);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Goes back to the tool in hand before the picker, as a picked color is meant for painting.
    /// </summary>
    public void FinishPick()
    {
        if (Tool.Kind == ToolKind.Picker && _beforePicker is { } previous)
            SelectTool(previous);
    }

    public void SetSize(double size)
    {
        Tool.Size = Tool.Clamp(size);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        BrushAdjusted?.Invoke(this, EventArgs.Empty);
    }

    public void SetOpacity(double opacity)
    {
        Tool.Opacity = Math.Clamp(opacity, 0.05, 1);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        BrushAdjusted?.Invoke(this, EventArgs.Empty);
    }

    public void SetSymmetry(bool on)
    {
        Symmetry = on;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ArmPress(bool armed)
    {
        IsPressArmed = armed;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts a stroke on the active layer, or returns null when the tool doesn't paint strokes or the
    /// layer is hidden.
    /// </summary>
    public StrokeBuilder? BeginStroke()
    {
        if (!Tool.IsBrush || !CanPaint())
            return null;

        var style = new StrokeStyle(
            Tool.Kind, Color, (float)Tool.Size, (float)Tool.Opacity, Symmetry ? Document.Width / 2f : null, _seed++);
        return new StrokeBuilder(Tool, style, Document.Active);
    }

    /// <summary>
    /// Paints a finished stroke into its layer, as one step of history.
    /// </summary>
    public void Commit(StrokeBuilder stroke)
    {
        // A stroke that started just before the press and ends during it, like a thumb on the screen as
        // the device opens, would change the page under the print: it's dropped instead.
        if (_isPressing)
            return;

        var live = stroke.Snapshot();
        var layer = live.Target;
        if (live.Samples.Length == 0 || layer.IsDisposed || !Document.Layers.Contains(layer))
            return;

        var area = Pixels.Clip(live.Bounds, Document.Bounds);
        if (area.IsEmpty)
            return;

        lock (Document.Sync)
        {
            var before = Pixels.Copy(layer.Pixels, area);
            using (var canvas = new SKCanvas(layer.Pixels))
                StrokeRenderer.Draw(canvas, live);
            layer.Refresh();
            History.Push(new PixelsStep(layer, area, before, Pixels.Copy(layer.Pixels, area)));
        }

        if (live.Style.Tool != ToolKind.Eraser)
            Remember(live.Style.Color);
        Painted();
    }

    /// <summary>
    /// Fills the area under a point of the page with the color.
    /// </summary>
    public void FillAt(SKPoint point)
    {
        if (_isPressing || !CanPaint())
            return;

        var layer = Document.Active;
        using var reference = DocumentRenderer.RenderComposite(Document, false);
        lock (Document.Sync)
        {
            using var whole = Pixels.Copy(layer.Pixels, Document.Bounds);
            if (Fill.Apply(reference, layer.Pixels, (int)point.X, (int)point.Y, Color, Tool.Opacity) is not { } area)
                return;

            layer.Refresh();
            History.Push(new PixelsStep(layer, area, Pixels.Copy(whole, area), Pixels.Copy(layer.Pixels, area)));
        }

        Remember(Color);
        Painted();
    }

    /// <summary>
    /// Presses the paint of every visible layer onto the facing half of the page, as folding wet paper
    /// does, across the line the device folds along.
    /// </summary>
    /// <remarks>
    /// The press runs its filters on copies of the layers on another thread, so the screen keeps moving
    /// while the device opens; the prints go in once they're ready. Nothing it does throws: callers can
    /// discard the task.
    /// </remarks>
    public async Task PressAsync(PressFold fold)
    {
        if (_isPressing)
        {
            Notice?.Invoke(this, "Still pressing the last one.");
            return;
        }

        IsPressArmed = false;
        _isPressing = true;
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        var document = Document;
        var jobs = new List<(Layer Layer, int Version, SKBitmap Before, SKBitmap After, int Seed)>();
        string? notice = null;

        // The copies are the history's once it has them; until then they're disposed here, whatever happens.
        var handedOver = false;
        try
        {
            // Copies of the visible layers as they are now: the press works on these, off the lock.
            lock (document.Sync)
            {
                foreach (var layer in document.Layers)
                {
                    if (layer.IsVisible)
                        jobs.Add((layer, layer.Version, Pixels.Copy(layer.Pixels, document.Bounds), Pixels.Copy(layer.Pixels, document.Bounds), _seed++));
                }
            }

            if (jobs.Count == 0)
            {
                notice = "Every layer is hidden: there's no paint to press.";
                return;
            }

            await Task.Run(() =>
            {
                foreach (var job in jobs)
                    InkPress.Apply(job.After, job.Seed, fold);
            });

            // Back on the UI thread. Paint, layers or visibility that changed meanwhile would be lost or
            // missed under the print, so the press only goes in on the page it was made from.
            List<PixelsStep>? steps = null;
            lock (document.Sync)
            {
                var unchanged = Document == document && !document.IsDisposed
                    && jobs.TrueForAll(job => document.Layers.Contains(job.Layer) && job.Layer.IsVisible && job.Layer.Version == job.Version)
                    && document.Layers.Count(layer => layer.IsVisible) == jobs.Count;
                if (unchanged)
                    steps = PasteAll(jobs, document.Bounds);
            }

            if (steps is null)
            {
                notice = "The page changed while it was being pressed, so the press was left out.";
                return;
            }

            History.Push(new StepGroup(steps));
            handedOver = true;
            Painted();
            Pressed?.Invoke(this, new PressedEventArgs(steps, fold));
        }
        catch (Exception)
        {
            // Once the history has the press, it happened: a failing listener doesn't make it fail.
            if (!handedOver)
                notice = "The press couldn't be made.";
        }
        finally
        {
            if (!handedOver)
            {
                foreach (var job in jobs)
                {
                    job.Before.Dispose();
                    job.After.Dispose();
                }
            }

            _isPressing = false;
            SettingsChanged?.Invoke(this, EventArgs.Empty);

            // Here rather than after the try: the paths that leave early return from inside it.
            if (notice is not null)
                Notice?.Invoke(this, notice);
        }
    }

    // Prints every layer or none: one that fails puts back the layers already printed.
    private static List<PixelsStep> PasteAll(List<(Layer Layer, int Version, SKBitmap Before, SKBitmap After, int Seed)> jobs, SKRectI bounds)
    {
        var done = 0;
        try
        {
            foreach (var job in jobs)
            {
                Pixels.Paste(job.After, job.Layer.Pixels, 0, 0);
                job.Layer.Refresh();
                done++;
            }
        }
        catch
        {
            for (var i = 0; i < done; i++)
            {
                Pixels.Paste(jobs[i].Before, jobs[i].Layer.Pixels, 0, 0);
                jobs[i].Layer.Refresh();
            }

            throw;
        }

        return jobs.ConvertAll(job => new PixelsStep(job.Layer, bounds, job.Before, job.After));
    }

    public void ClearLayer()
    {
        // While the press prints, the layers stay as it copied them: a change would only drop the print.
        if (_isPressing)
            return;

        var layer = Document.Active;
        lock (Document.Sync)
        {
            var before = Pixels.Copy(layer.Pixels, Document.Bounds);
            layer.Pixels.Erase(SKColors.Transparent);
            layer.Refresh();
            History.Push(new PixelsStep(layer, Document.Bounds, before, Pixels.Copy(layer.Pixels, Document.Bounds)));
        }

        Painted();
    }

    public void Undo()
    {
        if (!_isPressing && History.Undo())
            Restored();
    }

    public void Redo()
    {
        if (!_isPressing && History.Redo())
            Restored();
    }

    // Each layer keeps the page's pixels twice, about 15 MB at the default size, on top of the undo
    // history: a phone runs out of memory long before a person runs out of layers.
    public const int MaxLayers = 8;

    public void AddLayer()
    {
        if (_isPressing || !HasRoomForLayer())
            return;

        var layer = new Layer(Document.NextLayerName(), Document.Width, Document.Height);
        var index = Document.ActiveIndex + 1;
        var step = new LayerStep(layer, index, removed: false);
        step.Redo(Document);
        History.Push(step);
        LayersEdited();
    }

    public void DuplicateLayer()
    {
        if (_isPressing || !HasRoomForLayer())
            return;

        Layer copy;
        lock (Document.Sync)
            copy = Document.Active.Clone($"{Document.Active.Name} copy");

        var step = new LayerStep(copy, Document.ActiveIndex + 1, removed: false);
        step.Redo(Document);
        History.Push(step);
        LayersEdited();
    }

    private bool HasRoomForLayer()
    {
        if (Document.Layers.Count < MaxLayers)
            return true;

        Notice?.Invoke(this, $"A drawing holds up to {MaxLayers} layers.");
        return false;
    }

    public void DeleteLayer()
    {
        if (_isPressing)
            return;

        if (Document.Layers.Count <= 1)
        {
            ClearLayer();
            return;
        }

        var step = new LayerStep(Document.Active, Document.ActiveIndex, removed: true);
        step.Redo(Document);
        History.Push(step);
        LayersEdited();
    }

    /// <summary>
    /// Moves the active layer up (positive) or down the stack.
    /// </summary>
    public void MoveLayer(int by)
    {
        var from = Document.ActiveIndex;
        var to = Math.Clamp(from + by, 0, Document.Layers.Count - 1);
        if (to == from)
            return;

        LayerMoveStep.Move(Document, from, to);
        History.Push(new LayerMoveStep(from, to));
        LayersEdited();
    }

    public void SelectLayer(int index)
    {
        lock (Document.Sync)
            Document.ActiveIndex = Math.Clamp(index, 0, Document.Layers.Count - 1);
        LayersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLayerVisible(int index, bool visible)
    {
        if (_isPressing)
            return;

        lock (Document.Sync)
            Document.Layers[index].IsVisible = visible;
        LayersEdited();
    }

    public void SetLayerOpacity(int index, double opacity)
    {
        lock (Document.Sync)
            Document.Layers[index].Opacity = Math.Clamp(opacity, 0, 1);
        LayersEdited();
    }

    public void NewDrawing() => Open(Document.CreateBlank());

    public void OpenDrawing(string id)
    {
        if (id == Document.Id)
            return;

        if (TryLoad(id) is { } document)
            Open(document);
        else
            Notice?.Invoke(this, "That drawing couldn't be opened.");
    }

    public async Task DeleteDrawingAsync(string id)
    {
        // A save still writing the drawing finishes first, or it could bring the folder back.
        var isOpen = id == Document.Id;
        var pending = isOpen && _autosave.IsEnabled;
        if (isOpen)
            _autosave.Stop();
        await _lastSave;

        try
        {
            Gallery.Delete(id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (pending)
                _autosave.Start();
            Notice?.Invoke(this, "That drawing couldn't be deleted.");
            return;
        }

        if (id == Document.Id)
        {
            _autosave.Stop();
            var next = Gallery.List().FirstOrDefault();
            Open((next is null ? null : TryLoad(next.Id)) ?? Document.CreateBlank(), save: false);
        }
    }

    /// <summary>
    /// Saves changes now rather than after the next pause, as when the app goes to the background or
    /// the gallery opens. A drawing without changes isn't written, so its date stays; a save already
    /// under way is finished.
    /// </summary>
    public Task SaveNow()
    {
        if (!_autosave.IsEnabled)
            return _lastSave;

        _autosave.Stop();
        return SaveAsync(Document);
    }

    /// <summary>
    /// Saves before the app quits, waiting a few seconds at most.
    /// </summary>
    public void SaveBeforeExit()
    {
        _autosave.Stop();
        try
        {
            Task.Run(() => Gallery.SaveAsync(Document)).Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The app is closing, so there's no one left to tell; the last autosave stays on disk.
        }
    }

    private Task SaveAsync(Document document) => _lastSave = SaveOrTellAsync(document);

    // Saves without throwing: a full disk or a missing folder shows a notice instead of losing the error.
    // Autosaves aren't awaited, so anything thrown here would otherwise vanish, or end the app where awaited.
    private async Task SaveOrTellAsync(Document document)
    {
        try
        {
            await Gallery.SaveAsync(document);
        }
        catch (Exception)
        {
            Notice?.Invoke(this, "The drawing couldn't be saved.");
        }
    }

    private void Open(Document document, bool save = true)
    {
        var old = Document;
        var saved = save && _autosave.IsEnabled ? SaveAsync(old) : Task.CompletedTask;
        _autosave.Stop();

        Document = document;
        History.Reset(document);
        IsPressArmed = false;

        // The old drawing goes once its last changes are on disk.
        saved.ContinueWith(_ => Dispatcher.UIThread.Post(old.Dispose), TaskScheduler.Default);

        DocumentOpened?.Invoke(this, EventArgs.Empty);
        LayersChanged?.Invoke(this, EventArgs.Empty);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        PaintChanged?.Invoke(this, EventArgs.Empty);
        if (save)
            ScheduleSave();
    }

    private Document? TryLoad(string id)
    {
        try
        {
            return Gallery.Load(id);
        }
        catch (Exception exception) when (exception is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool CanPaint()
    {
        if (Document.Active.IsVisible)
            return true;

        Notice?.Invoke(this, $"{Document.Active.Name} is hidden. Show it to paint on it.");
        return false;
    }

    private void Remember(SKColor color)
    {
        _recentColors.Remove(color);
        _recentColors.Insert(0, color);
        if (_recentColors.Count > MaxRecentColors)
            _recentColors.RemoveAt(_recentColors.Count - 1);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Painted()
    {
        PaintChanged?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
    }

    // Undo and redo can touch pixels or layers.
    private void Restored()
    {
        LayersChanged?.Invoke(this, EventArgs.Empty);
        Painted();
    }

    private void LayersEdited()
    {
        LayersChanged?.Invoke(this, EventArgs.Empty);
        Painted();
    }

    private void ScheduleSave()
    {
        lock (Document.Sync)
            Document.Modified = DateTime.Now;
        _autosave.Stop();
        _autosave.Start();
    }
}

/// <summary>
/// The layers a press changed, with their paint from before it, and the line it folded along.
/// </summary>
public sealed record PressedEventArgs(IReadOnlyList<PixelsStep> Layers, PressFold Fold);
