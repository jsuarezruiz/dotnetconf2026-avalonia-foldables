using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Inkfold.Painting;

namespace Inkfold.Controls;

/// <summary>
/// The tools, and the color in hand. Tapping the tool in hand, or the color, asks for the full panel.
/// </summary>
public partial class ToolRail : UserControl
{
    /// <summary>
    /// Defines the <see cref="Orientation"/> property.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ToolRail, Orientation>(nameof(Orientation), Orientation.Vertical);

    private readonly (ToolKind Kind, Button Button)[] _tools;
    private Studio? _studio;
    private bool _updating;

    public ToolRail()
    {
        InitializeComponent();
        _tools =
        [
            (ToolKind.Pen, PenButton),
            (ToolKind.Marker, MarkerButton),
            (ToolKind.Wash, WashButton),
            (ToolKind.Spray, SprayButton),
            (ToolKind.Eraser, EraserButton),
            (ToolKind.Fill, FillButton),
            (ToolKind.Picker, PickerButton),
        ];

        foreach (var (kind, button) in _tools)
            button.Click += (_, _) => OnTool(kind);

        ColorWell.Click += (_, _) => PanelRequested?.Invoke(this, PanelSection.Color);
        SizeSlider.ValueChanged += (_, e) =>
        {
            if (!_updating && _studio is { } studio)
                studio.SetSize(SizeFromSlider(studio.Tool, e.NewValue));
        };
        OpacitySlider.ValueChanged += (_, e) =>
        {
            if (!_updating)
                _studio?.SetOpacity(e.NewValue);
        };
        ApplyOrientation();
    }

    /// <summary>
    /// Raised to open the full panel at a section.
    /// </summary>
    public event EventHandler<PanelSection>? PanelRequested;

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public Studio? Studio
    {
        get => _studio;
        set
        {
            if (_studio is { } old)
                old.SettingsChanged -= OnSettingsChanged;
            _studio = value;
            if (value is not null)
                value.SettingsChanged += OnSettingsChanged;
            Refresh();
        }
    }

    /// <summary>
    /// Maps a brush size to a slider position, on a log scale so small sizes get room.
    /// </summary>
    public static double SliderFromSize(ToolInfo tool) =>
        tool.MaxSize <= tool.MinSize ? 0 : Math.Log(tool.Size / tool.MinSize) / Math.Log(tool.MaxSize / tool.MinSize);

    public static double SizeFromSlider(ToolInfo tool, double value) =>
        tool.MinSize * Math.Pow(tool.MaxSize / tool.MinSize, Math.Clamp(value, 0, 1));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OrientationProperty)
            ApplyOrientation();
    }

    private void OnTool(ToolKind kind)
    {
        if (_studio is not { } studio)
            return;

        if (studio.Tool.Kind == kind)
            PanelRequested?.Invoke(this, PanelSection.Brush);
        else
            studio.SelectTool(kind);
    }

    private void ApplyOrientation()
    {
        var vertical = Orientation == Orientation.Vertical;
        Items.Orientation = Orientation;
        Divider.Width = vertical ? double.NaN : 1;
        Divider.Height = vertical ? 1 : double.NaN;
        Divider.Margin = vertical ? new Thickness(8, 4) : new Thickness(4, 8);
        Sliders.IsVisible = vertical;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (_studio is not { } studio)
            return;

        _updating = true;
        foreach (var (kind, button) in _tools)
            button.Classes.Set("selected", studio.Tool.Kind == kind);

        var color = studio.Color;
        ColorDot.Background = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        SizeSlider.IsEnabled = studio.Tool.HasSize;
        SizeSlider.Value = SliderFromSize(studio.Tool);
        OpacitySlider.IsEnabled = studio.Tool.Kind != ToolKind.Picker;
        OpacitySlider.Value = studio.Tool.Opacity;
        _updating = false;
    }
}

/// <summary>
/// A part of the full tool panel.
/// </summary>
public enum PanelSection
{
    Brush,
    Color,
    Layers,
}
