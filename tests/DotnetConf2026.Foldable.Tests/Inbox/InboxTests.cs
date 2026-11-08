using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Inbox.Views;
using Nova.Avalonia.Foldable;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.InboxApp;

// The inbox on a simulated iPhone Duo: open flat, closed on the cover display, and in tabletop.
public class InboxTests
{
    private static readonly FoldState Open = new(true, DevicePosture.Flat, 180, new Rect(455.5, 0, 40, 669), false);
    private static readonly FoldState Closed = new(true, DevicePosture.Closed, 0, null, false);
    private static readonly FoldState Tabletop = new(true, DevicePosture.HalfOpened, 90, new Rect(0, 314.5, 951, 40), true);

    [AvaloniaFact]
    public void The_open_message_and_the_reply_survive_closing_and_opening()
    {
        var device = new Device();
        var message = device.ViewModel.Messages[4];
        device.ViewModel.SelectedMessage = message;
        device.Settle();
        var reply = device.ReplyBox;
        reply.Text = "Thanks, I'll bring the hub";
        reply.CaretIndex = 7;

        device.Fold(Closed);
        Assert.Equal(TwoPaneViewMode.SinglePane, device.Panes.Mode);
        device.Fold(Open);

        Assert.Same(message, device.ViewModel.SelectedMessage);
        Assert.Equal("Thanks, I'll bring the hub", reply.Text);
        Assert.Equal("Thanks, I'll bring the hub", device.ViewModel.ReplyDraft);
        Assert.Equal(7, reply.CaretIndex);
    }

    [AvaloniaFact]
    public void The_reading_position_stays_on_the_same_words_through_the_fold()
    {
        var device = new Device();
        var message = device.ViewModel.Messages[0];
        device.ViewModel.SelectedMessage = message;
        device.Settle();
        var reader = device.Reader;
        var body = device.Body;

        // Scroll as a person would, to the third paragraph: the offset moves, the size doesn't.
        var words = message.Body.IndexOf("Third,", System.StringComparison.Ordinal);
        var line = body.TextLayout.HitTestTextPosition(words);
        reader.Offset = reader.Offset.WithY(reader.Offset.Y + body.TranslatePoint(line.TopLeft, reader)!.Value.Y);
        device.Settle();
        Assert.Equal(words, device.ViewModel.ReadingPosition);
        var offset = reader.Offset.Y;

        // Tabletop: the message goes above the fold at twice the width, and every line rewraps.
        device.Fold(Tabletop);

        Assert.Equal(TwoPaneViewMode.Tall, device.Panes.Mode);
        Assert.NotEqual(offset, reader.Offset.Y);
        var top = reader.TranslatePoint(default, body)!.Value;
        var atTop = body.TextLayout.HitTestPoint(new Point(0, top.Y + 1)).TextPosition;
        Assert.Equal(
            body.TextLayout.GetLineIndexFromCharacterIndex(words, false),
            body.TextLayout.GetLineIndexFromCharacterIndex(atTop, false));
    }

    [AvaloniaFact]
    public void The_list_keeps_its_place_through_the_fold()
    {
        // A virtualizing ListBox does this itself: the rows above the view aren't measured again.
        var device = new Device();
        var list = device.List;
        var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        var target = device.ViewModel.Messages[12];
        list.ScrollIntoView(target);
        device.Settle();
        viewer.Offset = viewer.Offset.WithY(viewer.Offset.Y + list.ContainerFromItem(target)!.TranslatePoint(default, viewer)!.Value.Y);
        device.Settle();

        device.Fold(Tabletop);

        Assert.InRange(list.ContainerFromItem(target)!.TranslatePoint(default, viewer)!.Value.Y, -1, 1);
    }

    [AvaloniaFact]
    public void Focus_moves_next_door_when_its_pane_hides_and_comes_back_with_it()
    {
        var device = new Device();
        var message = device.ViewModel.Messages[2];
        device.ViewModel.SelectedMessage = message;
        device.Settle();
        var reply = device.ReplyBox;
        reply.Text = "See you at ten";
        reply.Focus();
        reply.SelectionStart = 4;
        reply.SelectionEnd = 7;
        device.Settle();
        Assert.Same(reply, device.Focused);

        // Closed: the cover display shows the list, so the focus lands on the message being answered.
        device.Fold(Closed);
        var landed = Assert.IsType<ListBoxItem>(device.Focused);
        Assert.Same(message, landed.DataContext);

        // Open again: back in the reply, with its caret and selection.
        device.Fold(Open);
        Assert.Same(reply, device.Focused);
        Assert.Equal(4, reply.SelectionStart);
        Assert.Equal(7, reply.SelectionEnd);
    }

    [AvaloniaFact]
    public void Back_shows_only_with_one_pane_and_returns_to_the_message_in_the_list()
    {
        var device = new Device();
        var message = device.ViewModel.Messages[1];
        device.ViewModel.SelectedMessage = message;
        device.Settle();
        Assert.False(device.BackButton.IsVisible);

        // On the cover display, Enter on a message opens it in place of the list.
        device.Fold(Closed);
        device.List.ContainerFromItem(message)!.Focus();
        device.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        device.Settle();
        Assert.Equal(TwoPaneViewPriority.Pane2, device.Panes.PanePriority);
        Assert.True(device.BackButton.IsEffectivelyVisible);

        device.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        device.Settle();
        Assert.Equal(TwoPaneViewPriority.Pane1, device.Panes.PanePriority);
        Assert.Same(message, Assert.IsType<ListBoxItem>(device.Focused).DataContext);
    }

