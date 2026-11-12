using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Inkfold.Controls;
using Inkfold.Painting;
using Nova.Avalonia.Foldable;
using SkiaSharp;

namespace Inkfold.Views;

/// <summary>
/// The studio: the page, and the tools arranged around it for the way the device is held.
/// </summary>
public partial class MainView : UserControl
{
    // The rails' thickness: a 44-point button, its padding and the border.
    private const double RailThickness = 56;
    private const double Gap = 10;
    private const double FloatingPanelWidth = 300;

    // Half the widest fold band, the iPhone Duo's 40 points, and a little more.
    private const double FoldAllowance = 24;

    private readonly Studio _studio;
    private readonly PressTrigger _press = new();
    private readonly DispatcherTimer _toastTimer;
    private TopLevel? _topLevel;
    private StageLayout _layout;
    private bool _panelOnLeft;

    // The fold as the layout last saw it, without the angle: folding moves the angle many times a frame,
    // and only the posture and the fold's place change the layout.
    private FoldState? _layoutFold;

    // The line the armed press folds along: the device's fold when it was armed, down the page or
    // across it. It stays as armed: the screen may turn while the device closes and opens.
    private PressFold _pressFold;

    public MainView()
    {
        InitializeComponent();

        _studio = new Studio(new Gallery(Gallery.DefaultRoot));
        Canvas.Studio = _studio;
        Tools.Studio = _studio;
        Actions.Studio = _studio;
        DockActions.Studio = _studio;
        DockActions.ShowsLayers = false;
        FloatingPanel.Studio = _studio;
        DockedPanel.Studio = _studio;
        GalleryLayer.Studio = _studio;

        Tools.PanelRequested += (_, section) => TogglePanel(section, onLeft: true);
        FloatingPanel.CloseRequested += (_, _) => ClosePanel();
        foreach (var actions in new[] { Actions, DockActions })
        {
            actions.LayersRequested += (_, _) => TogglePanel(PanelSection.Layers, onLeft: false);
            actions.GalleryRequested += (_, _) => ShowGallery();
            actions.ExportRequested += (_, _) => Export();
            actions.PressRequested += (_, _) => OnPressRequested();
        }

        GalleryLayer.CloseRequested += (_, _) => GalleryLayer.IsVisible = false;
        Canvas.FittedChanged += (_, _) => FitButton.IsVisible = !Canvas.IsFitted;
        FitButton.Click += (_, _) => Canvas.FitToView(animate: true);
        PressNowButton.Click += (_, _) => _ = _studio.PressAsync(_pressFold);
        PressCancelButton.Click += (_, _) => _studio.ArmPress(false);
        _studio.SettingsChanged += (_, _) => UpdatePressBanner();
        _studio.Notice += (_, message) => ShowToast(message);

        Panes.ModeChanged += (_, _) => ApplyLayout();
        Stage.SizeChanged += (_, _) => ApplyLayout();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.IsVisible = false;
        };

