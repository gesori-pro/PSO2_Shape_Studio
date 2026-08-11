using System.Numerics;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.Formats;
using Silk.NET.OpenGL;

namespace Pso2ShapeStudio.App.Rendering;

public sealed class ModelViewport : OpenGlControlBase
{
    private const int MaximumBones = 256;
    private const float DefaultYaw = -0.55f;
    private const float DefaultPitch = 0.08f;
    private const float DefaultDistance = 2.6f;
    private static readonly Vector3 DefaultFocus = new(0, 1.0f, 0);

    private readonly object _sceneLock = new();
    private readonly List<GpuMesh> _gpuMeshes = [];
    // Keyed by texture instance, not name: two loads of the same file (or an
    // edited ICE reloaded) produce equal names with different pixels, and a
    // name key would hand one model the other's texture.
    private readonly Dictionary<(RenderTexture Texture, bool Srgb), uint> _gpuTextures = new();
    private IReadOnlyList<RenderModel> _pendingModels = [];
    private RenderTextureSet? _pendingSkinTextureT1;
    private RenderTextureSet? _pendingSkinTextureT2;
    private CharacterColorPalette _pendingCharacterColors = CharacterColorPalette.Default;
    private Matrix4x4[] _pendingSkinMatrices = IdentityBones();
    private bool _sceneDirty = true;
    private bool _bonesDirty = true;

    private GL? _gl;
    private uint _program;
    private uint _boneBuffer;
    private uint _floorVertexArray;
    private uint _floorVertexBuffer;
    private uint _floorVertexCount;
    private int _viewProjectionLocation = -1;
    private int _useSkinningLocation = -1;
    private int _lightDirectionLocation = -1;
    private int _cameraPositionLocation = -1;
    private int _baseColorLocation = -1;
    private int _hasTextureLocation = -1;
    private int _diffuseTextureLocation = -1;
    private int _hasMaskLocation = -1;
    private int _maskTextureLocation = -1;
    private int _hasNormalLocation = -1;
    private int _normalTextureLocation = -1;
    private int _hasMultiLocation = -1;
    private int _multiTextureLocation = -1;
    private int _diffuseUvSetLocation = -1;
    private int _maskUvSetLocation = -1;
    private int _normalUvSetLocation = -1;
    private int _multiUvSetLocation = -1;
    private int _color1Location = -1;
    private int _color2Location = -1;
    private int _color3Location = -1;
    private int _color4Location = -1;
    private int _colorChannelsLocation = -1;
    private int _multiplyColorLocation = -1;
    private int _alphaCutoffLocation = -1;
    private int _blendModeLocation = -1;

    private Vector3 _background = new(0.055f, 0.065f, 0.08f);
    private bool _floorGuideVisible = true;
    private Vector3 _focus = DefaultFocus;
    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private float _distance = DefaultDistance;
    private Point _lastPointer;
    private PointerMode _pointerMode;
    private readonly Stopwatch _renderClock = Stopwatch.StartNew();
    private int _framesInWindow;
    private int _modelCount;
    private int _vertexCount;
    private int _triangleCount;
    private int _textureCount;
    private AppLanguage _language = AppLanguage.English;

    public event EventHandler<ViewportStatistics>? StatisticsChanged;

    public event EventHandler<string>? RendererStatusChanged;

    public event EventHandler<ViewportCameraState>? CameraChanged;

    public void SetLanguage(AppLanguage language) => _language = language;

    /// <summary>Viewport clear color, as plain sRGB [0..1] components.</summary>
    public void SetBackgroundColor(Vector3 color)
    {
        _background = Vector3.Clamp(color, Vector3.Zero, Vector3.One);
        RequestNextFrameRendering();
    }

    public void SetFloorGuideVisible(bool visible)
    {
        _floorGuideVisible = visible;
        RequestNextFrameRendering();
    }

    public void SetModels(IEnumerable<RenderModel> models)
    {
        lock (_sceneLock)
        {
            _pendingModels = models.ToArray();
            _sceneDirty = true;
        }

        RequestNextFrameRendering();
    }

    public void SetSkinTextures(RenderTextureSet? type1, RenderTextureSet? type2)
    {
        lock (_sceneLock)
        {
            _pendingSkinTextureT1 = type1;
            _pendingSkinTextureT2 = type2;
            _sceneDirty = true;
        }

        RequestNextFrameRendering();
    }

    public void SetCharacterColors(CharacterColorPalette colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        lock (_sceneLock)
        {
            _pendingCharacterColors = colors;
            _sceneDirty = true;
        }

        RequestNextFrameRendering();
    }

