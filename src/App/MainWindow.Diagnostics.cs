using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.App;

// Command-line diagnostics: smoke tests, camera telemetry, and the pose
// stress loop. Nothing here runs in a plain interactive session.
public partial class MainWindow : Window
{
    private DispatcherTimer? _stressTimer;
    private int _stressFrame;

    /// <summary>
    /// Wires the hooks behind --camera-diagnostics, --renderer-smoke= and
    /// --scene-smoke=. The smoke variants park the window off-screen, write
    /// a one-line report, and close on their own so CI can assert on it.
    /// </summary>
    private void WireDiagnosticHooks()
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--camera-diagnostics", StringComparer.OrdinalIgnoreCase))
        {
            Viewport.CameraChanged += (_, camera) =>
                StatusText.Text =
                    $"CAMERA mode={camera.Mode} yaw={camera.Yaw:F3} pitch={camera.Pitch:F3} " +
                    $"focusY={camera.FocusY:F3} distance={camera.Distance:F3}";
        }

        var rendererSmoke = arguments.FirstOrDefault(argument =>
            argument.StartsWith("--renderer-smoke=", StringComparison.OrdinalIgnoreCase));
        if (rendererSmoke is not null)
        {
            var reportPath = rendererSmoke["--renderer-smoke=".Length..];
            HideForSmokeTest();
            Viewport.RendererStatusChanged += (_, message) =>
            {
                try
                {
                    File.WriteAllText(reportPath, message);
                }
                finally
                {
                    DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(100));
                }
            };
        }

        var sceneSmoke = arguments.FirstOrDefault(argument =>
            argument.StartsWith("--scene-smoke=", StringComparison.OrdinalIgnoreCase));
        if (sceneSmoke is not null)
        {
            var reportPath = sceneSmoke["--scene-smoke=".Length..];
            var completed = false;
            HideForSmokeTest();

            void CompleteSceneSmoke(string message)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                File.WriteAllText(reportPath, message);
                DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(100));
            }

            Viewport.RendererStatusChanged += (_, message) =>
            {
                if (message.Contains(L(AppText.FailedWord), StringComparison.OrdinalIgnoreCase))
                {
                    CompleteSceneSmoke(message);
                }
            };
            Viewport.StatisticsChanged += (_, statistics) =>
            {
                if (statistics.VertexCount > 0 && statistics.TextureCount > 0)
                {
                    CompleteSceneSmoke(
                        $"scene ready: models={statistics.ModelCount} vertices={statistics.VertexCount} " +
                        $"triangles={statistics.TriangleCount} textures={statistics.TextureCount}");
                }
            };
        }
    }

    private void HideForSmokeTest()
    {
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(-32000, -32000);
    }

    private void StartStressTest()
    {
        _stressTimer?.Stop();
        _stressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var scale = 1.175f + 0.175f * MathF.Sin(_stressFrame++ * 0.08f);
            _profile["waist"] = new ShapeValue(new Vector3(scale), Vector3.Zero, Vector3.Zero);
            RebuildPose();
        });
        _stressTimer.Start();
    }
}
