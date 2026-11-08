using System;
using System.Globalization;
using System.Linq;
using Avalonia.Media;

namespace Inbox.Models;

/// <summary>
/// A message in the inbox.
/// </summary>
public sealed record Message(int Id, string From, string Subject, string Body, DateTime Received)
{
    private static readonly IBrush[] Avatars =
    [
        new SolidColorBrush(Color.Parse("#7C5CFF")),
        new SolidColorBrush(Color.Parse("#E0679A")),
        new SolidColorBrush(Color.Parse("#2FA8A0")),
        new SolidColorBrush(Color.Parse("#E39A3B")),
        new SolidColorBrush(Color.Parse("#4C8DF0")),
        new SolidColorBrush(Color.Parse("#9B6A4A")),
    ];

    public string FirstName => From.Split(' ')[0];

    public string Initials => string.Concat(From.Split(' ').Take(2).Select(word => word[0]));

    // The same color for a sender every time, unlike string.GetHashCode, which changes per run.
    public IBrush Avatar => Avatars[From.Sum(c => c) % Avatars.Length];

    public string Preview => Body.ReplaceLineEndings(" ");

    public string Time
    {
        get
        {
            var days = (DateTime.Today - Received.Date).Days;
            return days switch
            {
                0 => Received.ToString("t", CultureInfo.CurrentCulture),
                1 => "Yesterday",
                < 7 => Received.ToString("dddd", CultureInfo.CurrentCulture),
                _ => Received.ToString("MMM d", CultureInfo.CurrentCulture),
            };
        }
    }

    // The time on its own: in a custom format, "t" is only the first letter of AM or PM.
    public string Date =>
        $"{Received.ToString("dddd, MMM d", CultureInfo.CurrentCulture)} · {Received.ToString("t", CultureInfo.CurrentCulture)}";
}
