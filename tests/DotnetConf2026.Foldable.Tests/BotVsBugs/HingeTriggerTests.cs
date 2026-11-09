using System.Collections.Generic;
using Avalonia;
using BotVsBugs.Game;
using Nova.Avalonia.Foldable;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.BotVsBugs;

public class HingeTriggerTests
{
    // The iPhone Duo's inner display, held like a book.
    private static readonly Rect Fold = new(455.5, 0, 40, 669);

    private readonly HingeTrigger _trigger = new();
    private readonly List<double> _shots = [];

    public HingeTriggerTests() => _trigger.Fired += _shots.Add;

    [Fact]
    public void Folding_and_opening_fires_once_with_the_deepest_pull()
    {
        _trigger.Update(Flat());
        _trigger.Ready();

        // Device Hub's half open preset arms it; folding deeper pulls further; opening fires.
        _trigger.Update(HalfOpen(128));
        Assert.True(_trigger.IsArmed);
        _trigger.Update(HalfOpen(100));
        _trigger.Update(HalfOpen(115));

        // Opening on the way to firing doesn't ease the band: the gauge shows the shot it will fire.
        Assert.Equal((172 - 100) / 90d, _trigger.Pull, 3);
        _trigger.Update(Flat());

        var power = Assert.Single(_shots);
        Assert.Equal((172 - 100) / 90d, power, 3);
        Assert.False(_trigger.IsArmed);
    }

    [Fact]
    public void A_shallow_fold_fires_when_the_hinge_opens_back_to_rest()
    {
        _trigger.Update(Flat());
        _trigger.Ready();

        // Just deep enough to arm, too shallow to open another 22° before flat.
        _trigger.Update(HalfOpen(158.4));
        Assert.True(_trigger.IsArmed);
        _trigger.Update(Flat());

        var power = Assert.Single(_shots);
        Assert.Equal((172 - 158.4) / 90d, power, 3);
    }

    [Fact]
    public void Opening_from_closed_never_fires()
    {
        // Ready while open flat, then closed: the hinge left, and opening starts from where it comes back.
        _trigger.Update(Flat());
        _trigger.Ready();
        _trigger.Update(new FoldState(true, DevicePosture.Closed, 0, null, false));
        _trigger.Update(HalfOpen(90));
        _trigger.Update(Flat());

        Assert.Empty(_shots);
    }

    [Fact]
    public void Without_a_hinge_angle_the_trigger_stays_off()
    {
        _trigger.Ready();
        _trigger.Update(new FoldState(true, DevicePosture.HalfOpened, null, Fold, true));
        _trigger.Update(new FoldState(true, DevicePosture.Flat, null, Fold, false));

        Assert.False(_trigger.IsActive);
        Assert.Empty(_shots);
    }

    [Fact]
    public void A_wobble_of_the_hinge_does_not_arm()
    {
        // Lying flat, a wobble pulls too little to count.
        _trigger.Update(Flat());
        _trigger.Ready();
        _trigger.Update(HalfOpen(175));
        _trigger.Update(Flat());
        Assert.False(_trigger.IsArmed);

        // Standing in book, the pull is already deep, so only the travel since the bot got ready counts.
        _trigger.Stop();
        _trigger.Update(HalfOpen(100));
        _trigger.Ready();
        _trigger.Update(HalfOpen(95));
        _trigger.Update(HalfOpen(100));
        Assert.False(_trigger.IsArmed);
        Assert.Empty(_shots);
    }

    private static FoldState Flat() => new(true, DevicePosture.Flat, 180, Fold, false);

    private static FoldState HalfOpen(double angle) => new(true, DevicePosture.HalfOpened, angle, Fold, true);
}
