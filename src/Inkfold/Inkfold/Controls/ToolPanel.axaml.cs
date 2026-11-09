using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Inkfold.Painting;
using SkiaSharp;

namespace Inkfold.Controls;

/// <summary>
/// Everything about the brush, the color and the layers. Docked under the page in tabletop it shows all
/// three side by side; floating, it shows one section at a time.
/// </summary>
public partial class ToolPanel : UserControl
{
    /// <summary>
    /// Defines the <see cref="IsDocked"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsDockedProperty =
        AvaloniaProperty.Register<ToolPanel, bool>(nameof(IsDocked));

    // Inks and paints, from dark to light, and the paper's own color for painting it back.
    // Named, so a screen reader can say which color a swatch is.
    private static readonly (SKColor Color, string Name)[] Palette =
    [
        (new(0x1D, 0x1B, 0x2A), "Ink"), (new(0x4A, 0x47, 0x57), "Slate"), (new(0x8E, 0x8A, 0x99), "Gray"), (new(0xF6, 0xF1, 0xE6), "Paper"),
        (new(0xC6, 0x28, 0x28), "Red"), (new(0xF2, 0x6B, 0x3A), "Orange"), (new(0xF5, 0xB7, 0x2E), "Amber"), (new(0xFF, 0xE3, 0x6E), "Yellow"),
        (new(0x2E, 0x9E, 0x5B), "Green"), (new(0x14, 0x7D, 0x7E), "Teal"), (new(0x1F, 0x6F, 0xD1), "Blue"), (new(0x1E, 0x2D, 0x7A), "Navy"),
        (new(0x6B, 0x3F, 0xC7), "Purple"), (new(0xE0, 0x4F, 0x9B), "Pink"), (new(0x8B, 0x5A, 0x2B), "Brown"), (new(0xD9, 0xB0, 0x8C), "Tan"),
    ];

    // The label fits the tile; the name is what the tooltip and a screen reader say, as on the rail.
    private static readonly (ToolKind Kind, string Icon, string Label, string Name)[] ToolTiles =
    [
        (ToolKind.Pen, Icons.PenLine, "Pen", "Pen"),
        (ToolKind.Marker, Icons.Highlighter, "Marker", "Marker"),
        (ToolKind.Wash, Icons.Droplets, "Water", "Watercolor"),
        (ToolKind.Spray, Icons.SprayCan, "Spray", "Spray"),
        (ToolKind.Eraser, Icons.Eraser, "Eraser", "Eraser"),
        (ToolKind.Fill, Icons.PaintBucket, "Fill", "Fill"),
        (ToolKind.Picker, Icons.Pipette, "Picker", "Picker"),
    ];

    private readonly Dictionary<ToolKind, Button> _toolButtons = [];
    private readonly List<TextBlock> _toolNames = [];
    private readonly List<(SKColor Color, Button Button)> _swatches = [];
    private readonly DispatcherTimer _thumbnails;
    private Studio? _studio;
    private PanelSection _section = PanelSection.Brush;
    private bool _updating;

