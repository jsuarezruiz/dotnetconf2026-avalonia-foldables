using System;
using System.Globalization;
using Avalonia.Controls;
using BotVsBugs.Game;

namespace BotVsBugs.Controls;

/// <summary>
/// The bottom pane in tabletop: shows the hinge pulling the sling, and sets the aim.
/// </summary>
public partial class ControlDeck : UserControl
{
    public ControlDeck()
    {
        InitializeComponent();
        Dial.AimChanged += (_, _) => AimChanged?.Invoke(this, EventArgs.Empty);
        PauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets or sets the aim the dial shows, in radians.
    /// </summary>
    public double Aim
    {
        get => Dial.Aim;
        set => Dial.Aim = value;
    }

    /// <summary>
    /// Occurs when the dial is dragged to a new aim.
    /// </summary>
    public event EventHandler? AimChanged;

    public event EventHandler? PauseRequested;

    public void ShowWorld(GameWorld? world)
    {
        if (world is null)
            return;

        LevelText.Text = $"{world.Index + 1}. {world.Level.Name}";
        ScoreText.Text = world.Score.ToString("N0", CultureInfo.CurrentCulture);
        StatusText.Text = world.Phase switch
        {
            GamePhase.Flying => "Bot away!",
            GamePhase.Won => "All bugs squashed.",
            GamePhase.Lost => "The bugs got away.",
            _ => $"{world.BugsLeft} {(world.BugsLeft == 1 ? "bug" : "bugs")} left · {world.BotsLeft} {(world.BotsLeft == 1 ? "bot" : "bots")} to go",
        };
    }

    public void ShowHinge(HingeTrigger hinge)
    {
        Gauge.Angle = hinge.Angle;
        Gauge.Pull = hinge.IsArmed ? hinge.Pull : 0d;
        Gauge.IsArmed = hinge.IsArmed;
    }
}
