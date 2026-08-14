using System.Numerics;
using System.Runtime.InteropServices;
using Pso2ShapeStudio.Formats;
using Silk.NET.OpenGL;

namespace Pso2ShapeStudio.App.Rendering;

// The Y=0 reference: a depth-writing translucent surface, the line grid,
// and the highlighted world axes.
public sealed partial class ModelViewport
{
    private uint _floorVertexArray;
    private uint _floorVertexBuffer;
    private uint _floorSurfaceVertexCount;
    private uint _floorGridFirstVertex;
    private uint _floorGridVertexCount;
    private uint _floorAxisFirstVertex;
    private uint _floorAxisVertexCount;

    private bool _floorGuideVisible = true;

    public void SetFloorGuideVisible(bool visible)
    {
        _floorGuideVisible = visible;
        RequestNextFrameRendering();
    }

    private unsafe void CreateFloorGuide(GL api)
    {
        const int halfLineCount = 10;
        const float spacing = 0.25f;
        const float extent = halfLineCount * spacing;
        const float lineLift = 0.001f;
        var vertices = new List<GpuVertex>(6 + halfLineCount * 8 + 4);

        // A lightly tinted surface makes the exact Y=0 intersection boundary
        // readable. It writes depth so geometry below the plane is hidden.
        Add(new Vector3(-extent, 0f, -extent));
        Add(new Vector3(extent, 0f, -extent));
        Add(new Vector3(extent, 0f, extent));
        Add(new Vector3(-extent, 0f, -extent));
        Add(new Vector3(extent, 0f, extent));
        Add(new Vector3(-extent, 0f, extent));
        _floorSurfaceVertexCount = (uint)vertices.Count;

        _floorGridFirstVertex = (uint)vertices.Count;
        for (var line = -halfLineCount; line <= halfLineCount; line++)
        {
            if (line == 0)
            {
                continue;
            }

            var offset = line * spacing;
            Add(new Vector3(-extent, lineLift, offset));
            Add(new Vector3(extent, lineLift, offset));
            Add(new Vector3(offset, lineLift, -extent));
            Add(new Vector3(offset, lineLift, extent));
        }
        _floorGridVertexCount = (uint)vertices.Count - _floorGridFirstVertex;

        _floorAxisFirstVertex = (uint)vertices.Count;
        Add(new Vector3(-extent, lineLift * 2f, 0f));
        Add(new Vector3(extent, lineLift * 2f, 0f));
        Add(new Vector3(0f, lineLift * 2f, -extent));
        Add(new Vector3(0f, lineLift * 2f, extent));
        _floorAxisVertexCount = (uint)vertices.Count - _floorAxisFirstVertex;

        _floorVertexArray = api.GenVertexArray();
        _floorVertexBuffer = api.GenBuffer();
        api.BindVertexArray(_floorVertexArray);
        api.BindBuffer(BufferTargetARB.ArrayBuffer, _floorVertexBuffer);
        var vertexArray = vertices.ToArray();
        fixed (GpuVertex* pointer = vertexArray)
        {
            api.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertexArray.Length * Marshal.SizeOf<GpuVertex>()),
                pointer,
                BufferUsageARB.StaticDraw);
        }

        ConfigureVertexAttributes(api);
        api.BindVertexArray(0);

        void Add(Vector3 position) => vertices.Add(new GpuVertex(
            position,
            Vector3.UnitY,
            Vector2.Zero,
            Vector2.Zero,
            Vector2.Zero,
            new Vector4(1f, 0f, 0f, 0f),
            new Byte4(0, 0, 0, 0)));
    }

    private void DrawFloorSurface(GL api)
    {
        if (!_floorGuideVisible || _floorVertexArray == 0 || _floorSurfaceVertexCount == 0)
        {
            return;
        }

        api.Uniform1(_useSkinningLocation, 0);
        api.Uniform1(_hasTextureLocation, 0);
        api.Uniform1(_hasMaskLocation, 0);
        api.Uniform1(_hasNormalLocation, 0);
        api.Uniform1(_hasMultiLocation, 0);
        api.Uniform4(_colorChannelsLocation, 0f, 0f, 0f, 0f);
        api.Uniform1(_multiplyColorLocation, 0);
        api.Uniform1(_alphaCutoffLocation, 0f);
        api.Uniform1(_blendModeLocation, (int)MaterialBlendMode.AlphaBlend);
        api.DepthMask(true);
        api.Enable(EnableCap.Blend);
        api.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        api.BindVertexArray(_floorVertexArray);

        var lightBackground =
            _background.X * 0.2126f +
            _background.Y * 0.7152f +
            _background.Z * 0.0722f > 0.45f;
        if (lightBackground)
        {
            api.Uniform4(_baseColorLocation, 0.08f, 0.11f, 0.16f, 0.16f);
        }
        else
        {
            api.Uniform4(_baseColorLocation, 0.48f, 0.54f, 0.64f, 0.18f);
        }
        api.DrawArrays(PrimitiveType.Triangles, 0, _floorSurfaceVertexCount);
        api.BindVertexArray(0);
        api.Disable(EnableCap.Blend);
    }

    private void DrawFloorGrid(GL api)
    {
        if (!_floorGuideVisible || _floorVertexArray == 0 || _floorGridVertexCount == 0)
        {
            return;
        }

        api.Uniform1(_useSkinningLocation, 0);
        api.Uniform1(_hasTextureLocation, 0);
        api.Uniform1(_hasMaskLocation, 0);
        api.Uniform1(_hasNormalLocation, 0);
        api.Uniform1(_hasMultiLocation, 0);
        api.Uniform4(_colorChannelsLocation, 0f, 0f, 0f, 0f);
        api.Uniform1(_multiplyColorLocation, 0);
        api.Uniform1(_alphaCutoffLocation, 0f);
        api.Uniform1(_blendModeLocation, (int)MaterialBlendMode.AlphaBlend);
        api.DepthMask(false);
        api.Enable(EnableCap.Blend);
        api.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        api.BindVertexArray(_floorVertexArray);

        var lightBackground =
            _background.X * 0.2126f +
            _background.Y * 0.7152f +
            _background.Z * 0.0722f > 0.45f;
        if (lightBackground)
        {
            api.Uniform4(_baseColorLocation, 0.08f, 0.11f, 0.16f, 0.62f);
        }
        else
        {
            api.Uniform4(_baseColorLocation, 0.56f, 0.63f, 0.74f, 0.68f);
        }
        api.DrawArrays(PrimitiveType.Lines, (int)_floorGridFirstVertex, _floorGridVertexCount);

        // The two center axes are stronger than the ordinary grid so the
        // actual zero crossing remains recognizable at oblique camera angles.
        api.Uniform4(_baseColorLocation, 0.27f, 0.55f, 0.92f, 0.88f);
        api.DrawArrays(PrimitiveType.Lines, (int)_floorAxisFirstVertex, _floorAxisVertexCount);
        api.BindVertexArray(0);
        api.Disable(EnableCap.Blend);
        api.DepthMask(true);
    }

    private void DeleteFloorGuide(GL api)
    {
        if (_floorVertexArray != 0)
        {
            api.DeleteVertexArray(_floorVertexArray);
            _floorVertexArray = 0;
        }

        if (_floorVertexBuffer != 0)
        {
            api.DeleteBuffer(_floorVertexBuffer);
            _floorVertexBuffer = 0;
        }

        _floorSurfaceVertexCount = 0;
        _floorGridFirstVertex = 0;
        _floorGridVertexCount = 0;
        _floorAxisFirstVertex = 0;
        _floorAxisVertexCount = 0;
    }
}
