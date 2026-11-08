using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Inbox.Models;

namespace Inbox.Views;

/// <summary>
/// The list of messages, and the search above it.
/// </summary>
public partial class InboxView : UserControl
{
    public InboxView()
    {
        InitializeComponent();

        // Choosing a row shows its message beside the list; with one pane, opening it takes the list's place.
        List.SelectionChanged += (_, _) =>
        {
            if (List.SelectedItem is Message message)
                Selected?.Invoke(this, message);
        };

        // A tap or Enter opens a message, even the one already selected, as after going back.
        List.AddHandler(TappedEvent, (_, e) => OpenFrom(e.Source));
        List.AddHandler(KeyDownEvent, (_, e) =>
        {
            // The row with the focus, which may not be the selected one yet.
            if (e.Key == Key.Enter
                && ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext ?? List.SelectedItem) is Message message)
            {
                Opened?.Invoke(this, message);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        // From the search, Down goes to the results and Escape clears it.
        SearchBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Down && List.ItemCount > 0)
            {
                List.ScrollIntoView(0);
                List.ContainerFromIndex(0)?.Focus(NavigationMethod.Directional);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Occurs when the person picks a message in the list.
    /// </summary>
    public event EventHandler<Message>? Selected;

    /// <summary>
    /// Occurs when the person opens a message.
    /// </summary>
    public event EventHandler<Message>? Opened;

    private void OpenFrom(object? source)
    {
        if (source is Control control && control.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is Message message)
            Opened?.Invoke(this, message);
    }
}
