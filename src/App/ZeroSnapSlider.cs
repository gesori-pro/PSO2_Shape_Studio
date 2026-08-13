using Avalonia.Controls;
using Avalonia.Input;

namespace Pso2ShapeStudio.App;

/// <summary>
/// A slider with a small zero dead zone during pointer dragging. Values loaded
/// from files or entered numerically are left untouched.
/// </summary>
public sealed class ZeroSnapSlider : Slider
{
    private bool _pointerAdjusting;
    private bool _snapping;

    public ZeroSnapSlider()
    {
        ValueChanged += (_, _) => SnapPointerValueToZero();
    }

    public double ZeroSnapThreshold { get; set; } = 1.0;

    // Avalonia themes are keyed by the concrete control type. Without this,
    // the subclass keeps its layout slot and value behavior but receives no
    // Slider template, so the rotation track and thumb are invisible.
    protected override Type StyleKeyOverride => typeof(Slider);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pointerAdjusting = true;
        base.OnPointerPressed(e);
        SnapPointerValueToZero();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        SnapPointerValueToZero();
        _pointerAdjusting = false;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        SnapPointerValueToZero();
        _pointerAdjusting = false;
        base.OnPointerCaptureLost(e);
    }

    private void SnapPointerValueToZero()
    {
        if (!_pointerAdjusting || _snapping || Value == 0 ||
            Math.Abs(Value) > ZeroSnapThreshold)
        {
            return;
        }

        _snapping = true;
        try
        {
            SetCurrentValue(ValueProperty, 0d);
        }
        finally
        {
            _snapping = false;
        }
    }
}