    [AvaloniaFact]
    public void Escape_and_a_swipe_from_the_edge_go_back_too()
    {
        var device = new Device();
        var message = device.ViewModel.Messages[1];
        device.Fold(Closed);

        void OpenOnCover()
        {
            device.List.ContainerFromItem(message)!.Focus();
            device.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            // Let the message finish sliding in, as a person would before swiping.
            device.Wait(0.5);
            Assert.Equal(TwoPaneViewPriority.Pane2, device.Panes.PanePriority);
        }

        OpenOnCover();
        device.ReplyBox.Focus();
        device.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        device.Settle();
        Assert.Equal(TwoPaneViewPriority.Pane1, device.Panes.PanePriority);

        // A short swipe from the edge springs back; a long one goes back.
        OpenOnCover();
        device.Swipe(from: 6, to: 60);
        Assert.Equal(TwoPaneViewPriority.Pane2, device.Panes.PanePriority);
        device.Swipe(from: 6, to: 300);
        Assert.Equal(TwoPaneViewPriority.Pane1, device.Panes.PanePriority);

        // Away from the edge, a drag isn't a swipe back.
        OpenOnCover();
        device.Swipe(from: 120, to: 420);
        Assert.Equal(TwoPaneViewPriority.Pane2, device.Panes.PanePriority);
    }

    [AvaloniaFact]
    public void The_search_survives_the_fold_and_never_closes_the_open_message()
    {
        var device = new Device();
        var open = device.ViewModel.Messages[6];
        device.ViewModel.SelectedMessage = open;
        device.Settle();
        var search = device.SearchBox;
        search.Focus();
        search.Text = "fold";
        search.CaretIndex = 2;
        device.Settle();
        var found = device.List.ItemCount;
        Assert.InRange(found, 1, device.ViewModel.Messages.Count - 1);

        // The open message doesn't match: it's out of the list, but still open.
        Assert.DoesNotContain(open, device.ViewModel.VisibleMessages);
        Assert.Same(open, device.ViewModel.SelectedMessage);

        device.Fold(Closed);
        device.Fold(Open);
        Assert.Equal("fold", search.Text);
        Assert.Equal(found, device.List.ItemCount);
        Assert.Same(search, device.Focused);
        Assert.Equal(2, search.CaretIndex);

        // Escape clears the search, and the open message is selected in the list again.
        device.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        device.Settle();
        Assert.Equal(string.Empty, search.Text);
        Assert.Same(open, device.List.SelectedItem);
    }

    // The app in a window sized like the iPhone Duo's displays, with a fold the test sets.
    private sealed class Device
    {
        private readonly SimulatedFoldStateProvider _fold = new(Open);

        public Device()
        {
            View = new MainView();
            Window = new Window { Width = 951, Height = 669, Content = View };
            global::Nova.Avalonia.Foldable.Fold.SetProvider(Window, _fold);
            Window.Show();
            Window.Activate();
            Settle();
        }

        public Window Window { get; }

        public MainView View { get; }

        public global::Inbox.InboxViewModel ViewModel => View.ViewModel;

        public TwoPaneView Panes => Find<TwoPaneView>("Panes");

        public ListBox List => Find<ListBox>("List");

        public TextBox ReplyBox => Find<TextBox>("ReplyBox");

        public Button BackButton => Find<Button>("BackButton");

        public TextBox SearchBox => Find<TextBox>("SearchBox");

        public ScrollViewer Reader => Find<ScrollViewer>("Reader");

        public TextBlock Body => Find<TextBlock>("Body");

        public IInputElement? Focused => Window.FocusManager?.GetFocusedElement();

        // The cover display is 466 by 678 points; the inner display 951 by 669.
        public void Fold(FoldState state)
        {
            var closed = state.Posture == DevicePosture.Closed;
            Window.Width = closed ? 466 : 951;
            Window.Height = closed ? 678 : 669;
            _fold.Current = state;
            Settle();
        }

        // A horizontal drag across the middle of the screen, with the mouse.
        public void Swipe(double from, double to)
        {
            var y = Window.Height / 2;
            Window.MouseDown(new Point(from, y), MouseButton.Left);
            for (var x = from; x <= to; x += 20)
                Window.MouseMove(new Point(x, y));
            Window.MouseMove(new Point(to, y));
            Window.MouseUp(new Point(to, y), MouseButton.Left);

            // The page springs back, or the list comes in.
            Wait(0.4);
        }

        // Real time passes, for animations: the render timer is driven by hand.
        public void Wait(double seconds)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                System.Threading.Thread.Sleep(16);
            }

            Settle();
        }

        public void Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Window.UpdateLayout();
            }

            Dispatcher.UIThread.RunJobs();
        }

        private T Find<T>(string name) where T : Control =>
            View.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    }
}