    public void SetSkinMatrices(IReadOnlyList<Matrix4x4> matrices)
    {
        if (matrices.Count > MaximumBones)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matrices), matrices.Count, $"At most {MaximumBones} bones are supported.");
        }

        var buffer = IdentityBones();
        for (var index = 0; index < matrices.Count; index++)
        {
            buffer[index] = matrices[index];
        }

        lock (_sceneLock)
        {
            _pendingSkinMatrices = buffer;
            _bonesDirty = true;
        }

        RequestNextFrameRendering();
    }

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            _program = CreateProgram(_gl, VertexShader, FragmentShader);
            _viewProjectionLocation = _gl.GetUniformLocation(_program, "uViewProjection");
            _useSkinningLocation = _gl.GetUniformLocation(_program, "uUseSkinning");
            _lightDirectionLocation = _gl.GetUniformLocation(_program, "uLightDirection");
            _cameraPositionLocation = _gl.GetUniformLocation(_program, "uCameraPosition");
            _baseColorLocation = _gl.GetUniformLocation(_program, "uBaseColor");
            _hasTextureLocation = _gl.GetUniformLocation(_program, "uHasTexture");
            _diffuseTextureLocation = _gl.GetUniformLocation(_program, "uDiffuseTexture");
            _hasMaskLocation = _gl.GetUniformLocation(_program, "uHasMask");
            _maskTextureLocation = _gl.GetUniformLocation(_program, "uMaskTexture");
            _hasNormalLocation = _gl.GetUniformLocation(_program, "uHasNormal");
            _normalTextureLocation = _gl.GetUniformLocation(_program, "uNormalTexture");
            _hasMultiLocation = _gl.GetUniformLocation(_program, "uHasMulti");
            _multiTextureLocation = _gl.GetUniformLocation(_program, "uMultiTexture");
            _diffuseUvSetLocation = _gl.GetUniformLocation(_program, "uDiffuseUvSet");
            _maskUvSetLocation = _gl.GetUniformLocation(_program, "uMaskUvSet");
            _normalUvSetLocation = _gl.GetUniformLocation(_program, "uNormalUvSet");
            _multiUvSetLocation = _gl.GetUniformLocation(_program, "uMultiUvSet");
            _color1Location = _gl.GetUniformLocation(_program, "uColor1");
            _color2Location = _gl.GetUniformLocation(_program, "uColor2");
            _color3Location = _gl.GetUniformLocation(_program, "uColor3");
            _color4Location = _gl.GetUniformLocation(_program, "uColor4");
            _colorChannelsLocation = _gl.GetUniformLocation(_program, "uColorChannels");
            _multiplyColorLocation = _gl.GetUniformLocation(_program, "uMultiplyColor");
            _alphaCutoffLocation = _gl.GetUniformLocation(_program, "uAlphaCutoff");
            _blendModeLocation = _gl.GetUniformLocation(_program, "uBlendMode");

            var block = _gl.GetUniformBlockIndex(_program, "Bones");
            _gl.UniformBlockBinding(_program, block, 0);
            _boneBuffer = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, _boneBuffer);
            _gl.BufferData(
                BufferTargetARB.UniformBuffer,
                (nuint)(MaximumBones * Marshal.SizeOf<Matrix4x4>()),
                null,
                BufferUsageARB.DynamicDraw);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, 0, _boneBuffer);
            CreateFloorGuide(_gl);

            _gl.Enable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.Disable(EnableCap.CullFace);
            ReportRendererStatus(AppLocalizer.Text(_language, AppText.RendererReady, GlVersion));
            RequestNextFrameRendering();
        }
        catch (Exception exception)
        {
            ReportRendererStatus(AppLocalizer.Text(
                _language, AppText.RendererInitFailed, GlVersion, exception.Message));
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is not null)
        {
            DeleteMeshes(_gl);
            if (_boneBuffer != 0)
            {
                _gl.DeleteBuffer(_boneBuffer);
            }

            DeleteFloorGuide(_gl);

            if (_program != 0)
            {
                _gl.DeleteProgram(_program);
            }

            _gl.Dispose();
            _gl = null;
        }

        base.OnOpenGlDeinit(gl);
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        if (_gl is null)
        {
            return;
        }
        try
        {
            UploadPendingScene(_gl);
            UploadPendingBones(_gl);

            var width = Math.Max(1u, (uint)Math.Round(Bounds.Width));
            var height = Math.Max(1u, (uint)Math.Round(Bounds.Height));
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
            _gl.Viewport(0, 0, width, height);
            _gl.ClearColor(_background.X, _background.Y, _background.Z, 1f);
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

            var viewProjection = BuildViewProjection(width / (float)height);
            var cameraPosition = CameraPosition();
            _gl.UseProgram(_program);
            _gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
            _gl.Uniform3(_lightDirectionLocation, 0.38f, 0.78f, 0.49f);
            _gl.Uniform3(
                _cameraPositionLocation,
                cameraPosition.X,
                cameraPosition.Y,
                cameraPosition.Z);
            _gl.Uniform1(_diffuseTextureLocation, 0);
            _gl.Uniform1(_maskTextureLocation, 1);
            _gl.Uniform1(_normalTextureLocation, 2);
            _gl.Uniform1(_multiTextureLocation, 3);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, 0, _boneBuffer);

            DrawFloorGuide(_gl);
            _gl.Uniform1(_useSkinningLocation, 1);
            _gl.Uniform1(_blendModeLocation, (int)MaterialBlendMode.Opaque);

            // Opaque/cutout materials write depth without blending. Transparent
            // and additive materials follow far-to-near without touching depth.
            // Leaving blending enabled for opaque materials lets tiny non-zero
            // BC alpha values reveal the surface below as single-pixel specks.
            var drawOrder = _gpuMeshes
                .Where(value => !value.IsTransparent)
                .Concat(_gpuMeshes
                    .Where(value => value.IsTransparent)
                    .OrderByDescending(value =>
                        Vector3.DistanceSquared(cameraPosition, value.Center)));
            var depthWriteEnabled = true;
            var blendingEnabled = false;
            var activeBlendMode = MaterialBlendMode.Opaque;
            foreach (var mesh in drawOrder)
            {
                if (mesh.IsTransparent == depthWriteEnabled)
                {
                    depthWriteEnabled = !mesh.IsTransparent;
                    _gl.DepthMask(depthWriteEnabled);
                }

                if (mesh.IsTransparent != blendingEnabled)
                {
                    blendingEnabled = mesh.IsTransparent;
                    if (blendingEnabled)
                    {
                        _gl.Enable(EnableCap.Blend);
                    }
                    else
                    {
                        _gl.Disable(EnableCap.Blend);
                    }
                }

                if (mesh.BlendMode != activeBlendMode)
                {
                    activeBlendMode = mesh.BlendMode;
                    _gl.BlendFunc(
                        BlendingFactor.SrcAlpha,
                        activeBlendMode == MaterialBlendMode.Additive
                            ? BlendingFactor.One
                            : BlendingFactor.OneMinusSrcAlpha);
                }

                _gl.Uniform4(
                    _baseColorLocation,
                    mesh.BaseColor.X,
                    mesh.BaseColor.Y,
                    mesh.BaseColor.Z,
                    mesh.BaseColor.W);
                _gl.Uniform1(_hasTextureLocation, mesh.Texture != 0 ? 1 : 0);
                _gl.Uniform1(_hasMaskLocation, mesh.MaskTexture != 0 ? 1 : 0);
                _gl.Uniform1(_hasNormalLocation, mesh.NormalTexture != 0 ? 1 : 0);
                _gl.Uniform1(_hasMultiLocation, mesh.MultiTexture != 0 ? 1 : 0);
                _gl.Uniform1(_diffuseUvSetLocation, mesh.TextureUvSets.Diffuse);
                _gl.Uniform1(_maskUvSetLocation, mesh.TextureUvSets.Mask);
                _gl.Uniform1(_normalUvSetLocation, mesh.TextureUvSets.Normal);
                _gl.Uniform1(_multiUvSetLocation, mesh.TextureUvSets.Multi);
                _gl.Uniform4(
                    _color1Location,
                    mesh.Color1.X, mesh.Color1.Y, mesh.Color1.Z, mesh.Color1.W);
                _gl.Uniform4(
                    _color2Location,
                    mesh.Color2.X, mesh.Color2.Y, mesh.Color2.Z, mesh.Color2.W);
                _gl.Uniform4(
                    _color3Location,
                    mesh.Color3.X, mesh.Color3.Y, mesh.Color3.Z, mesh.Color3.W);
                _gl.Uniform4(
                    _color4Location,
                    mesh.Color4.X, mesh.Color4.Y, mesh.Color4.Z, mesh.Color4.W);
                _gl.Uniform4(
                    _colorChannelsLocation,
                    mesh.ColorChannels.X,
                    mesh.ColorChannels.Y,
                    mesh.ColorChannels.Z,
                    mesh.ColorChannels.W);
                _gl.Uniform1(_multiplyColorLocation, mesh.MultiplyColor ? 1 : 0);
                _gl.Uniform1(_alphaCutoffLocation, mesh.AlphaCutoff);
                _gl.Uniform1(_blendModeLocation, (int)mesh.BlendMode);
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.Texture);
                _gl.ActiveTexture(TextureUnit.Texture1);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.MaskTexture);
                _gl.ActiveTexture(TextureUnit.Texture2);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.NormalTexture);
                _gl.ActiveTexture(TextureUnit.Texture3);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.MultiTexture);
                _gl.BindVertexArray(mesh.VertexArray);
                _gl.DrawElements(
                    PrimitiveType.Triangles,
                    mesh.IndexCount,
                    DrawElementsType.UnsignedInt,
                    null);
            }

            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.BindVertexArray(0);
            RecordRenderedFrame();
        }
        catch (Exception exception)
        {
            ReportRendererStatus(AppLocalizer.Text(
                _language, AppText.RendererRenderFailed, exception.Message));
        }
    }

    protected override void OnOpenGlLost()
    {
        ReportRendererStatus(AppLocalizer.Text(_language, AppText.RendererContextLost));
        base.OnOpenGlLost();
    }

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

        var isRotationButton =
            properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed or
                PointerUpdateKind.RightButtonPressed ||
            properties.IsLeftButtonPressed ||
            properties.IsRightButtonPressed;
        _pointerMode = isRotationButton
            ? modifiers.HasFlag(KeyModifiers.Control)
                ? PointerMode.VerticalMove
                : PointerMode.Rotate
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

        if (_pointerMode == PointerMode.Rotate)
        {
            _yaw -= dx * 0.008f;
            _pitch = Math.Clamp(_pitch + dy * 0.008f, -1.45f, 1.45f);
        }
        else
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
        RequestNextFrameRendering();
        ReportCameraState();
    }

    private void ReportCameraState() => CameraChanged?.Invoke(
        this,
        new ViewportCameraState(_yaw, _pitch, _focus.Y, _distance, _pointerMode.ToString()));

    private unsafe void UploadPendingScene(GL api)
    {
        IReadOnlyList<RenderModel>? models = null;
        RenderTextureSet? skinTextureT1 = null;
        RenderTextureSet? skinTextureT2 = null;
        CharacterColorPalette? characterColors = null;
        lock (_sceneLock)
        {
            if (_sceneDirty)
            {
                models = _pendingModels;
                skinTextureT1 = _pendingSkinTextureT1;
                skinTextureT2 = _pendingSkinTextureT2;
                characterColors = _pendingCharacterColors;
                _sceneDirty = false;
            }
        }

        if (models is null)
        {
            return;
        }

        DeleteMeshes(api);
        foreach (var model in models)
        {
            foreach (var mesh in model.Meshes)
            {
                var material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.Materials.Count
                    ? model.Materials[mesh.MaterialIndex]
                    : new RenderMaterial(
                        "fallback",
                        new Vector4(0.63f, 0.67f, 0.72f, 1f),
                        null,
                        true,
                        0);
                var materialTextures = new RenderTextureSet(
                    material.DiffuseTexture,
                    material.MaskTexture,
                    material.NormalTexture,
                    material.MultiTexture);
                // Unknown covers classic models and renamed/extracted files;
                // a skin material with no skin at all reads as a bug, so fall
                // back to whichever set is loaded rather than none.
                var selectedSkin = material.UsesSkinTexture
                    ? model.BodyType switch
                    {
                        Pso2BodyType.Type1 => skinTextureT1,
                        Pso2BodyType.Type2 => skinTextureT2,
                        _ => skinTextureT1 ?? skinTextureT2,
                    }
                    : null;
                var textures = selectedSkin is null
                    ? materialTextures
                    : new RenderTextureSet(
                        selectedSkin.Diffuse ?? materialTextures.Diffuse,
                        selectedSkin.Mask ?? materialTextures.Mask,
                        selectedSkin.Normal ?? materialTextures.Normal,
                        selectedSkin.Multi ?? materialTextures.Multi);
                _gpuMeshes.Add(UploadMesh(
                    api,
                    mesh,
                    material,
                    textures,
                    characterColors ?? CharacterColorPalette.Default));
            }
        }

        _modelCount = models.Count;
        _vertexCount = models.Sum(model => model.VertexCount);
        _triangleCount = models.Sum(model => model.TriangleCount);
        _textureCount = models.Sum(model => model.TextureCount);
        var statistics = new ViewportStatistics(
            _modelCount, _vertexCount, _triangleCount, _textureCount, 0);
        Dispatcher.UIThread.Post(
            () => StatisticsChanged?.Invoke(this, statistics),
            DispatcherPriority.Background);
    }

    private void RecordRenderedFrame()
    {
        _framesInWindow++;
        if (_renderClock.Elapsed.TotalSeconds < 0.75)
        {
            return;
        }

        var fps = _framesInWindow / _renderClock.Elapsed.TotalSeconds;
        _framesInWindow = 0;
        _renderClock.Restart();
        var statistics = new ViewportStatistics(
            _modelCount, _vertexCount, _triangleCount, _textureCount, fps);
        Dispatcher.UIThread.Post(
            () => StatisticsChanged?.Invoke(this, statistics),
            DispatcherPriority.Background);
    }

    private unsafe void UploadPendingBones(GL api)
    {
        Matrix4x4[]? matrices = null;
        lock (_sceneLock)
        {
            if (_bonesDirty)
            {
                matrices = _pendingSkinMatrices;
                _bonesDirty = false;
            }
        }

        if (matrices is null)
        {
            return;
        }

        api.BindBuffer(BufferTargetARB.UniformBuffer, _boneBuffer);
        fixed (Matrix4x4* pointer = matrices)
        {
            api.BufferSubData(
                BufferTargetARB.UniformBuffer,
                0,
                (nuint)(matrices.Length * Marshal.SizeOf<Matrix4x4>()),
                pointer);
        }
    }

    private unsafe GpuMesh UploadMesh(
        GL api,
        RenderMesh mesh,
        RenderMaterial material,
        RenderTextureSet textures,
        CharacterColorPalette colors)
    {
        var vertices = new GpuVertex[mesh.VertexCount];
        var uv2 = mesh.GetUvChannel(1);
        var uv3 = mesh.GetUvChannel(2);
        for (var index = 0; index < vertices.Length; index++)
        {
            var paletteIndex = mesh.PaletteIndices[index];
            vertices[index] = new GpuVertex(
                mesh.Positions[index],
                mesh.Normals[index],
                mesh.Uv[index],
                uv2[index],
                uv3[index],
                mesh.Weights[index],
                new Byte4(
                    ResolveBone(mesh.Palette, paletteIndex.X),
                    ResolveBone(mesh.Palette, paletteIndex.Y),
                    ResolveBone(mesh.Palette, paletteIndex.Z),
                    ResolveBone(mesh.Palette, paletteIndex.W)));
        }

        var indices = mesh.Triangles.Select(index => (uint)index).ToArray();
        var vao = api.GenVertexArray();
        var vertexBuffer = api.GenBuffer();
        var indexBuffer = api.GenBuffer();
        api.BindVertexArray(vao);

        api.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
        fixed (GpuVertex* pointer = vertices)
        {
            api.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * Marshal.SizeOf<GpuVertex>()),
                pointer,
                BufferUsageARB.StaticDraw);
        }

        api.BindBuffer(BufferTargetARB.ElementArrayBuffer, indexBuffer);
        fixed (uint* pointer = indices)
        {
            api.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(indices.Length * sizeof(uint)),
                pointer,
                BufferUsageARB.StaticDraw);
        }

        ConfigureVertexAttributes(api);
        api.BindVertexArray(0);

        var texture = textures.Diffuse is null
            ? 0
            : GetOrUploadTexture(api, textures.Diffuse, srgb: true);
        var maskTexture = textures.Mask is null
            ? 0
            : GetOrUploadTexture(api, textures.Mask, srgb: false);
        var normalTexture = textures.Normal is null
            ? 0
            : GetOrUploadTexture(api, textures.Normal, srgb: false);
        var multiTexture = textures.Multi is null
            ? 0
            : GetOrUploadTexture(api, textures.Multi, srgb: false);
        var mapping = material.ColorMapping;
        return new GpuMesh(
            vao,
            vertexBuffer,
            indexBuffer,
            (uint)indices.Length,
            texture,
            maskTexture,
            normalTexture,
            multiTexture,
            material.BaseColor,
            colors[mapping.Red],
            colors[mapping.Green],
            colors[mapping.Blue],
            colors[mapping.Alpha],
            new Vector4(
                mapping.Red == Pso2ColorChannel.Unused ? 0f : 1f,
                mapping.Green == Pso2ColorChannel.Unused ? 0f : 1f,
                mapping.Blue == Pso2ColorChannel.Unused ? 0f : 1f,
                mapping.Alpha == Pso2ColorChannel.Unused ? 0f : 1f),
            material.UsesSkinTexture,
            material.AlphaCutoff / 255f,
            material.TextureUvSets,
            material.BlendMode,
            MeshCenter(mesh));
    }

    private unsafe void CreateFloorGuide(GL api)
    {
        const int halfLineCount = 10;
        const float spacing = 0.25f;
        const float extent = halfLineCount * spacing;
        var vertices = new List<GpuVertex>((halfLineCount * 2 + 1) * 4);
        for (var line = -halfLineCount; line <= halfLineCount; line++)
        {
            var offset = line * spacing;
            Add(new Vector3(-extent, 0f, offset));
            Add(new Vector3(extent, 0f, offset));
            Add(new Vector3(offset, 0f, -extent));
            Add(new Vector3(offset, 0f, extent));
        }

        _floorVertexCount = (uint)vertices.Count;
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

    private void DrawFloorGuide(GL api)
    {
        if (!_floorGuideVisible || _floorVertexArray == 0 || _floorVertexCount == 0)
        {
            return;
        }

        api.Uniform1(_useSkinningLocation, 0);
        api.Uniform4(_baseColorLocation, 0.28f, 0.32f, 0.38f, 0.42f);
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
        api.DrawArrays(PrimitiveType.Lines, 0, _floorVertexCount);
        api.BindVertexArray(0);
        api.Disable(EnableCap.Blend);
        api.DepthMask(true);
    }

    private static unsafe void ConfigureVertexAttributes(GL api)
    {
        var stride = (uint)Marshal.SizeOf<GpuVertex>();
        api.EnableVertexAttribArray(0);
        api.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        api.EnableVertexAttribArray(1);
        api.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
        api.EnableVertexAttribArray(2);
        api.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)24);
        api.EnableVertexAttribArray(3);
        api.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, (void*)32);
        api.EnableVertexAttribArray(4);
        api.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, (void*)40);
        api.EnableVertexAttribArray(5);
        api.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, stride, (void*)48);
        api.EnableVertexAttribArray(6);
        api.VertexAttribIPointer(6, 4, VertexAttribIType.UnsignedByte, stride, (void*)64);
    }

    private static Vector3 MeshCenter(RenderMesh mesh)
    {
        if (mesh.Positions.Length == 0)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        foreach (var position in mesh.Positions)
        {
            sum += position;
        }

        return sum / mesh.Positions.Length;
    }

    private unsafe uint GetOrUploadTexture(GL api, RenderTexture texture, bool srgb)
    {
        var cacheKey = (texture, srgb);
        if (_gpuTextures.TryGetValue(cacheKey, out var existing))
        {
            return existing;
        }

        var handle = api.GenTexture();
        api.BindTexture(TextureTarget.Texture2D, handle);
        api.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        var uploadPixels = TexturePixelRows.ToOpenGl(texture.RgbaPixels, texture.Width, texture.Height);
        fixed (byte* pixels = uploadPixels)
        {
            api.TexImage2D(
                TextureTarget.Texture2D,
                0,
                srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8,
                (uint)texture.Width,
                (uint)texture.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }
        api.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)GLEnum.LinearMipmapLinear);
        api.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)GLEnum.Linear);
        api.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)GLEnum.Repeat);
        api.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)GLEnum.Repeat);
        api.GenerateMipmap(TextureTarget.Texture2D);
        _gpuTextures.Add(cacheKey, handle);
        return handle;
    }

    private static byte ResolveBone(IReadOnlyList<int> palette, byte paletteIndex)
    {
        if (palette.Count == 0)
        {
            return 0;
        }

        if (paletteIndex >= palette.Count)
        {
            throw new InvalidDataException(
                $"Palette index {paletteIndex} exceeds palette length {palette.Count}.");
        }

        return checked((byte)palette[paletteIndex]);
    }

    private void DeleteMeshes(GL api)
    {
        foreach (var mesh in _gpuMeshes)
        {
            api.DeleteVertexArray(mesh.VertexArray);
            api.DeleteBuffer(mesh.VertexBuffer);
            api.DeleteBuffer(mesh.IndexBuffer);
        }

        _gpuMeshes.Clear();
        foreach (var texture in _gpuTextures.Values)
        {
            api.DeleteTexture(texture);
        }
        _gpuTextures.Clear();
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

        _floorVertexCount = 0;
    }

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

    private static unsafe uint CreateProgram(GL api, string vertexSource, string fragmentSource)
    {
        var vertex = CompileShader(api, ShaderType.VertexShader, vertexSource);
        var fragment = CompileShader(api, ShaderType.FragmentShader, fragmentSource);
        var program = api.CreateProgram();
        api.AttachShader(program, vertex);
        api.AttachShader(program, fragment);
        api.LinkProgram(program);
        api.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        var log = api.GetProgramInfoLog(program);
        api.DetachShader(program, vertex);
        api.DetachShader(program, fragment);
        api.DeleteShader(vertex);
        api.DeleteShader(fragment);
        if (linked == 0)
        {
            api.DeleteProgram(program);
            throw new InvalidOperationException($"OpenGL program link failed: {log}");
        }

        return program;
    }

    private static uint CompileShader(GL api, ShaderType type, string source)
    {
        var shader = api.CreateShader(type);
        api.ShaderSource(shader, source);
        api.CompileShader(shader);
        api.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        var log = api.GetShaderInfoLog(shader);
        if (compiled == 0)
        {
            api.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compilation failed: {log}");
        }

        return shader;
    }

    private static Matrix4x4[] IdentityBones()
    {
        return Enumerable.Repeat(Matrix4x4.Identity, MaximumBones).ToArray();
    }

    private void ReportRendererStatus(string message)
    {
        Dispatcher.UIThread.Post(
            () => RendererStatusChanged?.Invoke(this, message),
            DispatcherPriority.Background);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct GpuVertex(
        Vector3 Position,
        Vector3 Normal,
        Vector2 Uv,
        Vector2 Uv2,
        Vector2 Uv3,
        Vector4 Weights,
        Byte4 BoneIndices);

    private readonly record struct GpuMesh(
        uint VertexArray,
        uint VertexBuffer,
        uint IndexBuffer,
        uint IndexCount,
        uint Texture,
        uint MaskTexture,
        uint NormalTexture,
        uint MultiTexture,
        Vector4 BaseColor,
        Vector4 Color1,
        Vector4 Color2,
        Vector4 Color3,
        Vector4 Color4,
        Vector4 ColorChannels,
        bool MultiplyColor,
        float AlphaCutoff,
        RenderTextureUvSets TextureUvSets,
        MaterialBlendMode BlendMode,
        Vector3 Center)
    {
        public bool IsTransparent => BlendMode is
            MaterialBlendMode.AlphaBlend or MaterialBlendMode.Additive;
    }

    private enum PointerMode
    {
        None,
        Rotate,
        VerticalMove,
    }

    private const string VertexShader = """
        #version 300 es
        precision highp float;
        precision highp int;
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv1;
        layout(location = 3) in vec2 aUv2;
        layout(location = 4) in vec2 aUv3;
        layout(location = 5) in vec4 aWeights;
        layout(location = 6) in uvec4 aBones;

        layout(std140) uniform Bones
        {
            mat4 uBones[256];
        };

        uniform mat4 uViewProjection;
        uniform bool uUseSkinning;
        out vec3 vNormal;
        out vec3 vPosition;
        out vec2 vUv1;
        out vec2 vUv2;
        out vec2 vUv3;

        void main()
        {
            mat4 skin = mat4(1.0);
            if (uUseSkinning)
            {
                skin =
                    aWeights.x * uBones[aBones.x] +
                    aWeights.y * uBones[aBones.y] +
                    aWeights.z * uBones[aBones.z] +
                    aWeights.w * uBones[aBones.w];
            }
            vec4 position = skin * vec4(aPosition, 1.0);
            vNormal = normalize(mat3(skin) * aNormal);
            vPosition = position.xyz;
            // Aqua stores PSO2 V downward. DDS rows are uploaded bottom-up,
            // so this is the same conversion used by the Blender importer.
            vUv1 = vec2(aUv1.x, 1.0 - aUv1.y);
            vUv2 = vec2(aUv2.x, 1.0 - aUv2.y);
            vUv3 = vec2(aUv3.x, 1.0 - aUv3.y);
            gl_Position = uViewProjection * position;
        }
        """;

    private const string FragmentShader = """
        #version 300 es
        precision highp float;
        precision highp int;
        in vec3 vNormal;
        in vec3 vPosition;
        in vec2 vUv1;
        in vec2 vUv2;
        in vec2 vUv3;
        uniform vec3 uLightDirection;
        uniform vec3 uCameraPosition;
        uniform vec4 uBaseColor;
        uniform bool uHasTexture;
        uniform sampler2D uDiffuseTexture;
        uniform bool uHasMask;
        uniform sampler2D uMaskTexture;
        uniform bool uHasNormal;
        uniform sampler2D uNormalTexture;
        uniform bool uHasMulti;
        uniform sampler2D uMultiTexture;
        uniform int uDiffuseUvSet;
        uniform int uMaskUvSet;
        uniform int uNormalUvSet;
        uniform int uMultiUvSet;
        uniform vec4 uColor1;
        uniform vec4 uColor2;
        uniform vec4 uColor3;
        uniform vec4 uColor4;
        uniform vec4 uColorChannels;
        uniform bool uMultiplyColor;
        uniform float uAlphaCutoff;
        uniform int uBlendMode;
        out vec4 FragColor;

        vec3 colorize(vec3 inputColor, vec3 target, float factor)
        {
            vec3 result = uMultiplyColor ? inputColor * target : target;
            return mix(inputColor, result, clamp(factor, 0.0, 1.0));
        }

        vec2 selectUv(int setIndex)
        {
            if (setIndex == 1)
            {
                return vUv2;
            }
            if (setIndex == 2)
            {
                return vUv3;
            }
            return vUv1;
        }

        vec3 mappedNormal(vec3 geometryNormal)
        {
            vec2 normalUv = selectUv(uNormalUvSet);
            vec3 tangentNormal = texture(uNormalTexture, normalUv).xyz * 2.0 - 1.0;
            vec3 positionDx = dFdx(vPosition);
            vec3 positionDy = dFdy(vPosition);
            vec2 uvDx = dFdx(normalUv);
            vec2 uvDy = dFdy(normalUv);
            vec3 tangent = normalize(positionDx * uvDy.y - positionDy * uvDx.y);
            vec3 bitangent = normalize(-positionDx * uvDy.x + positionDy * uvDx.x);
            return normalize(mat3(tangent, bitangent, geometryNormal) * tangentNormal);
        }

        void main()
        {
            vec3 normal = normalize(vNormal);
            if (uHasNormal)
            {
                normal = mappedNormal(normal);
            }

            float diffuse = max(dot(normal, normalize(uLightDirection)), 0.0);
            float hemisphere = 0.38 + 0.22 * (normal.y * 0.5 + 0.5);
            vec4 albedo = uHasTexture
                ? texture(uDiffuseTexture, selectUv(uDiffuseUvSet))
                : uBaseColor;
            if (uHasMask)
            {
                vec4 mask = texture(uMaskTexture, selectUv(uMaskUvSet)) * uColorChannels;
                albedo.rgb = colorize(albedo.rgb, uColor1.rgb, mask.r);
                albedo.rgb = colorize(albedo.rgb, uColor2.rgb, mask.g);
                albedo.rgb = colorize(albedo.rgb, uColor3.rgb, mask.b);
                albedo.rgb = colorize(albedo.rgb, uColor4.rgb, mask.a);
            }
            // 0=opaque, 1=cutout/hollow, 2=blendalpha, 3=add. Cutout uses
            // PSO2's strict "above threshold" rule, so alpha zero is rejected
            // even when the stored cutoff is zero.
            if (uBlendMode == 1 && albedo.a <= uAlphaCutoff)
            {
                discard;
            }

            vec4 multi = uHasMulti
                ? texture(uMultiTexture, selectUv(uMultiUvSet))
                : vec4(0.0, 0.56, 1.0, 0.0);
            float ambientOcclusion = uHasMulti ? multi.b : 1.0;
            float roughness = mix(0.2, 1.0, multi.g);
            float metallic = multi.r;
            vec3 viewDirection = normalize(uCameraPosition - vPosition);
            vec3 halfDirection = normalize(normalize(uLightDirection) + viewDirection);
            float specularPower = mix(96.0, 6.0, roughness);
            float specular = pow(max(dot(normal, halfDirection), 0.0), specularPower);
            vec3 specularColor = mix(vec3(0.04), albedo.rgb, metallic);
            vec3 lit = albedo.rgb * (hemisphere * ambientOcclusion + diffuse * 0.55) +
                       specularColor * specular * (0.2 + metallic * 0.35);
            vec3 outColor = lit + albedo.rgb * multi.a;
            // Textures are sampled through SRGB8 (linear values) and the
            // palette arrives linear, but the swapchain is not an sRGB
            // target - encode on the way out or every midtone reads dark.
            float outputAlpha = uBlendMode >= 2 ? albedo.a : 1.0;
            FragColor = vec4(pow(clamp(outColor, 0.0, 1.0), vec3(1.0 / 2.2)), outputAlpha);
        }
        """;
}

public sealed record ViewportStatistics(
    int ModelCount,
    int VertexCount,
    int TriangleCount,
    int TextureCount,
    double FramesPerSecond);

public sealed record ViewportCameraState(
    float Yaw,
    float Pitch,
    float FocusY,
    float Distance,
    string Mode);
