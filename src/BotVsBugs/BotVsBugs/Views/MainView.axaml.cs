using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BotVsBugs.Game;
using Nova.Avalonia.Foldable;

namespace BotVsBugs.Views;

/// <summary>
/// The app: the game with its HUD, the tabletop deck, and the menus over them.
/// </summary>
public partial class MainView : UserControl
{
    private static readonly TimeSpan HintTime = TimeSpan.FromSeconds(7);
    private static readonly Thickness HudMargin = new(12);

    private readonly DispatcherTimer _hintTimer = new() { Interval = HintTime };
    private GameWorld? _world;
    private TopLevel? _topLevel;

    public MainView()
    {
        InitializeComponent();

        PauseButton.Click += (_, _) => Pause();
        Deck.PauseRequested += (_, _) => Pause();
        Deck.AimChanged += (_, _) => Game.AimAngle = Deck.Aim;
        Game.AimChanged += (_, _) => Deck.Aim = Game.AimAngle;
        Game.HingeChanged += (_, _) => ShowHinge();
        PlayPanes.ModeChanged += (_, _) => ShowHinge();
        _hintTimer.Tick += (_, _) => HideHint();

        Load(Progress.NextLevel);
        ShowTitle();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // A game takes the whole screen: no system bars, and the world drawn around the camera.
        // ApplySafeArea keeps the HUD, deck and menus clear of what remains.
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel?.InsetsManager is { } insets)
        {
            insets.DisplayEdgeToEdgePreference = true;
            insets.IsSystemBarVisible = false;
            insets.SafeAreaChanged += OnSafeAreaChanged;
        }

