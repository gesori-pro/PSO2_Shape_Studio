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

public sealed partial class ModelViewport : OpenGlControlBase
{
    private const int MaximumBones = 256;

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
    private int _hiddenMeshPartMask;

    private GL? _gl;
    private uint _program;
    private uint _boneBuffer;

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

    public void SetOrnamentVisibility(
        bool basewearOrnament1,
        bool basewearOrnament2,
        bool outerwearOrnament)
    {
        var mask = 0;
        Hide(Pso2MeshPart.BasewearOrnament1, basewearOrnament1);
        Hide(Pso2MeshPart.BasewearOrnament2, basewearOrnament2);
        Hide(Pso2MeshPart.OuterwearOrnament, outerwearOrnament);
        Volatile.Write(ref _hiddenMeshPartMask, mask);
        RequestNextFrameRendering();

        void Hide(Pso2MeshPart part, bool visible)
        {
            if (!visible)
            {
                mask |= 1 << (int)part;
            }
        }
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

            // The translucent surface writes depth before the models. From a
            // normal above-floor view, geometry above Y=0 stays visible while
            // geometry below it is cleanly hidden at the intersection.
            DrawFloorSurface(_gl);
            _gl.Uniform1(_useSkinningLocation, 1);
            _gl.Uniform1(_blendModeLocation, (int)MaterialBlendMode.Opaque);

            // Opaque/cutout materials write depth without blending. Transparent
            // and additive materials follow far-to-near without touching depth.
            // Leaving blending enabled for opaque materials lets tiny non-zero
            // BC alpha values reveal the surface below as single-pixel specks.
            var hiddenMeshPartMask = Volatile.Read(ref _hiddenMeshPartMask);
            var visibleMeshes = _gpuMeshes.Where(value =>
                IsMeshPartVisible(value.Part, hiddenMeshPartMask));
            var drawOrder = visibleMeshes
                .Where(value => !value.IsTransparent)
                .Concat(visibleMeshes
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

            DrawFloorGrid(_gl);
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

        // Meshes are cheap to rebuild; textures are not (a skin set alone is
        // hundreds of megabytes of mip-mapped uploads). Keep the texture
        // cache across rebuilds so a visibility toggle, color change, or an
        // added model only uploads pixels the GPU has not seen, then prune
        // whatever this scene no longer references.
        DeleteMeshBuffers(api);
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

        PruneUnusedTextures(api);
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

        // The triangle list is int[] but every value is a non-negative vertex
        // index, so the bytes are uploaded directly as GL_UNSIGNED_INT.
        api.BindBuffer(BufferTargetARB.ElementArrayBuffer, indexBuffer);
        fixed (int* pointer = mesh.Triangles)
        {
            api.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(mesh.Triangles.Length * sizeof(uint)),
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
            (uint)mesh.Triangles.Length,
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
            MeshCenter(mesh),
            mesh.Part);
    }

    private static bool IsMeshPartVisible(Pso2MeshPart part, int hiddenMeshPartMask)
    {
        var value = (int)part;
        return value is < 0 or >= 31 || (hiddenMeshPartMask & (1 << value)) == 0;
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
        DeleteMeshBuffers(api);
        foreach (var texture in _gpuTextures.Values)
        {
            api.DeleteTexture(texture);
        }
        _gpuTextures.Clear();
    }

    private void DeleteMeshBuffers(GL api)
    {
        foreach (var mesh in _gpuMeshes)
        {
            api.DeleteVertexArray(mesh.VertexArray);
            api.DeleteBuffer(mesh.VertexBuffer);
            api.DeleteBuffer(mesh.IndexBuffer);
        }

        _gpuMeshes.Clear();
    }

    /// <summary>
    /// Frees cached textures no mesh in the rebuilt scene samples anymore.
    /// A model hidden with the eye toggle gives its VRAM back here and pays
    /// one re-upload when shown again.
    /// </summary>
    private void PruneUnusedTextures(GL api)
    {
        var used = new HashSet<uint>(_gpuMeshes.Count * 4);
        foreach (var mesh in _gpuMeshes)
        {
            used.Add(mesh.Texture);
            used.Add(mesh.MaskTexture);
            used.Add(mesh.NormalTexture);
            used.Add(mesh.MultiTexture);
        }

        List<(RenderTexture Texture, bool Srgb)>? stale = null;
        foreach (var pair in _gpuTextures)
        {
            if (!used.Contains(pair.Value))
            {
                (stale ??= []).Add(pair.Key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var key in stale)
        {
            api.DeleteTexture(_gpuTextures[key]);
            _gpuTextures.Remove(key);
        }
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
        Vector3 Center,
        Pso2MeshPart Part)
    {
        public bool IsTransparent => BlendMode is
            MaterialBlendMode.AlphaBlend or MaterialBlendMode.Additive;
    }

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
