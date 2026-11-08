using Avalonia;
using Inkfold.Painting;
using Nova.Avalonia.Foldable;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.Inkfold;

public class PressTriggerTests
{
    private static readonly Rect Fold = new(455.5, 0, 40, 669);

    private readonly PressTrigger _press = new();

    public PressTriggerTests() => _press.Arm();

    [Fact]
    public void Closing_then_opening_into_book_posture_prints()
    {
        // The iPhone Duo's Device Hub presets: open, closed, half open at about 128 degrees.
        Assert.Equal(PressStep.None, _press.Update(Duo(DevicePosture.Flat, 180)));
        Assert.Equal(PressStep.Closed, _press.Update(Duo(DevicePosture.Closed, 0)));
        Assert.Equal(PressStep.Opened, _press.Update(Duo(DevicePosture.HalfOpened, 128)));
    }

    [Fact]
    public void Without_a_hinge_sensor_the_cover_display_counts_as_closed()
    {
        // Android without a hinge sensor: the cover display reports Unknown, with no angle and no fold.
        var open = new FoldState(true, DevicePosture.Flat, null, Fold, false);
        var cover = new FoldState(true, DevicePosture.Unknown, null, null, false);

        Assert.Equal(PressStep.None, _press.Update(open));
        Assert.Equal(PressStep.Closed, _press.Update(cover));
        Assert.Equal(PressStep.Opened, _press.Update(open));
    }

    [Fact]
    public void Folding_without_closing_does_not_print()
    {
        Assert.Equal(PressStep.None, _press.Update(Duo(DevicePosture.Flat, 180)));
        Assert.Equal(PressStep.None, _press.Update(Duo(DevicePosture.HalfOpened, 90)));
        Assert.Equal(PressStep.None, _press.Update(Duo(DevicePosture.HalfOpened, 30)));
        Assert.Equal(PressStep.None, _press.Update(Duo(DevicePosture.Flat, 180)));
    }

    [Fact]
    public void A_barely_open_hinge_does_not_print_yet()
    {
        Assert.Equal(PressStep.Closed, _press.Update(Duo(DevicePosture.HalfOpened, 10)));
        Assert.Equal(PressStep.None, _press.Update(Duo(DevicePosture.HalfOpened, 40)));
        Assert.Equal(PressStep.Opened, _press.Update(Duo(DevicePosture.HalfOpened, 70)));
    }

    [Fact]
    public void Closed_is_reported_once()
    {
        Assert.Equal(PressStep.Closed, _press.Update(Duo(DevicePosture.Closed, 0)));
        Assert.Equal(PressStep.None, _press.Update(Duo(DevicePosture.Closed, 2)));
        Assert.True(_press.IsClosed);

        _press.Arm();
        Assert.False(_press.IsClosed);
    }

    private static FoldState Duo(DevicePosture posture, double angle) =>
        new(true, posture, angle, posture == DevicePosture.Closed ? null : Fold, posture == DevicePosture.HalfOpened);
}
