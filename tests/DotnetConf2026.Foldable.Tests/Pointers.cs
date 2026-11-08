using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace DotnetConf2026.Foldable.Tests;

// Pointer events with a pointer of the test's own, so a test can have two fingers, or a pen and a palm,
// on a control at once: the headless platform's helpers only have the one mouse.
internal sealed class Pointers(Window window, Control target)
{
    private ulong _time = 1000;

    public void Down(IPointer pointer, Point at) => Raise(new PointerPressedEventArgs(
        target, pointer, window, at, Tick(), Properties(PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));

    public void Move(IPointer pointer, Point at) => Raise(new PointerEventArgs(
        InputElement.PointerMovedEvent, target, pointer, window, at, Tick(), Properties(PointerUpdateKind.Other), KeyModifiers.None));

    public void Up(IPointer pointer, Point at) => Raise(new PointerReleasedEventArgs(
        target, pointer, window, at, Tick(), Properties(PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    private static PointerPointProperties Properties(PointerUpdateKind kind) =>
        new(kind == PointerUpdateKind.LeftButtonReleased ? RawInputModifiers.None : RawInputModifiers.LeftMouseButton, kind);

    private ulong Tick() => _time += 16;

    private void Raise(PointerEventArgs e)
    {
        (e.Pointer.Captured as Control ?? target).RaiseEvent(e);
        Dispatcher.UIThread.RunJobs();
    }
}
