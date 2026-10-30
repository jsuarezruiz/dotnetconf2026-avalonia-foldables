using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Inkfold.Controls;

/// <summary>
/// Undo and redo, symmetry and the press, and the way to the layers, the gallery and export.
/// </summary>
public partial class ActionRail : UserControl
{
    /// <summary>
    /// Defines the <see cref="Orientation"/> property.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ActionRail, Orientation>(nameof(Orientation), Orientation.Vertical);

    private Studio? _studio;

    public ActionRail()
    {
        InitializeComponent();
        UndoButton.Click += (_, _) => _studio?.Undo();
        RedoButton.Click += (_, _) => _studio?.Redo();
        SymmetryButton.Click += (_, _) => _studio?.SetSymmetry(!_studio.Symmetry);
        PressButton.Click += (_, _) => PressRequested?.Invoke(this, EventArgs.Empty);
        LayersButton.Click += (_, _) => LayersRequested?.Invoke(this, EventArgs.Empty);
        GalleryButton.Click += (_, _) => GalleryRequested?.Invoke(this, EventArgs.Empty);
        ExportButton.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        ApplyOrientation();
    }

    public event EventHandler? PressRequested;

    public event EventHandler? LayersRequested;

    public event EventHandler? GalleryRequested;

    public event EventHandler? ExportRequested;

    /// <summary>
    /// Gets or sets whether the rail has a layers button; the docked panel shows the layers itself.
    /// </summary>
    public bool ShowsLayers
    {
        get => LayersButton.IsVisible;
        set => LayersButton.IsVisible = value;
    }

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OrientationProperty)
            ApplyOrientation();
    }

    private void ApplyOrientation()
    {
        var vertical = Orientation == Orientation.Vertical;
        Items.Orientation = Orientation;
        foreach (var divider in new[] { FirstDivider, SecondDivider })
        {
            divider.Width = vertical ? double.NaN : 1;
            divider.Height = vertical ? 1 : double.NaN;
            divider.Margin = vertical ? new Thickness(8, 4) : new Thickness(4, 8);
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (_studio is not { } studio)
            return;

        UndoButton.IsEnabled = studio.History.CanUndo;
        RedoButton.IsEnabled = studio.History.CanRedo;
        SymmetryButton.Classes.Set("selected", studio.Symmetry);
        PressButton.Classes.Set("selected", studio.IsPressArmed);
    }
}
