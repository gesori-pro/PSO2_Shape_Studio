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
    private RenderSkinTextureSet? _pendingSkinTextureT1;
    private RenderSkinTextureSet? _pendingSkinTextureT2;
    private CharacterColorPalette _pendingCharacterColors = CharacterColorPalette.Default;
    private float _muscularity;
    private float _groundLift;
    private float _skinGloss;
    private Matrix4x4[] _pendingSkinMatrices = IdentityBones();
    private bool _sceneDirty = true;
    private bool _bonesDirty = true;
    private int _hiddenMeshPartMask;

    private GL? _gl;
    private uint _program;
    private uint _boneBuffer;
    private bool _demoLighting;
    private int _demoLightingLocation;
    private int _skinMaterialLocation;
    private int _muscularityLocation;
    private int _skinGlossLocation;
    private int _demoEnvironmentLocation;
    private uint _demoEnvironment;
    private int _gameMaterialLocation;
    private int _gameEnvironmentLocation;
    private uint _gameEnvironment;
    private uint _hdrFramebuffer, _hdrColor, _hdrDepth, _toneProgram, _toneVao;
    private uint _hdrWidth, _hdrHeight;

    public void SetDemoLighting(bool enabled)
    {
        _demoLighting = enabled;
        RequestNextFrameRendering();
    }

    private int _viewProjectionLocation = -1;
    private int _modelTransformLocation = -1;
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
    private int _hasMuscleDiffuseLocation = -1;
    private int _muscleDiffuseTextureLocation = -1;
    private int _hasMuscleMaskLocation = -1;
    private int _muscleMaskTextureLocation = -1;
    private int _hasMuscleNormalLocation = -1;
    private int _muscleNormalTextureLocation = -1;
    private int _hasMuscleMultiLocation = -1;
    private int _muscleMultiTextureLocation = -1;
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

    public void SetSkinTextures(RenderSkinTextureSet? type1, RenderSkinTextureSet? type2)
    {
        lock (_sceneLock)
        {
            _pendingSkinTextureT1 = type1;
            _pendingSkinTextureT2 = type2;
            _sceneDirty = true;
        }

        RequestNextFrameRendering();
    }

    public void SetCharacterSurface(CharacterSurfaceSettings surface)
    {
        _muscularity = Math.Clamp(surface.Muscularity, 0f, 1f);
        _skinGloss = Math.Clamp(surface.SkinGloss, -1f, 1f);
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

    /// <summary>
    /// How far to raise the models so they stand on the floor (see
    /// GroundContact); the floor guide and camera stay where they are.
    /// </summary>
    public void SetGroundLift(float lift)
    {
        Volatile.Write(ref _groundLift, lift);
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
            _demoLightingLocation = _gl.GetUniformLocation(_program, "uDemoLighting");
            _skinMaterialLocation = _gl.GetUniformLocation(_program, "uSkinMaterial");
            _muscularityLocation = _gl.GetUniformLocation(_program, "uMuscularity");
            _skinGlossLocation = _gl.GetUniformLocation(_program, "uSkinGloss");
            _demoEnvironmentLocation = _gl.GetUniformLocation(_program, "uDemoEnvironment");
            _gameMaterialLocation = _gl.GetUniformLocation(_program, "uGameMaterial");
            _gameEnvironmentLocation = _gl.GetUniformLocation(_program, "uGameEnvironment");
            _viewProjectionLocation = _gl.GetUniformLocation(_program, "uViewProjection");
            _modelTransformLocation = _gl.GetUniformLocation(_program, "uModelTransform");
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
            _hasMuscleDiffuseLocation = _gl.GetUniformLocation(_program, "uHasMuscleDiffuse");
            _muscleDiffuseTextureLocation = _gl.GetUniformLocation(_program, "uMuscleDiffuseTexture");
            _hasMuscleMaskLocation = _gl.GetUniformLocation(_program, "uHasMuscleMask");
            _muscleMaskTextureLocation = _gl.GetUniformLocation(_program, "uMuscleMaskTexture");
            _hasMuscleNormalLocation = _gl.GetUniformLocation(_program, "uHasMuscleNormal");
            _muscleNormalTextureLocation = _gl.GetUniformLocation(_program, "uMuscleNormalTexture");
            _hasMuscleMultiLocation = _gl.GetUniformLocation(_program, "uHasMuscleMulti");
            _muscleMultiTextureLocation = _gl.GetUniformLocation(_program, "uMuscleMultiTexture");
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
            _demoEnvironment = CreateDemoEnvironmentCube(_gl);
            _gameEnvironment = CreateCreatorEnvironmentCube(_gl);
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
            DeleteDemoTargets(_gl);
            if (_demoEnvironment != 0) _gl.DeleteTexture(_demoEnvironment);
            _demoEnvironment = 0;
            if (_gameEnvironment != 0) _gl.DeleteTexture(_gameEnvironment);
            _gameEnvironment = 0;
            if (_toneProgram != 0) _gl.DeleteProgram(_toneProgram);
            if (_toneVao != 0) _gl.DeleteVertexArray(_toneVao);
            _toneProgram = _toneVao = 0;

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
            if (_demoLighting) EnsureDemoTargets(_gl, width, height);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _demoLighting ? _hdrFramebuffer : (uint)framebuffer);
            _gl.Viewport(0, 0, width, height);
            // In demo lighting the background is cleared to what the tone
            // pass turns into the chosen colour, with alpha 0 so the glare
            // can tell it from the character.
            var clear = _demoLighting ? DemoBackground() : _background;
            _gl.ClearColor(clear.X, clear.Y, clear.Z, _demoLighting ? 0f : 1f);
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

            var viewProjection = BuildViewProjection(width / (float)height);
            var cameraPosition = CameraPosition();
            var identityTransform = Matrix4x4.Identity;
            var modelTransform = Matrix4x4.CreateRotationY(_modelYaw) *
                                 Matrix4x4.CreateTranslation(0f, Volatile.Read(ref _groundLift), 0f);
            var sortingModelTransform = modelTransform;
            _gl.UseProgram(_program);
            _gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
            _gl.UniformMatrix4(_modelTransformLocation, 1, false, (float*)&identityTransform);
            _gl.Uniform1(_demoLightingLocation, _demoLighting ? 1 : 0);
            var light = _demoLighting ? new Vector3(0.29143327f, 0.11152586f, 0.95006776f) : new Vector3(0.38f, 0.78f, 0.49f);
            _gl.Uniform3(_lightDirectionLocation, light.X, light.Y, light.Z);
            _gl.Uniform3(
                _cameraPositionLocation,
                cameraPosition.X,
                cameraPosition.Y,
                cameraPosition.Z);
            _gl.Uniform1(_diffuseTextureLocation, 0);
            _gl.Uniform1(_maskTextureLocation, 1);
            _gl.Uniform1(_normalTextureLocation, 2);
            _gl.Uniform1(_multiTextureLocation, 3);
            _gl.Uniform1(_demoEnvironmentLocation, 4);
            _gl.Uniform1(_gameEnvironmentLocation, 9);
            _gl.Uniform1(_muscleDiffuseTextureLocation, 5);
            _gl.Uniform1(_muscleMaskTextureLocation, 6);
            _gl.Uniform1(_muscleNormalTextureLocation, 7);
            _gl.Uniform1(_muscleMultiTextureLocation, 8);
            _gl.Uniform1(_muscularityLocation, _muscularity);
            _gl.Uniform1(_skinGlossLocation, _skinGloss);
            _gl.ActiveTexture(TextureUnit.Texture4);
            _gl.BindTexture(TextureTarget.TextureCubeMap, _demoEnvironment);
            _gl.ActiveTexture(TextureUnit.Texture9);
            _gl.BindTexture(TextureTarget.TextureCubeMap, _gameEnvironment);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, 0, _boneBuffer);

            // The translucent surface writes depth before the models. From a
            // normal above-floor view, geometry above Y=0 stays visible while
            // geometry below it is cleanly hidden at the intersection.
            _gl.Uniform1(_gameMaterialLocation, 0);
            DrawFloorSurface(_gl);
            _gl.UniformMatrix4(_modelTransformLocation, 1, false, (float*)&modelTransform);
            _gl.Uniform1(_gameMaterialLocation, 1);
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
                        Vector3.DistanceSquared(
                            cameraPosition,
                            Vector3.Transform(value.Center, sortingModelTransform))));
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
                _gl.Uniform1(_hasMuscleDiffuseLocation, mesh.MuscleTexture != 0 ? 1 : 0);
                _gl.Uniform1(_hasMuscleMaskLocation, mesh.MuscleMaskTexture != 0 ? 1 : 0);
                _gl.Uniform1(_hasMuscleNormalLocation, mesh.MuscleNormalTexture != 0 ? 1 : 0);
                _gl.Uniform1(_hasMuscleMultiLocation, mesh.MuscleMultiTexture != 0 ? 1 : 0);
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
                _gl.Uniform1(_skinMaterialLocation, mesh.SkinMaterial ? 1 : 0);
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
                _gl.ActiveTexture(TextureUnit.Texture5);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.MuscleTexture);
                _gl.ActiveTexture(TextureUnit.Texture6);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.MuscleMaskTexture);
                _gl.ActiveTexture(TextureUnit.Texture7);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.MuscleNormalTexture);
                _gl.ActiveTexture(TextureUnit.Texture8);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.MuscleMultiTexture);
                _gl.BindVertexArray(mesh.VertexArray);
                _gl.DrawElements(
                    PrimitiveType.Triangles,
                    mesh.IndexCount,
                    DrawElementsType.UnsignedInt,
                    null);
            }

            _gl.UniformMatrix4(_modelTransformLocation, 1, false, (float*)&identityTransform);
            _gl.Uniform1(_gameMaterialLocation, 0);
            DrawFloorGrid(_gl);
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.BindVertexArray(0);
            if (_demoLighting) DrawDemoTone(_gl, (uint)framebuffer, clear);
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

    // What demo lighting clears the background to: the value the tone pass
    // turns into the chosen colour. (The glare, in the tone pass, leaves the
    // background alone.)
    private Vector3 DemoBackground() => new(
        InverseDemoTone(_background.X),
        InverseDemoTone(_background.Y),
        InverseDemoTone(_background.Z));

    // Capture frame16602, event3396: x*(3*x+.03)/(x*(3*x+1)+.14), exposure .84.
    // Invert the curve for the UI background so changing the lighting keeps its chosen color.
    private static float InverseDemoTone(float display)
    {
        var y = MathF.Pow(Math.Clamp(display, 0f, 0.9999f), 2.2f);
        var a = 3f * (1f - y);
        var b = 0.03f - y;
        return (-b + MathF.Sqrt(b * b + 0.56f * a * y)) / (2f * a * 0.84f);
    }

    private unsafe void EnsureDemoTargets(GL api, uint width, uint height)
    {
        if (_hdrFramebuffer != 0 && width == _hdrWidth && height == _hdrHeight) return;
        DeleteDemoTargets(api);
        _hdrColor = api.GenTexture();
        api.BindTexture(TextureTarget.Texture2D, _hdrColor);
        api.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, width, height, 0, PixelFormat.Rgba, PixelType.HalfFloat, null);
        // The glare reads the frame blurred, from its mips.
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _hdrDepth = api.GenRenderbuffer();
        api.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _hdrDepth);
        api.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, width, height);
        _hdrFramebuffer = api.GenFramebuffer();
        api.BindFramebuffer(FramebufferTarget.Framebuffer, _hdrFramebuffer);
        api.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _hdrColor, 0);
        api.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _hdrDepth);
        if (api.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            DeleteDemoTargets(api);
            _demoLighting = false;
            throw new InvalidOperationException("HDR preview framebuffer is not supported by this OpenGL context.");
        }
        _hdrWidth = width; _hdrHeight = height;
        if (_toneProgram == 0) _toneProgram = CreateProgram(api, ToneVertexShader, ToneFragmentShader);
        if (_toneVao == 0) _toneVao = api.GenVertexArray();
    }

    private void DeleteDemoTargets(GL api)
    {
        if (_hdrFramebuffer != 0) api.DeleteFramebuffer(_hdrFramebuffer);
        if (_hdrColor != 0) api.DeleteTexture(_hdrColor);
        if (_hdrDepth != 0) api.DeleteRenderbuffer(_hdrDepth);
        _hdrFramebuffer = _hdrColor = _hdrDepth = 0;
        _hdrWidth = _hdrHeight = 0;
    }

    private static unsafe uint CreateDemoEnvironmentCube(GL api)
    {
        const int faceWidth = 64;
        const int maximumMip = 6;
        var texture = api.GenTexture();
        try
        {
            api.BindTexture(TextureTarget.TextureCubeMap, texture);
            api.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            for (var face = 0; face < 6; face++)
            {
                var pixels = new float[faceWidth * faceWidth * 4];
                for (var y = 0; y < faceWidth; y++)
                {
                    for (var x = 0; x < faceWidth; x++)
                    {
                        var u = 2f * (x + 0.5f) / faceWidth - 1f;
                        var v = 2f * (y + 0.5f) / faceWidth - 1f;
                        var direction = CubeDirection(face, u, v);
                        var color = StudioRadiance(direction);
                        var offset = (y * faceWidth + x) * 4;
                        pixels[offset] = color.X;
                        pixels[offset + 1] = color.Y;
                        pixels[offset + 2] = color.Z;
                        pixels[offset + 3] = 1f;
                    }
                }

                fixed (float* pointer = pixels)
                {
                    var target = (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + face);
                    api.TexImage2D(
                        target,
                        0,
                        InternalFormat.Rgba16f,
                        faceWidth,
                        faceWidth,
                        0,
                        PixelFormat.Rgba,
                        PixelType.Float,
                        pointer);
                }
            }

            api.GenerateMipmap(TextureTarget.TextureCubeMap);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, maximumMip);
            return texture;
        }
        catch
        {
            api.DeleteTexture(texture);
            throw;
        }

        static Vector3 CubeDirection(int face, float u, float v) => Vector3.Normalize(face switch
        {
            0 => new Vector3(1f, -v, -u),
            1 => new Vector3(-1f, -v, u),
            2 => new Vector3(u, 1f, v),
            3 => new Vector3(u, -1f, -v),
            4 => new Vector3(u, -v, 1f),
            _ => new Vector3(-u, -v, -1f),
        });

        static Vector3 StudioRadiance(Vector3 direction)
        {
            var height = Math.Clamp(direction.Y * 0.5f + 0.5f, 0f, 1f);
            var color = Vector3.Lerp(
                new Vector3(0.018f, 0.024f, 0.038f),
                new Vector3(0.24f, 0.31f, 0.43f),
                MathF.Pow(height, 0.7f));
            color += SoftBox(direction, new Vector3(-0.42f, 0.32f, 0.85f), 28f, 4.2f) *
                     new Vector3(1.0f, 0.86f, 0.70f);
            color += SoftBox(direction, new Vector3(0.82f, 0.18f, 0.54f), 20f, 1.35f) *
                     new Vector3(0.55f, 0.72f, 1.0f);
            color += SoftBox(direction, new Vector3(0.05f, 0.92f, -0.38f), 36f, 1.8f) *
                     new Vector3(0.82f, 0.90f, 1.0f);
            return color;
        }

        static float SoftBox(
            Vector3 direction,
            Vector3 lightDirection,
            float power,
            float intensity) =>
            MathF.Pow(Math.Max(0f, Vector3.Dot(direction, Vector3.Normalize(lightDirection))), power) *
            intensity;
    }

    // The character creator's lighting environment (s_CubeExtTex of capture
    // frame 16602). Its lighting pass reads the 256-texel cube at mip 7.5
    // only, so mips 7 and 8 - two by two and one texel a face - are all of
    // it that shows: here as the two levels of a small cube, read at 0.5.
    // Linear RGB; faces +X, -X, +Y, -Y, +Z, -Z, rows top to bottom.
    private static readonly float[][] CreatorEnvironment =
    [
        [
            0.54004f, 0.48657f, 0.49194f, 0.40967f, 0.41089f, 0.54004f, 0.52246f, 0.38379f, 0.24536f, 0.44556f, 0.33740f, 0.19714f,
            0.10419f, 0.13708f, 0.21277f, 0.15247f, 0.16724f, 0.13489f, 0.51855f, 0.36938f, 0.20032f, 0.51953f, 0.37939f, 0.22083f,
            0.24353f, 0.31201f, 0.51758f, 0.08301f, 0.14404f, 0.32690f, 0.07373f, 0.10748f, 0.17090f, 0.02919f, 0.06384f, 0.16821f,
            0.68115f, 0.48682f, 0.31030f, 0.59277f, 0.41333f, 0.24634f, 0.55029f, 0.38525f, 0.21289f, 0.52979f, 0.36255f, 0.20068f,
            0.16992f, 0.22534f, 0.35034f, 0.62793f, 0.56689f, 0.59717f, 0.54639f, 0.42676f, 0.27881f, 0.57861f, 0.43115f, 0.28052f,
            0.16211f, 0.23914f, 0.46411f, 0.60400f, 0.65430f, 0.76172f, 0.32275f, 0.26440f, 0.13293f, 0.40015f, 0.31494f, 0.16223f,
        ],
        [
            0.47949f, 0.40479f, 0.36865f,
            0.32373f, 0.26318f, 0.19226f,
            0.10736f, 0.15686f, 0.29590f,
            0.58838f, 0.41211f, 0.24255f,
            0.48071f, 0.41260f, 0.37671f,
            0.37231f, 0.36816f, 0.38037f,
        ],
    ];

    private static unsafe uint CreateCreatorEnvironmentCube(GL api)
    {
        var texture = api.GenTexture();
        try
        {
            api.BindTexture(TextureTarget.TextureCubeMap, texture);
            api.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            for (var level = 0; level < CreatorEnvironment.Length; level++)
            {
                var size = 2 >> level;
                var texels = size * size;
                for (var face = 0; face < 6; face++)
                {
                    var pixels = new float[texels * 4];
                    for (var i = 0; i < texels; i++)
                    {
                        var source = (face * texels + i) * 3;
                        pixels[i * 4] = CreatorEnvironment[level][source];
                        pixels[i * 4 + 1] = CreatorEnvironment[level][source + 1];
                        pixels[i * 4 + 2] = CreatorEnvironment[level][source + 2];
                        pixels[i * 4 + 3] = 1f;
                    }

                    fixed (float* pointer = pixels)
                    {
                        api.TexImage2D(
                            (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + face),
                            level,
                            InternalFormat.Rgba16f,
                            (uint)size,
                            (uint)size,
                            0,
                            PixelFormat.Rgba,
                            PixelType.Float,
                            pointer);
                    }
                }
            }

            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
            api.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, CreatorEnvironment.Length - 1);
            return texture;
        }
        catch
        {
            api.DeleteTexture(texture);
            throw;
        }
    }

    private void DrawDemoTone(GL api, uint target, Vector3 background)
    {
        api.BindFramebuffer(FramebufferTarget.Framebuffer, target);
        api.Disable(EnableCap.DepthTest);
        api.Disable(EnableCap.CullFace);
        api.UseProgram(_toneProgram);
        api.ActiveTexture(TextureUnit.Texture0);
        api.BindTexture(TextureTarget.Texture2D, _hdrColor);
        api.GenerateMipmap(TextureTarget.Texture2D);
        api.Uniform1(api.GetUniformLocation(_toneProgram, "uScene"), 0);
        api.Uniform3(
            api.GetUniformLocation(_toneProgram, "uBackground"),
            background.X,
            background.Y,
            background.Z);
        api.BindVertexArray(_toneVao);
        api.DrawArrays(PrimitiveType.Triangles, 0, 3);
        api.BindVertexArray(0);
        api.Enable(EnableCap.DepthTest);
    }

    private unsafe void UploadPendingScene(GL api)
    {
        IReadOnlyList<RenderModel>? models = null;
        RenderSkinTextureSet? skinTextureT1 = null;
        RenderSkinTextureSet? skinTextureT2 = null;
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
                        selectedSkin.Base.Diffuse ?? materialTextures.Diffuse,
                        selectedSkin.Base.Mask ?? materialTextures.Mask,
                        selectedSkin.Base.Normal ?? materialTextures.Normal,
                        selectedSkin.Base.Multi ?? materialTextures.Multi);
                var muscleTextures = selectedSkin is null
                    ? textures
                    : new RenderTextureSet(
                        selectedSkin.Muscle.Diffuse ?? textures.Diffuse,
                        selectedSkin.Muscle.Mask ?? textures.Mask,
                        selectedSkin.Muscle.Normal ?? textures.Normal,
                        selectedSkin.Muscle.Multi ?? textures.Multi);
                _gpuMeshes.Add(UploadMesh(
                    api,
                    mesh,
                    material,
                    textures,
                    muscleTextures,
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
        RenderTextureSet muscleTextures,
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
        var muscleTexture = muscleTextures.Diffuse is null
            ? 0
            : GetOrUploadTexture(api, muscleTextures.Diffuse, srgb: true);
        var muscleMaskTexture = muscleTextures.Mask is null
            ? 0
            : GetOrUploadTexture(api, muscleTextures.Mask, srgb: false);
        var muscleNormalTexture = muscleTextures.Normal is null
            ? 0
            : GetOrUploadTexture(api, muscleTextures.Normal, srgb: false);
        var muscleMultiTexture = muscleTextures.Multi is null
            ? 0
            : GetOrUploadTexture(api, muscleTextures.Multi, srgb: false);
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
            muscleTexture,
            muscleMaskTexture,
            muscleNormalTexture,
            muscleMultiTexture,
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
        var used = new HashSet<uint>(_gpuMeshes.Count * 8);
        foreach (var mesh in _gpuMeshes)
        {
            used.Add(mesh.Texture);
            used.Add(mesh.MaskTexture);
            used.Add(mesh.NormalTexture);
            used.Add(mesh.MultiTexture);
            used.Add(mesh.MuscleTexture);
            used.Add(mesh.MuscleMaskTexture);
            used.Add(mesh.MuscleNormalTexture);
            used.Add(mesh.MuscleMultiTexture);
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
        uint MuscleTexture,
        uint MuscleMaskTexture,
        uint MuscleNormalTexture,
        uint MuscleMultiTexture,
        Vector4 BaseColor,
        Vector4 Color1,
        Vector4 Color2,
        Vector4 Color3,
        Vector4 Color4,
        Vector4 ColorChannels,
        bool MultiplyColor,
        bool SkinMaterial,
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
    float ModelYaw,
    string Mode);
