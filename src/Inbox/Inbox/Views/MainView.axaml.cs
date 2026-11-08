using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Nova.Avalonia.Foldable;

namespace Inbox.Views;

/// <summary>
/// The inbox: a list and the open message, either side of the fold. Folding the device is a resize, not a
/// relaunch: the open message, the reply, the reading position and the focus all stay where they were.
/// </summary>
public partial class MainView : UserControl
{
    private readonly InboxViewModel _vm = new();
    private TopLevel? _topLevel;

    // The control that had the focus, and one whose pane hid while it did.
    private Control? _focused;
    private Control? _restore;

    public MainView()
    {
        InitializeComponent();
        DataContext = _vm;

        InboxPane.Selected += (_, message) => _vm.SelectedMessage = message;
        InboxPane.Opened += (_, message) =>
        {
            var onePane = Panes.Mode == TwoPaneViewMode.SinglePane;
            _vm.Open(message, onePane);
            ShowPanes();
            if (onePane)
            {
                // The list just hid: start the keyboard on the new page, so Escape and Tab still work.
                MessagePane.SlideIn();
                Panes.UpdateLayout();
                MessagePane.BackButton.Focus();
            }
        };
        MessagePane.BackRequested += (_, _) => GoBack();

        // The mouse's back button, on desktop.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsXButton1Pressed && GoBack())
                e.Handled = true;
        }, RoutingStrategies.Tunnel);

        // The list needs no help to keep its place: a virtualizing ListBox keeps its first visible row
        // through a resize. The open message's reading position does: see MessageView.
        KeepFocus();
        Panes.ModeChanged += (_, _) =>
        {
            // Both panes on screen: there's no "reading" pane to go back from.
            if (Panes.Mode != TwoPaneViewMode.SinglePane)
                _vm.IsReading = false;
            ShowPanes();
        };

        ShowPanes();
    }

    /// <summary>
    /// Gets the state the views show, which outlives them.
    /// </summary>
    public InboxViewModel ViewModel => _vm;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The system's back: Android's back button or gesture.
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.BackRequested += OnSystemBack;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.BackRequested -= OnSystemBack;
        _topLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled && GoBack())
            e.Handled = true;
    }

    // Handled only when there's somewhere to go back to, so the system can still leave the app.
    private void OnSystemBack(object? sender, RoutedEventArgs e)
    {
        if (GoBack())
            e.Handled = true;
    }

    // Back from a message opened on its own, however the person asked: the button, the system's back,
    // Escape, the mouse's back button or a swipe from the edge.
    private bool GoBack()
    {
        if (Panes.Mode != TwoPaneViewMode.SinglePane || !_vm.IsReading)
            return false;

        _vm.Back();
        ShowPanes();

        // The list is shown at the next layout pass, and a hidden control can't take the focus.
        Panes.UpdateLayout();
        FocusOpenMessage();
        return true;
    }

    // One pane: the list, or the message opened from it. Closing the device goes back to the list, as
    // most mail apps do. Back only makes sense with one pane.
    private void ShowPanes()
    {
        Panes.PanePriority = _vm.IsReading ? TwoPaneViewPriority.Pane2 : TwoPaneViewPriority.Pane1;
        MessagePane.ShowsBack = Panes.Mode == TwoPaneViewMode.SinglePane;
    }

    // Never leave the focus on something hidden: when a fold hides the pane holding it, it moves to the
    // matching control next door, and comes back when the pane does. The reply keeps its text, caret and
    // (with ClearSelectionOnLostFocus off) its selection, because a hidden pane isn't destroyed.
    private void KeepFocus()
    {
        AddHandler(GotFocusEvent, (_, e) =>
        {
            // The person moved the focus on their own since a pane hid: that's where it stays.
            if (_restore is not null && _restore != _focused)
                _restore = null;
            _focused = e.Source as Control;
        });
        Panes.ModeChanged += (_, _) =>
        {
            if (_restore is { IsEffectivelyVisible: true } back)
            {
                _restore = null;
                back.Focus();                    // its pane is back
            }
            else if (_focused is { IsEffectivelyVisible: false } hidden)
            {
                _restore = hidden;               // its pane just hid
                CounterpartOf(hidden).Focus();   // land next door
            }

            _focused?.BringIntoView();
        };
    }

    // The reply's counterpart is its message in the list; the list's is the reply.
    private Control CounterpartOf(Control control) =>
        MessagePane.IsVisualAncestorOf(control)
            ? OpenMessageRow() ?? InboxPane.List
            : MessagePane.ReplyBox;

    private Control? OpenMessageRow() =>
        _vm.SelectedMessage is { } message ? InboxPane.List.ContainerFromItem(message) : null;

    private void FocusOpenMessage() => (OpenMessageRow() ?? InboxPane.List).Focus();
}
