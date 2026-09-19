using System.Numerics;
using Avalonia;
using Avalonia.Input;

namespace Pso2ShapeStudio.App.Rendering;

// Orbit camera state and pointer interaction. Pure math and input - the
// only rendering side effect is requesting the next frame.
public sealed partial class ModelViewport
{
    private const float DefaultYaw = -0.55f;
    private const float DefaultPitch = 0.08f;
    private const float DefaultDistance = 2.6f;
    private static readonly Vector3 DefaultFocus = new(0, 1.0f, 0);

    private Vector3 _focus = DefaultFocus;
    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private float _distance = DefaultDistance;
    private float _modelYaw;
    private Point _lastPointer;
    private PointerMode _pointerMode;

    public bool BeginCameraInteraction(
        Point position,
        PointerPointProperties properties,
        KeyModifiers modifiers)
    {
        if (properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed ||
            properties.IsMiddleButtonPressed)
        {
            ResetCamera();
            return true;
        }

        var leftPressed =
            properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed ||
            properties.IsLeftButtonPressed;
        var rightPressed =
            properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed ||
            properties.IsRightButtonPressed;
        _pointerMode = modifiers.HasFlag(KeyModifiers.Control) && (leftPressed || rightPressed)
            ? PointerMode.VerticalMove
            : rightPressed
                ? PointerMode.ModelRotate
                : leftPressed
                    ? PointerMode.CameraRotate
                    : PointerMode.None;
        if (_pointerMode != PointerMode.None)
        {
            _lastPointer = position;
            ReportCameraState();
        }

        return _pointerMode != PointerMode.None;
    }

    public bool UpdateCameraInteraction(Point current)
    {
        if (_pointerMode == PointerMode.None)
        {
            return false;
        }

        var dx = (float)(current.X - _lastPointer.X);
        var dy = (float)(current.Y - _lastPointer.Y);
        _lastPointer = current;

        if (_pointerMode == PointerMode.CameraRotate)
        {
            _yaw -= dx * 0.008f;
            _pitch = Math.Clamp(_pitch + dy * 0.008f, -1.45f, 1.45f);
        }
        else if (_pointerMode == PointerMode.ModelRotate)
        {
            _modelYaw = MathF.IEEERemainder(_modelYaw + dx * 0.008f, MathF.Tau);
        }
        else if (_pointerMode == PointerMode.VerticalMove)
        {
            _focus.Y += dy * (_distance * 0.0015f);
        }

        RequestNextFrameRendering();
        ReportCameraState();
        return true;
    }

    public bool EndCameraInteraction()
    {
        var wasActive = _pointerMode != PointerMode.None;
        _pointerMode = PointerMode.None;
        return wasActive;
    }

    public void ZoomCamera(double delta)
    {
        _distance = Math.Clamp(_distance * MathF.Pow(0.88f, (float)delta), 0.25f, 20f);
        RequestNextFrameRendering();
        ReportCameraState();
    }

    private void ResetCamera()
    {
        _focus = DefaultFocus;
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _distance = DefaultDistance;
        _modelYaw = 0f;
        RequestNextFrameRendering();
        ReportCameraState();
    }

    private void ReportCameraState() => CameraChanged?.Invoke(
        this,
        new ViewportCameraState(
            _yaw,
            _pitch,
            _focus.Y,
            _distance,
            _modelYaw,
            _pointerMode.ToString()));

    private Matrix4x4 BuildViewProjection(float aspect)
    {
        var view = Matrix4x4.CreateLookAt(CameraPosition(), _focus, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            45f * MathF.PI / 180f,
            Math.Max(aspect, 0.01f),
            0.01f,
            100f);
        return view * projection;
    }

    private Vector3 CameraPosition()
    {
        var horizontal = MathF.Cos(_pitch) * _distance;
        return _focus + new Vector3(
            MathF.Sin(_yaw) * horizontal,
            MathF.Sin(_pitch) * _distance,
            MathF.Cos(_yaw) * horizontal);
    }

    private enum PointerMode
    {
        None,
        CameraRotate,
        ModelRotate,
        VerticalMove,
    }
}
