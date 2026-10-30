using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Inkfold.Controls;
using Inkfold.Painting;

namespace Inkfold.Views;

/// <summary>
/// The saved drawings. Tap one to open it; the bin asks once more before deleting.
/// </summary>
public partial class GalleryView : UserControl
{
    // Two cards fit across one page of the open book.
    private const double CardWidth = 156;

    private Studio? _studio;

    public GalleryView()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        NewButton.Click += (_, _) =>
        {
            _studio?.NewDrawing();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? CloseRequested;

    public Studio? Studio
    {
        get => _studio;
        set => _studio = value;
    }

    /// <summary>
    /// Sets where the gallery's card sits, such as on one page of an open book.
    /// </summary>
    public Thickness CardMargin
    {
        get => CardHost.Margin;
        set => CardHost.Margin = value;
    }

    /// <summary>
    /// Lists the drawings again, the open one saved first so its thumbnail is current.
    /// </summary>
    public async void Refresh()
    {
        if (_studio is not { } studio)
            return;

        await studio.SaveNow();
        Items.Children.Clear();
        var items = studio.Gallery.List();
        CountText.Text = items.Count == 1 ? "1 drawing" : $"{items.Count} drawings";
        foreach (var item in items)
            Items.Children.Add(CreateCard(studio, item));
    }

    private Control CreateCard(Studio studio, GalleryItem item)
    {
        var isOpen = item.Id == studio.Document.Id;
        var open = new Button
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new Border
                    {
                        CornerRadius = new CornerRadius(14),
                        ClipToBounds = true,
                        BorderThickness = new Thickness(isOpen ? 2 : 1),
                        BorderBrush = isOpen ? Brush("AccentBrush") : Brush("ChromeLineBrush"),
                        Height = CardWidth * Document.DefaultHeight / Document.DefaultWidth,
                        Child = new Image { Source = Bitmaps.Load(item.ThumbnailPath), Stretch = Stretch.UniformToFill },
                    },
                    new TextBlock { Text = item.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0) },
                    new TextBlock
                    {
                        Text = isOpen ? "Open now" : item.Modified.ToString("MMM d, HH:mm"),
                        Classes = { "value" },
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(4, 0),
                    },
                },
            },
        };
        open.Click += (_, _) =>
        {
            studio.OpenDrawing(item.Id);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };

        var delete = new Button
        {
            Classes = { "icon", "small" },
            Background = Brush("ChromeBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            Content = new Icon { Data = Icons.Trash, Width = 18, Height = 18 },
        };
        ToolTip.SetTip(delete, "Delete");
        AutomationProperties.SetName(delete, $"Delete {item.Name}");
        AutomationProperties.SetName(open, isOpen ? $"{item.Name}, open now" : item.Name);
        var confirming = false;
        delete.Click += (_, _) =>
        {
            if (!confirming)
            {
                // A second tap deletes; the first only arms it.
                confirming = true;
                delete.Width = double.NaN;
                delete.Padding = new Thickness(12, 0);
                delete.Background = Brush("PressBrush");
                delete.Content = new TextBlock { Text = "Delete?", Foreground = Brushes.White, FontWeight = FontWeight.SemiBold };
                return;
            }

            studio.DeleteDrawing(item.Id);
            Refresh();
        };

        return new Panel { Width = CardWidth, Children = { open, delete } };
    }

    // App resources, found the same way before the control is on screen.
    private static IBrush? Brush(string key) => Application.Current?.FindResource(key) as IBrush;
}
