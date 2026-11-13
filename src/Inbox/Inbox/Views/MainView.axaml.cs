using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

    private bool _inBackground;

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
                // The list just hid: the keyboard starts on the message, so Escape goes back and Page Down
                // scrolls. Not on the back button: it hides when the device opens, and Enter would press it.
                MessagePane.SlideIn();
                Panes.UpdateLayout();
                MessagePane.Reader.Focus();
            }
        };
        MessagePane.BackRequested += (_, _) => GoBack();

        Panes.ModeChanged += (_, _) =>
        {
            // Both panes on screen: there's no "reading" pane to go back from. Not in the background, though:
            // there iOS lays the app out again for the app switcher's snapshot, wide enough for two panes,
            // and a mode nobody sees mustn't close the message they come back to.
            if (Panes.Mode != TwoPaneViewMode.SinglePane && !_inBackground)
                _vm.IsReading = false;
            ShowPanes();
        };

        // After the panes are set for the new mode, so the focus sees what's really hidden, the back
        // button included. The list needs no help to keep its place: a virtualizing ListBox keeps its first
        // visible row through a resize. The open message's reading position does: see MessageView.
        KeepFocus();

        ShowPanes();
    }

    /// <summary>
    /// Gets the state the views show.
    /// </summary>
    public InboxViewModel ViewModel => _vm;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The system's back: Android's back button or gesture, and on desktop the mouse's back button
        // and Alt+Left. The top level outlives this view, so the handler comes off when it detaches.
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.BackRequested += OnSystemBack;

        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } lifetime)
        {
            lifetime.Deactivated += OnDeactivated;
            lifetime.Activated += OnActivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.BackRequested -= OnSystemBack;
        _topLevel = null;

        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } lifetime)
        {
            lifetime.Deactivated -= OnDeactivated;
            lifetime.Activated -= OnActivated;
        }

        base.OnDetachedFromVisualTree(e);
    }

    // The app went to the background, or came back. Internal so the tests can send it there too: only
    // Avalonia can implement IActivatableLifetime.
    internal void OnDeactivated(object? sender, ActivatedEventArgs e) => _inBackground = true;

    // Back in front, the mode is one the person sees again. If the device opened meanwhile, both panes
    // are on screen now and the message stops being the "reading" pane.
    internal void OnActivated(object? sender, ActivatedEventArgs e)
    {
        _inBackground = false;
        if (Panes.Mode != TwoPaneViewMode.SinglePane && _vm.IsReading)
        {
            _vm.IsReading = false;
            ShowPanes();
        }
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
        // On a Mac, Option+Left in a text box moves the caret a word, but Avalonia also reads it as "back".
        // The text box keeps it; from there, Escape and the on-screen back button still go back.
        if (OperatingSystem.IsMacOS() && _topLevel?.FocusManager?.GetFocusedElement() is TextBox)
            return;

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
        // Any focus change, the person's or the hand-off's below, also forgets a control waiting for its
        // pane: the latest choice wins.
        AddHandler(GotFocusEvent, (_, e) =>
            (_focused, _restore) = (e.Source as Control, null));
        Panes.ModeChanged += (_, _) =>
        {
            if (_restore is { IsEffectivelyVisible: true } back)
                back.Focus();                    // its pane is back
            else if (_focused is { IsEffectivelyVisible: false } hidden)
            {
                CounterpartOf(hidden)?.Focus();  // land next door
                _restore = hidden;               // its pane just hid
            }
            else
                return;                          // nothing moved
            _focused?.BringIntoView();
        };
    }

    // The reply's counterpart is its message in the list; the list's is the reply. Not the search box,
    // which would bring up the keyboard: with the message hidden by a search, the first row in view.
    private Control? CounterpartOf(Control control) =>
        MessagePane.IsVisualAncestorOf(control)
            ? OpenMessageRow() ?? FirstRowInView()
            : MessagePane.ReplyBox;

    private Control? OpenMessageRow() =>
        _vm.SelectedMessage is { } message ? InboxPane.List.ContainerFromItem(message) : null;

    private Control? FirstRowInView() => InboxPane.List.GetRealizedContainers().FirstOrDefault();

    private void FocusOpenMessage() => (OpenMessageRow() ?? FirstRowInView())?.Focus();
}
