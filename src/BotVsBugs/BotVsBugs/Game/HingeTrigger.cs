using System;
using Nova.Avalonia.Foldable;

namespace BotVsBugs.Game;

/// <summary>
/// Turns the hinge into the slingshot: folding pulls the band back, opening it again lets go.
/// </summary>
/// <remarks>
/// Only a fold that starts while a bot waits on the sling arms it, so opening the device from closed,
/// which also swings the hinge open, never fires a shot.
/// </remarks>
public sealed class HingeTrigger
{
    // Flat, less some slack so a device lying open doesn't pull.
    private const double RestAngle = 172d;

    // Degrees of folding for a full pull: from 172° down to 82°.
    private const double FullTravel = 90d;

    // How far the hinge must close, after a bot is ready, to arm the shot.
    private const double ArmTravel = 8d;

    // A shot needs at least this much pull, so small wobbles of the hinge don't fire.
    private const double MinPull = 0.15d;

    // How far the hinge must open from its deepest point to let go.
    private const double ReleaseTravel = 22d;

    private bool _isReady;
    private double _highest;
    private double _deepest;

    /// <summary>
    /// Gets whether the device reports its hinge, open far enough to play on the inner display.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets whether a fold has armed the shot; opening the hinge now fires it.
    /// </summary>
    public bool IsArmed { get; private set; }

    /// <summary>
    /// Gets how far the band is pulled back, from 0 to 1. Once armed, it holds the deepest fold.
    /// </summary>
    public double Pull { get; private set; }

    /// <summary>
    /// Gets the latest hinge angle in degrees, or null without one.
    /// </summary>
    public double? Angle { get; private set; }

    /// <summary>
    /// Occurs when the hinge lets go, with the shot's power from 0 to 1: the deepest pull reached.
    /// </summary>
    public event Action<double>? Fired;

    /// <summary>
    /// Starts watching for a fold: a bot is on the sling. The hinge's current angle is the starting point.
    /// </summary>
    public void Ready()
    {
        _isReady = true;
        IsArmed = false;
        _highest = _deepest = Angle ?? 0d;
    }

    /// <summary>
    /// Stops watching: the bot is in the air, the level is over, or the game is paused.
    /// </summary>
    public void Stop()
    {
        _isReady = false;
        IsArmed = false;
    }

    /// <summary>
    /// Feeds the latest fold state, as <see cref="Fold"/> publishes it.
    /// </summary>
    public void Update(FoldState state)
    {
        var wasActive = IsActive;
        IsActive = state.IsFoldable
            && state.HingeAngle is not null
            && state.Posture is DevicePosture.HalfOpened or DevicePosture.Flat;

        if (!IsActive || state.HingeAngle is not { } angle)
        {
            Angle = null;
            Pull = 0d;
            IsArmed = false;
            return;
        }

        Angle = angle;
        Pull = Math.Clamp((RestAngle - angle) / FullTravel, 0d, 1d);

        if (!_isReady)
            return;

        // Coming back from the cover display, or a hinge that just started reporting: start from here.
        if (!wasActive)
        {
            _highest = _deepest = angle;
            IsArmed = false;
            return;
        }

        if (!IsArmed)
        {
            _highest = Math.Max(_highest, angle);
            if (_highest - angle >= ArmTravel && Pull >= MinPull)
            {
                IsArmed = true;
                _deepest = angle;
            }

            return;
        }

        // Armed, the band stays at the deepest fold until it lets go, so what shows is what fires.
        _deepest = Math.Min(_deepest, angle);
        Pull = Math.Clamp((RestAngle - _deepest) / FullTravel, 0d, 1d);

        // Opening by the release travel lets go; so does opening back to rest, which a shallow fold needs:
        // armed at 158°, it couldn't open another 22°.
        if (angle - _deepest < ReleaseTravel && angle < RestAngle)
            return;

        var power = Pull;
        Stop();
        Fired?.Invoke(power);
    }
}
