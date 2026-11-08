using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Inbox.Views;

/// <summary>
/// The open message, and the reply to it.
/// </summary>
public partial class MessageView : UserControl
{
    // A swipe that starts this close to the left edge goes back, as on iOS.
    private const double EdgeWidth = 28;

    private readonly TranslateTransform _slide = new();
    private readonly Transitions _settle =
    [
        new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut() },
    ];

    private InboxViewModel? _vm;
    private IPointer? _swipe;
    private double _swipeFrom;

    public MessageView()
    {
        InitializeComponent();

        BackButton.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        SendButton.Click += (_, _) => _vm?.Send();
        RenderTransform = _slide;
        _slide.Transitions = _settle;
        KeepReadingPosition();
        KeepEdgeSwipe();
    }

    /// <summary>
    /// Occurs when the person goes back to the list.
    /// </summary>
    public event EventHandler? BackRequested;

    /// <summary>
    /// Sets whether the back button shows: only when this pane is on its own.
    /// </summary>
    public bool ShowsBack
    {
        get => BackButton.IsVisible;
        set => BackButton.IsVisible = value;
    }

    /// <summary>
    /// Slides the message in from the right, as opening a page does.
    /// </summary>
    public void SlideIn()
    {
        _slide.Transitions = null;
        _slide.X = 120;
        _slide.Transitions = _settle;
        _slide.X = 0;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as InboxViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelChanged;
    }

    // Another message starts at its top.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InboxViewModel.SelectedMessage))
            Reader.Offset = default;
    }

    // Remember what the person was reading, not where: the fold changes the message's width, every line
    // rewraps, and the same pixel offset would land in another paragraph, or past the end.
    private void KeepReadingPosition()
    {
        Reader.ScrollChanged += (_, e) =>
        {
            // Real scrolling only, not a resize
            if (e.ViewportDelta == default && e.ExtentDelta == default && _vm is not null)
                _vm.ReadingPosition = CharacterAtTop();
        };

        // After the fold, bring the same words back to the top.
        Body.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged && _vm is { ReadingPosition: > 0 } vm)
                Dispatcher.UIThread.Post(() => ScrollToCharacter(vm.ReadingPosition));
        };
    }

    // The character on the line at the top of the reader. ScrollChanged comes before the content is
    // arranged at the new offset, so this works from the offset rather than from where things are drawn.
    private int CharacterAtTop()
    {
        var y = Reader.Offset.Y - BodyTop();
        return y > 0 ? Body.TextLayout.HitTestPoint(new Point(0, y + 1)).TextPosition : 0;
    }

    private void ScrollToCharacter(int index)
    {
        var line = Body.TextLayout.HitTestTextPosition(index);
        Reader.Offset = Reader.Offset.WithY(Math.Max(0, BodyTop() + line.Top));
    }

    // Where the body starts in the scrolled content, whatever the offset.
    private double BodyTop() =>
        Reader.Content is Control content && Body.TranslatePoint(default, content) is { } at
            ? content.Margin.Top + at.Y
            : 0;

    // A swipe in from the left edge: the message follows the finger, and letting it go a quarter of the way
    // across goes back; short of that, it slides into place again.
    private void KeepEdgeSwipe()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!ShowsBack || _swipe is not null || this.GetVisualParent() is not { } parent || e.GetPosition(this).X > EdgeWidth)
                return;

            _swipe = e.Pointer;
            _swipeFrom = e.GetPosition(parent).X;
            _slide.Transitions = null;
            e.Pointer.Capture(this);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        AddHandler(PointerMovedEvent, (_, e) =>
        {
            // Measured against the parent: this view moves with the finger.
            if (e.Pointer == _swipe && this.GetVisualParent() is { } parent)
                _slide.X = Math.Max(0, e.GetPosition(parent).X - _swipeFrom);
        }, RoutingStrategies.Tunnel);

        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.Pointer == _swipe)
                EndSwipe(back: _slide.X > Math.Min(120, Bounds.Width / 4));
        }, RoutingStrategies.Tunnel);

        AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (e.Pointer == _swipe)
                EndSwipe(back: false);
        });
    }

    private void EndSwipe(bool back)
    {
        var pointer = _swipe;
        _swipe = null;
        pointer?.Capture(null);

        if (back)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
            _slide.X = 0;
        }

        _slide.Transitions = _settle;
        _slide.X = 0;
    }
}