        ApplySafeArea();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel?.InsetsManager is { } insets)
            insets.SafeAreaChanged -= OnSafeAreaChanged;
        _topLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && !MenuLayer.IsVisible)
        {
            Pause();
            e.Handled = true;
        }
    }

    private void OnSafeAreaChanged(object? sender, EventArgs e) => ApplySafeArea();

    // The game draws edge to edge; the HUD and menus keep clear of the camera and the corners.
    private void ApplySafeArea()
    {
        var safe = _topLevel?.InsetsManager?.SafeAreaPadding ?? default;
        Hud.Margin = new Thickness(
            HudMargin.Left + safe.Left,
            HudMargin.Top + safe.Top,
            HudMargin.Right + safe.Right,
            HudMargin.Bottom);
        Deck.Padding = new Thickness(safe.Left, 0d, safe.Right, safe.Bottom);
        MenuPanes.Margin = safe;
    }

    // Builds the level and shows it, frozen until it's played.
    private void Load(int index)
    {
        if (_world is { } old)
            old.Changed -= OnWorldChanged;

        _world = new GameWorld(Levels.All[index], index);
        _world.Changed += OnWorldChanged;
        Game.IsPaused = true;
        Game.World = _world;
        ShowWorld();
    }

    private void Play(int index)
    {
        Load(index);
        HideMenu();
        Game.IsPaused = false;

        HintText.Text = _world!.Level.Hint;
        HintBox.IsVisible = true;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void Resume()
    {
        HideMenu();
        Game.IsPaused = false;
    }

    private void Pause()
    {
        if (_world is null || _world.Phase is GamePhase.Won or GamePhase.Lost)
            return;

        Game.IsPaused = true;
        ShowPause();
    }

    private void HideHint()
    {
        _hintTimer.Stop();
        HintBox.IsVisible = false;
    }

    private void OnWorldChanged(object? sender, EventArgs e)
    {
        if (sender != _world || _world is null)
            return;

        ShowWorld();

        if (_world.Phase == GamePhase.Flying)
            HideHint();

        if (_world.Phase == GamePhase.Won)
        {
            Progress.Record(_world.Index, _world.Stars);
            ShowResult(_world);
        }
        else if (_world.Phase == GamePhase.Lost)
        {
            ShowResult(_world);
        }
    }

    private void ShowWorld()
    {
        if (_world is not { } world)
            return;

        LevelText.Text = $"{world.Index + 1}. {world.Level.Name}";
        BugsText.Text = world.BugsLeft == 1 ? "1 bug" : $"{world.BugsLeft} bugs";
        ScoreText.Text = world.Score.ToString("N0", CultureInfo.CurrentCulture);
        Deck.ShowWorld(world);
    }

    // On a foldable the hinge chip says what the hinge is doing; in tabletop the deck shows it instead.
    private void ShowHinge()
    {
        var hinge = Game.Hinge;
        Deck.ShowHinge(hinge);

        HingeChip.IsVisible = hinge.IsActive && PlayPanes.Mode != TwoPaneViewMode.Tall;
        if (!HingeChip.IsVisible)
            return;

        HingeText.Text = hinge.IsArmed
            ? $"POWER {hinge.Pull * 100d:0}% · OPEN TO FIRE"
            : $"HINGE {hinge.Angle:0}° · FOLD TO PULL";
    }

    private void ShowMenu(Control hero, Control body)
    {
        MenuHero.Content = hero;
        MenuBody.Content = body;
        MenuLayer.IsVisible = true;
    }

    private void HideMenu()
    {
        MenuLayer.IsVisible = false;
        MenuHero.Content = null;
        MenuBody.Content = null;
    }

    private void ShowTitle()
    {
        var hero = Stack(
            new Image
            {
                Source = Sprites.Get("Bot/bot_jetpack.png"),
                Width = 170,
                Height = 170,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            Text("BOT vs BUGS", "title", 46),
            Text("Squash the bugs before they ship. Drag the dotnet bot back and let go, or fold your phone to pull and snap it open to fire.", "body"),
            Text($"{Progress.TotalStars} of {Levels.All.Count * 3} stars", "eyebrow"));

        var levels = new UniformGrid { Columns = 4 };
        for (var i = 0; i < Levels.All.Count; i++)
            levels.Children.Add(LevelButton(i));

        var next = Progress.NextLevel;
        var body = Stack(
            GameButton($"Play level {next + 1}", () => Play(next)),
            Text("LEVELS", "eyebrow"),
            levels);

        ShowMenu(hero, body);
    }

    private void ShowPause()
    {
        if (_world is not { } world)
            return;

        var hero = Stack(
            Text("PAUSED", "eyebrow"),
            Text($"{world.Index + 1}. {world.Level.Name}", "title"),
            Text(world.Level.Hint, "body"));

        var body = Stack(
            GameButton("Resume", Resume),
            GameButton("Restart", () => Play(world.Index), secondary: true),
            GameButton("Levels", ShowTitle, secondary: true));

        ShowMenu(hero, body);
    }

    private void ShowResult(GameWorld world)
    {
        var won = world.Phase == GamePhase.Won;
        var hero = Stack(
            Text(won ? "LEVEL COMPLETE" : "THE BUGS GOT AWAY", "eyebrow"),
            Text($"{world.Index + 1}. {world.Level.Name}", "title"),
            StarRow(world.Stars, 48),
            Text($"Score {world.Score.ToString("N0", CultureInfo.CurrentCulture)}", "title", 26));

        var body = new StackPanel { Spacing = 12 };
        var hasNext = world.Index + 1 < Levels.All.Count;
        if (won && hasNext)
            body.Children.Add(GameButton("Next level", () => Play(world.Index + 1)));
        body.Children.Add(GameButton(won ? "Play again" : "Try again", () => Play(world.Index), secondary: won && hasNext));
        body.Children.Add(GameButton("Levels", ShowTitle, secondary: true));

        ShowMenu(hero, body);
    }

    private Button LevelButton(int index)
    {
        var level = Levels.All[index];
        var button = new Button
        {
            Classes = { "level" },
            IsEnabled = Progress.IsUnlocked(index),
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    Text((index + 1).ToString(CultureInfo.CurrentCulture), "title", 26),
                    Text(level.Name, "body", 12),
                    StarRow(Progress.StarsFor(index), 16),
                },
            },
        };
        AutomationProperties.SetName(button, $"Level {index + 1}, {level.Name}");
        button.Click += (_, _) => Play(index);
        return button;
    }

    private static Button GameButton(string text, Action onClick, bool secondary = false)
    {
        var button = new Button { Content = text, Classes = { "game" } };
        if (secondary)
            button.Classes.Add("secondary");
        button.Click += (_, _) => onClick();
        return button;
    }

    private static StackPanel StarRow(int stars, double size)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = size / 6d,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (var i = 0; i < 3; i++)
        {
            row.Children.Add(new Image
            {
                Source = Sprites.Get("Ui/starGold.png"),
                Width = size,
                Height = size,
                Opacity = i < stars ? 1d : 0.2d,
            });
        }

        return row;
    }

    private static StackPanel Stack(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.AddRange(children);
        return stack;
    }

    private static TextBlock Text(string text, string style, double? size = null)
    {
        var block = new TextBlock
        {
            Text = text,
            Classes = { style },
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (size is { } fontSize)
            block.FontSize = fontSize;
        return block;
    }
}
