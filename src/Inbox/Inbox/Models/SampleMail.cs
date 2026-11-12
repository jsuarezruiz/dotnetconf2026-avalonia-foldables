using System;
using System.Collections.Generic;

namespace Inbox.Models;

/// <summary>
/// The inbox the demo opens with: enough mail to scroll, and previews that rewrap when a fold changes
/// the list's width.
/// </summary>
public static class SampleMail
{
    public static IReadOnlyList<Message> Messages { get; } = Create();

    private static List<Message> Create()
    {
        // Ages from now, to the minute, so the newest mail is at the top whatever time the demo starts.
        var now = DateTime.Now;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
        var id = 0;
        Message Mail(string from, string subject, double daysAgo, string body) =>
            new(++id, from, subject, body.Trim(), now.AddMinutes(-Math.Round(daysAgo * 24 * 60)));

        return
        [
            Mail("Emma Lindqvist", "Notes for the inbox demo", 0.05, """
                Here are my notes for the inbox demo, in the order I'd show things. It's a long one, so it's a good message to scroll while you fold.

                First, open the phone flat. The inbox is on the left page and this message is on the right one, either side of the fold. Nothing sits on the crease: the list stops before it and the message starts after it.

                Second, start a reply. Type a few words, select one of them, and leave the cursor in the box. This is the moment people lose work in apps that weren't built for foldables, because the app restarts when the screen changes.

                Third, close the phone. The cover display shows the inbox again, and the focus moves to this message in the list, so a keyboard or a screen reader still lands somewhere sensible. Nothing was thrown away: the reply pane is only hidden.

                Fourth, open it again. The reply comes back with its text, its cursor and the word you selected, and the focus goes straight back into it. Say it out loud: a fold is a resize, not a relaunch.

                Fifth, scroll this message down to here and stand the phone up in tabletop. The message moves to the upright half, above the fold, and gets about half as wide again, so every line rewraps and the text gets shorter. A pixel offset would now point at some other paragraph, or at the end. The app remembers the words at the top of the view instead, and brings them back.

                Sixth, and last, open a message on the cover display. It takes the whole screen, with a back button, because there's only room for one pane. Open the phone and the back button goes away, because both panes are on screen again.

                That's the whole demo: the open message, the draft, the focus and the reading position all survive the fold. None of it needs anything special from the platform, just state that lives outside the views, and views that hide rather than go away.
                """),
            Mail("Ana Ortega", "Run-through tomorrow at 10", 0.1, """
                Can we do one full run-through tomorrow at 10? I'd like to time the demos with the real devices on the stage desk, not the laptop.
                I'll bring the clicker and the spare USB-C hub.
                """),
            Mail("Ben Carter", "The fold is just a rectangle", 0.2, """
                Loved that line in the outline. One question from the review: does FoldBounds come in the top level's coordinates on both platforms? If so, say it on the slide, people will try to use it raw.
                """),
            Mail("Chloé Martin", "Stage layout and the document camera", 0.3, """
                The room has a document camera on the left of the stage. If you want to show the phone folding, we can put it there, but it needs to stay flat on the desk for focus.
                Let me know by Thursday so AV can plan the switch.
                """),
            Mail("Dev Patel", "Pixel Fold emulator image", 0.4, """
                The API 34 image works, but remember to set display_features once after a cold boot or there's no fold at all. I wrote the command on a sticky note and put it on your laptop.
                """),
            Mail("Emma Lindqvist", "Re: Inbox demo", 0.5, """
                The inbox is the demo people will relate to most. Everyone has lost a half-written reply because an app restarted. Show the draft surviving the fold, then the scroll position, then the focus.
                Keep it under a minute.
                """),
            Mail("Farah Haddad", "Speaker dinner", 1.1, """
                Dinner is at 8 at the place by the river. Vegetarian options are fine, I checked. Bring a jacket, the terrace gets cold after sunset.
                """),
            Mail("Gus Moreno", "Hinge sensor on older devices", 1.3, """
                Tried the hinge-angle sensor on three older Android foldables. Two report it, one reports nothing at all, so the angle stays null. The app didn't crash, which is the whole point, but maybe mention it in the talk.
                """),
            Mail("Hana Sato", "Photos from the meetup", 1.6, """
                Uploaded the photos from last week's meetup. There's a great one of the tabletop demo, the phone standing on the table with the video on top and the controls below. Feel free to use it.
                """),
            Mail("Ivan Petrov", "Review notes on slide 25", 2.2, """
                The scroll-position slide is good. One thing: ScrollIntoView only makes the item visible, it doesn't pin it to the top. If the demo needs the same message at the top after the fold, set the offset yourself.
                """),
            Mail("Julia Novak", "Travel booked", 2.5, """
                Flights and hotel are booked. You land at 14:05 on the day before the talk, and the hotel is a ten-minute walk from the venue. Confirmation numbers are in the shared folder.
                """),
            Mail("Ana Ortega", "Timing sheet", 3.1, """
                Here's the timing sheet with every slide and demo. The middle section is tight: nine samples in about ten minutes. Maybe pre-record one of them as a fallback in case the emulator is slow to wake up.
                """),
            Mail("Kofi Mensah", "Accessibility pass", 3.4, """
                Ran the sample with a screen reader. When the device closes and the pane with the focused text box hides, focus disappears and the next Tab goes nowhere sensible. Moving focus to the matching item in the list fixes it.
                Happy to pair on it.
                """),
            Mail("Lena Fischer", "Keyboard covers", 3.8, """
                A lot of people use foldables with a keyboard cover in the laptop pose. Worth checking that Tab order and focus still make sense when the layout changes underneath them.
                """),
            Mail("Marco Bianchi", "Re: TwoPaneView on the cover display", 4.2, """
                With the default thresholds, a tall cover display stacks the two panes instead of showing one. For a list and detail app you probably want one pane there, and two only across the fold.
                """),
            Mail("Nadia Rahman", "Badge pickup", 4.6, """
                Speaker badges are at the front desk from 8:00. Bring an ID. The green room opens at 8:30 and there's coffee, though the good coffee is on the second floor.
                """),
            Mail("Oscar Lindgren", "Video for the closing slide", 5.3, """
                Rendered the closing animation in 4K and 1080p. The 4K one is a bit heavy for the venue laptop, so I'd use the 1080p file. Both are in the drive.
                """),
            Mail("Priya Nair", "Questions from the dry run", 5.8, """
                Three questions came up in the dry run. Does it work on Windows with dual-screen devices? Does TwoPaneView animate between modes? And can you test postures in CI? The last one is easy, the headless tests answer it.
                """),
            Mail("Quentin Roux", "Slide template update", 6.4, """
                Updated the template with the new dark background and the fold line down the middle. Title slides keep the text on the left page, so nothing sits on the crease.
                """),
            Mail("Rosa Delgado", "Re: Draft lost after rotating", 8.0, """
                Found it: the activity was recreated on every configuration change, so the text box came back empty. Declaring the configuration changes in MainActivity fixed it. Folding is a resize, not a relaunch.
                """),
            Mail("Sam Okafor", "Workshop room", 9.5, """
                The workshop room has twenty seats and two foldables to share. People can pair up, or use the desktop simulator, which covers the postures well enough for the exercises.
                """),
            Mail("Tomás Silva", "Recording release form", 11.0, """
                Please sign the recording release form before the event. The session will be streamed live and published on the channel a week later.
                """),
            Mail("Uma Krishnan", "Thank you!", 13.0, """
                Thanks for the session at the meetup. The part about not caching screen sizes finally made our tablet layout bug make sense. We were reading the size once at startup.
                """),
            Mail("Victor Hugo Lima", "Sample code license", 16.0, """
                Is the sample code MIT like the library? We'd like to use the inbox sample as a starting point for an internal app and want to keep the notice right.
                """),
            Mail("Wen Zhao", "Conference schedule", 21.0, """
                The schedule is published. Your session is in the main room on the second day, right after the keynote, so expect a full room. Good luck!
                """),
        ];
    }
}