        ApplyLayout();
    }

    private enum StageLayout
    {
        Phone,
        Book,
        Tabletop,
    }

    /// <summary>
    /// Saves the drawing before the app quits.
    /// </summary>
    public void SaveBeforeExit() => _studio.SaveBeforeExit();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The page takes the whole screen, with no system bars; the rails keep clear of the camera
        // and the corners.
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel?.InsetsManager is { } insets)
        {
            insets.DisplayEdgeToEdgePreference = true;
            insets.IsSystemBarVisible = false;
            insets.SafeAreaChanged += OnSafeAreaChanged;
        }

        // Saves when the app goes to the background, where it may be closed without warning.
        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } lifetime)
            lifetime.Deactivated += OnDeactivated;

        ApplyLayout();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel?.InsetsManager is { } insets)
            insets.SafeAreaChanged -= OnSafeAreaChanged;
        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } lifetime)
            lifetime.Deactivated -= OnDeactivated;
        _topLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == Fold.StateProperty && change.NewValue is FoldState state)
        {
            WatchHingeForPress(state);

            var fold = state with { HingeAngle = null };
            if (fold != _layoutFold)
            {
                _layoutFold = fold;
                ApplyLayout();
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Z when command && shift:
            case Key.Y when command:
                _studio.Redo();
                break;
            case Key.Z when command:
                _studio.Undo();
                break;
            case Key.B when !command:
                _studio.SelectTool(ToolKind.Pen);
                break;
            case Key.M when !command:
                _studio.SelectTool(ToolKind.Marker);
                break;
            case Key.W when !command:
                _studio.SelectTool(ToolKind.Wash);
                break;
            case Key.A when !command:
                _studio.SelectTool(ToolKind.Spray);
                break;
            case Key.E when !command:
                _studio.SelectTool(ToolKind.Eraser);
                break;
            case Key.G when !command:
                _studio.SelectTool(ToolKind.Fill);
                break;
            case Key.I when !command:
                _studio.SelectTool(ToolKind.Picker);
                break;
            case Key.OemOpenBrackets:
                _studio.SetSize(_studio.Tool.Size / 1.2);
                break;
            case Key.OemCloseBrackets:
                _studio.SetSize(_studio.Tool.Size * 1.2);
                break;
            case Key.S when !command:
                _studio.SetSymmetry(!_studio.Symmetry);
                break;
            case Key.P when !command:
                OnPressRequested();
                break;
            case Key.L when !command:
                TogglePanel(PanelSection.Layers, onLeft: false);
                break;
            case Key.D0 or Key.NumPad0:
                Canvas.FitToView(animate: true);
                break;
            case Key.Escape:
                ClosePanel();
                GalleryLayer.IsVisible = false;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnSafeAreaChanged(object? sender, EventArgs e) => ApplyLayout();

    private void OnDeactivated(object? sender, ActivatedEventArgs e) => _ = _studio.SaveNow();

    // Lays the rails, panel and messages out for the posture: a book with a rail on each page, the
    // tabletop with its tools on the bottom half, or a phone with a rail at the top and the bottom.
    private void ApplyLayout()
    {
        var safe = _topLevel?.InsetsManager?.SafeAreaPadding ?? default;
        var state = Fold.GetState(this);
        _layout = Panes.Mode == TwoPaneViewMode.Tall
            ? StageLayout.Tabletop
            : state.FoldBounds is not null && state.Orientation == FoldOrientation.Vertical && state.Posture != DevicePosture.Closed
                ? StageLayout.Book
                : StageLayout.Phone;

        var width = Stage.Bounds.Width;
        var height = Stage.Bounds.Height;
        switch (_layout)
        {
            case StageLayout.Tabletop:
            {
                PressBanner.MaxWidth = 420;
                Toast.MaxWidth = 420;
                GalleryLayer.CardMargin = new Thickness(16);
                Tools.IsVisible = false;
                Actions.IsVisible = false;
                PanelHost.IsVisible = false;
                DockHost.Padding = new Thickness(safe.Left, 0, safe.Right, safe.Bottom);
                Canvas.FitInsets = new Thickness(safe.Left, safe.Top, safe.Right, 0);
                Place(FitButton, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(safe.Left + Gap, safe.Top + Gap, 0, 0));
                Place(PressBanner, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, safe.Top + Gap, 0, 0));
                Place(Toast, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, height / 2));
                break;
            }

            case StageLayout.Book:
            {
                // Symmetric, so the page's middle stays on the fold.
                var side = Math.Max(safe.Left, safe.Right) + Gap;
                var inset = side + RailThickness + 6;
                Tools.IsVisible = true;
                Tools.Orientation = Orientation.Vertical;
                Place(Tools, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(side, 0, 0, 0));
                Actions.IsVisible = true;
                Actions.Orientation = Orientation.Vertical;
                Place(Actions, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, side, 0));
                Canvas.FitInsets = new Thickness(inset, safe.Top, inset, safe.Bottom);
                Place(FitButton, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(inset + Gap, safe.Top + Gap, 0, 0));
                // Messages and the panel keep to one page, clear of the fold.
                var page = Math.Max(200, (width / 2) - FoldAllowance - inset - (2 * Gap));
                PressBanner.MaxWidth = page;
                Toast.MaxWidth = page;
                Place(PressBanner, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, safe.Top + Gap, inset + Gap, 0));
                Place(Toast, HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(inset + Gap, 0, 0, safe.Bottom + Gap));

                // The panel opens on the page of the rail that asked for it, level with the rail's top,
                // and grows down: its header stays put while its content changes.
                var rail = _panelOnLeft ? (Control)Tools : Actions;
                var top = Math.Max(safe.Top + Gap, (height - rail.Bounds.Height) / 2);
                PanelHost.Width = Math.Min(FloatingPanelWidth, page);
                PanelHost.Height = double.NaN;
                PanelHost.MaxHeight = Math.Max(200, height - top - safe.Bottom - Gap);
                Place(
                    PanelHost,
                    _panelOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment.Top,
                    _panelOnLeft ? new Thickness(inset + 4, top, 0, 0) : new Thickness(0, top, inset + 4, 0));
                GalleryLayer.CardMargin = new Thickness((width / 2) + FoldAllowance + Gap, safe.Top + Gap, safe.Right + Gap, safe.Bottom + Gap);
                break;
            }

            default:
            {
                PressBanner.MaxWidth = 420;
                Toast.MaxWidth = 420;
                GalleryLayer.CardMargin = new Thickness(safe.Left + Gap, safe.Top + Gap, safe.Right + Gap, safe.Bottom + Gap);
                var top = safe.Top + Gap + RailThickness;
                var bottom = safe.Bottom + Gap + RailThickness;
                Tools.IsVisible = true;
                Tools.Orientation = Orientation.Horizontal;
                Place(Tools, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, safe.Bottom + Gap));
                Actions.IsVisible = true;
                Actions.Orientation = Orientation.Horizontal;
                // At the top the sides count too: the iPhone Duo's cover display has its camera and clock in
                // the top corner, beside the app.
                Place(Actions, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(safe.Left, safe.Top + Gap, safe.Right, 0));
                Canvas.FitInsets = new Thickness(safe.Left, top, safe.Right, bottom);
                Place(FitButton, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(safe.Left, top + 8, safe.Right, 0));
                Place(PressBanner, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(safe.Left + Gap, top + 8, safe.Right + Gap, 0));
                Place(Toast, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, bottom + 8));

                // A sheet over the bottom of the page.
                PanelHost.Width = Math.Min(440, Math.Max(200, width - (2 * Gap)));
                PanelHost.Height = Math.Max(200, Math.Min(480, height - top - bottom - 16));
                PanelHost.MaxHeight = double.PositiveInfinity;
                Place(PanelHost, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, bottom + 8));
                break;
            }
        }
    }

    private static void Place(Control control, HorizontalAlignment horizontal, VerticalAlignment vertical, Thickness margin)
    {
        control.HorizontalAlignment = horizontal;
        control.VerticalAlignment = vertical;
        control.Margin = margin;
    }

    private void TogglePanel(PanelSection section, bool onLeft)
    {
        // The docked panel already shows every section.
        if (_layout == StageLayout.Tabletop)
            return;

        if (PanelHost.IsVisible && FloatingPanel.Section == section && (_panelOnLeft == onLeft || _layout != StageLayout.Book))
        {
            ClosePanel();
            return;
        }

        _panelOnLeft = onLeft;
        FloatingPanel.Show(section);
        PanelHost.IsVisible = true;
        ApplyLayout();
    }

    private void ClosePanel() => PanelHost.IsVisible = false;

    private void ShowGallery()
    {
        ClosePanel();
        GalleryLayer.IsVisible = true;
        GalleryLayer.Refresh();
    }

    // On a foldable the press waits for the fold; anywhere else it happens now.
    private void OnPressRequested()
    {
        if (_studio.IsPressArmed)
        {
            _studio.ArmPress(false);
            return;
        }

        var state = Fold.GetState(this);
        _pressFold = state.Orientation == FoldOrientation.Horizontal ? PressFold.Horizontal : PressFold.Vertical;
        if (state.IsFoldable)
        {
            _press.Arm();
            _studio.ArmPress(true);
        }
        else
        {
            _ = _studio.PressAsync(_pressFold);
        }
    }

    private void WatchHingeForPress(FoldState state)
    {
        if (!_studio.IsPressArmed)
            return;

        switch (_press.Update(state))
        {
            case PressStep.Closed:
                UpdatePressBanner();
                break;
            case PressStep.Opened:
                _ = _studio.PressAsync(_pressFold);
                break;
        }
    }

    private void UpdatePressBanner()
    {
        PressBanner.IsVisible = _studio.IsPressArmed;
        PressText.Text = _press.IsClosed
            ? "Now open it to see the print."
            : _pressFold == PressFold.Horizontal
                ? "Close the phone, then open it: the paint presses onto the other half of the page."
                : "Close the phone like a book, then open it: the paint presses onto the facing page.";
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // A PNG of the page as it looks, paper and all. An async void handler: anything it throws would end
    // the app, so every failure becomes a message.
    private async void Export()
    {
        var document = _studio.Document;
        var name = string.Concat(document.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '-' : c)) + ".png";

        try
        {
            using var image = DocumentRenderer.RenderComposite(document, true);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("The page couldn't be encoded as a PNG.");

            if (_topLevel?.StorageProvider is { CanSave: true } storage)
            {
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export drawing",
                    SuggestedFileName = name,
                    DefaultExtension = "png",
                    FileTypeChoices = [FilePickerFileTypes.ImagePng],
                    ShowOverwritePrompt = true,
                });
                if (file is null)
                    return;

                await using (var stream = await file.OpenWriteAsync())
                    png.SaveTo(stream);
                ShowToast($"Exported {file.Name}");
            }
            else
            {
                // No save dialog on this platform: the documents folder instead.
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Inkfold");
                Directory.CreateDirectory(folder);
                using (var stream = File.Create(Path.Combine(folder, name)))
                    png.SaveTo(stream);
                ShowToast($"Exported {name} to Inkfold's documents");
            }
        }
        catch (Exception exception)
        {
            ShowToast($"Couldn't export: {exception.Message}");
        }
    }
}
