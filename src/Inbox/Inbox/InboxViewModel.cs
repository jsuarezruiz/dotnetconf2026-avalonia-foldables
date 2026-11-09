using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Inbox.Models;

namespace Inbox;

/// <summary>
/// What the person would be upset to lose when the device folds: the message they opened, the reply
/// they were writing, their search and where they were reading. The panes only hide with the layout, but
/// this would survive them being rebuilt.
/// </summary>
public sealed class InboxViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<int, string> _drafts = [];
    private Message? _selectedMessage;
    private bool _isReading;
    private string _searchText = string.Empty;
    private IReadOnlyList<Message> _visibleMessages = SampleMail.Messages;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<Message> Messages { get; } = SampleMail.Messages;

    /// <summary>
    /// Gets the messages the search finds: all of them, without a search.
    /// </summary>
    public IReadOnlyList<Message> VisibleMessages => _visibleMessages;

    /// <summary>
    /// Gets or sets the search, matched against the sender, the subject and the text.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;

            _searchText = value;
            var words = value.Trim();
            _visibleMessages = words.Length == 0
                ? Messages
                : [.. Messages.Where(m =>
                    m.From.Contains(words, StringComparison.CurrentCultureIgnoreCase)
                    || m.Subject.Contains(words, StringComparison.CurrentCultureIgnoreCase)
                    || m.Body.Contains(words, StringComparison.CurrentCultureIgnoreCase))];
            Raise();
            Raise(nameof(VisibleMessages));
            Raise(nameof(HasResults));
            Raise(nameof(CountText));

            // The open message stays open even when the search hides it; it's selected again once shown.
            Raise(nameof(SelectedMessage));
        }
    }

    public bool HasResults => _visibleMessages.Count > 0;

    public string CountText => _searchText.Trim().Length == 0
        ? $"{Messages.Count} messages"
        : _visibleMessages.Count == 1 ? "1 match" : $"{_visibleMessages.Count} matches";

    public Message? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            if (Equals(_selectedMessage, value))
                return;

            _selectedMessage = value;
            ReadingPosition = 0;
            Raise();
            Raise(nameof(HasSelection));
            Raise(nameof(ReplyDraft));
            Raise(nameof(CanSend));
            Raise(nameof(ReplyHint));
        }
    }

    public bool HasSelection => _selectedMessage is not null;

    /// <summary>
    /// Gets or sets the reply to the open message. Each message keeps its own draft.
    /// </summary>
    public string ReplyDraft
    {
        get => _selectedMessage is { } message && _drafts.TryGetValue(message.Id, out var draft) ? draft : string.Empty;
        set
        {
            if (_selectedMessage is not { } message || value == ReplyDraft)
                return;

            _drafts[message.Id] = value;
            Raise();
            Raise(nameof(CanSend));
        }
    }

    /// <summary>
    /// Gets whether there's a reply to send.
    /// </summary>
    public bool CanSend => !string.IsNullOrWhiteSpace(ReplyDraft);

    public string ReplyHint => _selectedMessage is { } message ? $"Reply to {message.FirstName}…" : string.Empty;

    /// <summary>
    /// Gets or sets whether one pane shows the open message rather than the list.
    /// </summary>
    public bool IsReading
    {
        get => _isReading;
        set
        {
            if (_isReading == value)
                return;

            _isReading = value;
            Raise();
        }
    }

    /// <summary>
    /// Gets or sets where the person is reading the open message: the character at the top of the view,
    /// not a pixel offset. It's here rather than in the view, so it would survive the view being rebuilt.
    /// </summary>
    public int ReadingPosition { get; set; }

    /// <summary>
    /// Opens a message; with one pane on screen, it takes the place of the list.
    /// </summary>
    public void Open(Message message, bool onePane)
    {
        SelectedMessage = message;
        IsReading = onePane;
    }

    public void Back() => IsReading = false;

    // The demo doesn't send mail: sending clears the draft.
    public void Send()
    {
        if (_selectedMessage is { } message && _drafts.Remove(message.Id))
        {
            Raise(nameof(ReplyDraft));
            Raise(nameof(CanSend));
        }
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
