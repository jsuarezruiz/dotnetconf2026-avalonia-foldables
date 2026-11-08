using Nova.Avalonia.Foldable;

namespace Inkfold.Painting;

/// <summary>
/// What the fold just did to an armed press.
/// </summary>
public enum PressStep
{
    None,

    /// <summary>
    /// The device closed: the halves of the page touch.
    /// </summary>
    Closed,

    /// <summary>
    /// The device opened again after closing: time to show the print.
    /// </summary>
    Opened,
}

/// <summary>
/// Watches the fold for an armed press: the device closing, then opening again.
/// </summary>
/// <remarks>
/// The posture decides, and the hinge angle only fills in where the posture can't tell, because many
/// Android foldables have no hinge sensor. Without one, Android reports a cover display as
/// <see cref="DevicePosture.Unknown"/>, never <see cref="DevicePosture.Closed"/>, so that counts as closed.
/// Opening into book posture counts as open, so the print shows without laying the device flat.
/// </remarks>
public sealed class PressTrigger
{
    /// <summary>
    /// A hinge closed below this angle counts as closed.
    /// </summary>
    public const double ClosedAngle = 25;

    /// <summary>
    /// After closing, a hinge opened past this angle counts as open: far enough to see the page.
    /// </summary>
    public const double OpenAngle = 60;

    /// <summary>
    /// Gets whether the device has closed since the press was armed.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Starts watching from an open device, as the press is armed.
    /// </summary>
    public void Arm() => IsClosed = false;

    /// <summary>
    /// Feeds the latest fold state, as <see cref="Fold"/> publishes it.
    /// </summary>
    public PressStep Update(FoldState state)
    {
        if (!IsClosed)
        {
            if (!ReadsClosed(state))
                return PressStep.None;

            IsClosed = true;
            return PressStep.Closed;
        }

        if (!ReadsOpen(state))
            return PressStep.None;

        IsClosed = false;
        return PressStep.Opened;
    }

    private static bool ReadsClosed(FoldState state) =>
        state.Posture == DevicePosture.Closed
        || state.HingeAngle is < ClosedAngle
        || (state.IsFoldable && state.Posture == DevicePosture.Unknown && state.HingeAngle is null);

    private static bool ReadsOpen(FoldState state) =>
        state.Posture == DevicePosture.Flat
        || state.HingeAngle is >= OpenAngle
        || (state.Posture == DevicePosture.HalfOpened && state.HingeAngle is null);
}