    public ToolPanel()
    {
        InitializeComponent();

        foreach (var (kind, icon, text, name) in ToolTiles)
        {
            var label = new TextBlock { Text = text };
            _toolNames.Add(label);
            var button = new Button
            {
                Classes = { "tile" },
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new Icon { Data = icon, HorizontalAlignment = HorizontalAlignment.Center },
                        label,
                    },
                },
                Margin = new Thickness(3),
            };
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => _studio?.SelectTool(kind);
            _toolButtons[kind] = button;
            ToolGrid.Children.Add(button);
        }

        foreach (var (color, _) in Palette)
        {
            var button = Swatch(color);
            _swatches.Add((color, button));
            Swatches.Children.Add(button);
        }

        BrushTab.Click += (_, _) => Show(PanelSection.Brush);
        ColorTab.Click += (_, _) => Show(PanelSection.Color);
        LayersTab.Click += (_, _) => Show(PanelSection.Layers);
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        SizeSlider.ValueChanged += (_, e) =>
        {
            if (!_updating && _studio is { } studio)
                studio.SetSize(ToolRail.SizeFromSlider(studio.Tool, e.NewValue));
        };
        OpacitySlider.ValueChanged += (_, e) =>
        {
            if (!_updating)
                _studio?.SetOpacity(e.NewValue);
        };
        Spectrum.ColorChanged += (_, e) =>
        {
            if (!_updating)
                _studio?.SetColor(new SKColor(e.NewColor.R, e.NewColor.G, e.NewColor.B));
        };

        AddLayerButton.Click += (_, _) => _studio?.AddLayer();
        DuplicateLayerButton.Click += (_, _) => _studio?.DuplicateLayer();
        DeleteLayerButton.Click += (_, _) => _studio?.DeleteLayer();
        LayerUpButton.Click += (_, _) => _studio?.MoveLayer(1);
        LayerDownButton.Click += (_, _) => _studio?.MoveLayer(-1);
        ClearLayerButton.Click += (_, _) => _studio?.ClearLayer();
        LayerOpacitySlider.ValueChanged += (_, e) =>
        {
            if (!_updating && _studio is { } studio)
                studio.SetLayerOpacity(studio.Document.ActiveIndex, e.NewValue);
        };

        // The 16 swatches sit in even rows, eight or four across, never with one left over.
        ColorSection.SizeChanged += (_, e) =>
        {
            const double cell = 34;
            Swatches.Columns = e.NewSize.Width >= 8 * cell ? 8 : 4;
            Swatches.Width = Swatches.Columns * cell;
        };

        // Layer thumbnails follow the paint, a moment behind it.
        _thumbnails = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _thumbnails.Tick += (_, _) =>
        {
            _thumbnails.Stop();
            RefreshThumbnails();
        };

        ApplyDocking();
    }

    /// <summary>
    /// Raised when the floating panel's close button is pressed.
    /// </summary>
    public event EventHandler? CloseRequested;

    public bool IsDocked
    {
        get => GetValue(IsDockedProperty);
        set => SetValue(IsDockedProperty, value);
    }

    public PanelSection Section => _section;

    public Studio? Studio
    {
        get => _studio;
        set
        {
            if (_studio is { } old)
            {
                old.SettingsChanged -= OnSettingsChanged;
                old.LayersChanged -= OnLayersChanged;
                old.PaintChanged -= OnPaintChanged;
            }

            _studio = value;
            Preview.Studio = value;
            if (value is not null)
            {
                value.SettingsChanged += OnSettingsChanged;
                value.LayersChanged += OnLayersChanged;
                value.PaintChanged += OnPaintChanged;
            }

            RefreshSettings();
            RebuildLayers();
        }
    }

    /// <summary>
    /// Shows a section of the floating panel.
    /// </summary>
    public void Show(PanelSection section)
    {
        _section = section;
        ApplyDocking();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsDockedProperty)
            ApplyDocking();
    }

    private void ApplyDocking()
    {
        var docked = IsDocked;
        Header.IsVisible = !docked;
        BrushCaption.IsVisible = docked;
        ColorCaption.IsVisible = docked;
        LayersCaption.IsVisible = docked;
        Sections.ColumnDefinitions.Clear();
        if (docked)
            Sections.ColumnDefinitions.AddRange([new(1.1, GridUnitType.Star), new(1, GridUnitType.Star), new(1, GridUnitType.Star)]);

        // Docked, the section is narrow: the tools become square icon buttons, four to a row, as on
        // the rails. Floating, they're tiles with their names.
        foreach (var button in _toolButtons.Values)
        {
            button.Width = docked ? 44 : double.NaN;
            button.Height = docked ? 44 : double.NaN;
            button.Padding = docked ? new Thickness(0) : new Thickness(4, 8);
            button.HorizontalAlignment = docked ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Center;
        }

        foreach (var label in _toolNames)
            label.IsVisible = !docked;
        ToolGrid.HorizontalAlignment = docked ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Spectrum.Height = docked ? 120 : 150;
        PreviewBox.Height = docked ? 52 : 64;
        var margin = docked ? new Thickness(14, 12, 14, 12) : new Thickness(16, 8, 16, 16);
        BrushSection.Margin = margin;
        ColorSection.Margin = margin;
        LayersSection.Margin = margin;
        BrushSection.Spacing = docked ? 8 : 10;
        ColorSection.Spacing = docked ? 8 : 10;

        var sections = new Control[] { BrushSection, ColorSection, LayersSection };
        for (var i = 0; i < sections.Length; i++)
        {
            Grid.SetColumn(sections[i], docked ? i : 0);
            sections[i].IsVisible = docked || (int)_section == i;
        }

        BrushTab.Classes.Set("selected", _section == PanelSection.Brush);
        ColorTab.Classes.Set("selected", _section == PanelSection.Color);
        LayersTab.Classes.Set("selected", _section == PanelSection.Layers);
    }

    private Button Swatch(SKColor color)
    {
        var button = new Button
        {
            Classes = { "swatch" },
            Background = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)),
            Margin = new Thickness(0, 0, 6, 6),
        };
        var name = Array.Find(Palette, entry => entry.Color == color).Name
            ?? $"Color #{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => _studio?.SetColor(color);
        return button;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => RefreshSettings();

    private void RefreshSettings()
    {
        if (_studio is not { } studio)
            return;

        _updating = true;
        var tool = studio.Tool;
        foreach (var (kind, button) in _toolButtons)
            button.Classes.Set("selected", kind == tool.Kind);

        SizeSlider.IsEnabled = tool.HasSize;
        SizeSlider.Value = ToolRail.SliderFromSize(tool);
        SizeText.Text = tool.HasSize ? $"{tool.Size:0} px" : "—";
        OpacitySlider.IsEnabled = tool.Kind != ToolKind.Picker;
        OpacitySlider.Value = tool.Opacity;
        OpacityText.Text = $"{tool.Opacity * 100:0}%";
        ToolHint.Text = tool.Kind switch
        {
            ToolKind.Fill => "Tap an area of the page to fill it",
            ToolKind.Picker => "Touch the page to pick up its color",
            _ => "",
        };
        ToolHint.IsVisible = !tool.IsBrush;

        var color = studio.Color;
        var current = Spectrum.Color;
        if (current.R != color.Red || current.G != color.Green || current.B != color.Blue)
            Spectrum.Color = Color.FromRgb(color.Red, color.Green, color.Blue);

        foreach (var (swatchColor, button) in _swatches)
            button.Classes.Set("selected", swatchColor == color);

        Recent.Children.Clear();
        foreach (var recent in studio.RecentColors)
        {
            var button = Swatch(recent);
            button.Classes.Set("selected", recent == color);
            Recent.Children.Add(button);
        }

        RecentRow.IsVisible = studio.RecentColors.Count > 0;
        _updating = false;
    }

    private void OnLayersChanged(object? sender, EventArgs e) => RebuildLayers();

    private void OnPaintChanged(object? sender, EventArgs e)
    {
        _thumbnails.Stop();
        _thumbnails.Start();
    }

    // Top layer first, as they stack on the page. Rows are kept while the stack is the same, so
    // dragging a layer's opacity or toggling it doesn't rebuild the list.
    private void RebuildLayers()
    {
        var rows = LayerList.Children.OfType<LayerRow>().ToList();
        if (_studio is not { } studio)
        {
            ClearLayerRows(rows);
            return;
        }

        var document = studio.Document;
        var stack = Enumerable.Reverse(document.Layers).ToList();
        if (!rows.Select(row => row.Layer).SequenceEqual(stack))
        {
            ClearLayerRows(rows);
            foreach (var layer in stack)
            {
                var row = new LayerRow(layer, document);
                row.VisibilityToggled += (_, _) =>
                    studio.SetLayerVisible(studio.Document.Layers.IndexOf(row.Layer), !row.Layer.IsVisible);
                row.Selected += (_, _) => studio.SelectLayer(studio.Document.Layers.IndexOf(row.Layer));
                LayerList.Children.Add(row);
            }

            rows = LayerList.Children.OfType<LayerRow>().ToList();
        }

        foreach (var row in rows)
            row.Update(row.Layer == document.Active);

        _updating = true;
        LayerOpacitySlider.Value = document.Active.Opacity;
        LayerOpacityText.Text = $"{document.Active.Opacity * 100:0}%";
        _updating = false;
        LayerUpButton.IsEnabled = document.ActiveIndex < document.Layers.Count - 1;
        LayerDownButton.IsEnabled = document.ActiveIndex > 0;
    }

    private void ClearLayerRows(List<LayerRow> rows)
    {
        LayerList.Children.Clear();
        foreach (var row in rows)
            row.DisposeThumbnail();
    }

    private void RefreshThumbnails()
    {
        if (_studio is not { } studio)
            return;

        foreach (var row in LayerList.Children.OfType<LayerRow>())
            row.RefreshThumbnail(studio.Document);
    }

    private static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    // App resources, found the same way before the control is on screen.
    private static IBrush? Brush(string key) => Application.Current?.FindResource(key) as IBrush;

    /// <summary>
    /// A layer in the list: its thumbnail on paper, name and opacity, and an eye to show or hide it.
    /// </summary>
    private sealed class LayerRow : Border
    {
        private readonly Image _thumbnail;
        private readonly TextBlock _name;
        private readonly TextBlock _opacity;
        private readonly Button _eye;
        private readonly Icon _eyeIcon;

        public LayerRow(Layer layer, Document document)
        {
            Layer = layer;
            Classes.Add("layer");
            Focusable = true;
            _thumbnail = new Image { Width = 56, Height = 41, Stretch = Stretch.UniformToFill };
            _name = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            _opacity = new TextBlock { Classes = { "value" }, HorizontalAlignment = HorizontalAlignment.Left };
            _eyeIcon = new Icon { Width = 18, Height = 18 };
            _eye = new Button { Classes = { "icon", "small" }, Content = _eyeIcon };
            _eye.Click += (_, _) => VisibilityToggled?.Invoke(this, EventArgs.Empty);
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        ClipToBounds = true,
                        BorderBrush = Brush("ChromeLineBrush"),
                        BorderThickness = new Thickness(1),
                        Child = _thumbnail,
                    },
                    Column(new StackPanel
                    {
                        Margin = new Thickness(10, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { _name, _opacity },
                    }, 1),
                    Column(_eye, 2),
                },
            };
            RefreshThumbnail(document);
        }

        public event EventHandler? VisibilityToggled;

        public event EventHandler? Selected;

        public Layer Layer { get; }

        public void Update(bool active)
        {
            Classes.Set("selected", active);
            _name.Text = Layer.Name;
            _opacity.Text = $"{Layer.Opacity * 100:0}%";
            _eyeIcon.Data = Layer.IsVisible ? Icons.Eye : Icons.EyeOff;
            _eye.Opacity = Layer.IsVisible ? 1 : 0.55;
            AutomationProperties.SetName(_eye, Layer.IsVisible ? $"Hide {Layer.Name}" : $"Show {Layer.Name}");
            ToolTip.SetTip(_eye, Layer.IsVisible ? "Hide layer" : "Show layer");
            AutomationProperties.SetName(this, $"{Layer.Name}, {_opacity.Text}{(active ? ", selected" : "")}");
        }

        public void RefreshThumbnail(Document document)
        {
            var old = _thumbnail.Source as IDisposable;
            _thumbnail.Source = Bitmaps.LayerThumbnail(document, Layer, 112);
            old?.Dispose();
        }

        public void DisposeThumbnail()
        {
            (_thumbnail.Source as IDisposable)?.Dispose();
            _thumbnail.Source = null;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null)
                return;
            Selected?.Invoke(this, EventArgs.Empty);
        }

        // Enter or Space selects the layer, as a tap does.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || e.Source != this || e.Key is not (Key.Enter or Key.Space))
                return;
            Selected?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
