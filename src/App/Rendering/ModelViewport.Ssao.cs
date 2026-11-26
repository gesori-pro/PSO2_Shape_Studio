using System.Numerics;
using Pso2ShapeStudio.Formats;
using Silk.NET.OpenGL;

namespace Pso2ShapeStudio.App.Rendering;

// Demo lighting's screen-space ambient occlusion. The creator multiplies the
// material AO of costume and skin (models 0/4) by a half-resolution, wide and
// soft occlusion (t_AO, its hbao0/hbao1 passes and a cross-bilateral blur)
// for the sun, the fill and the ambient; the headlight keeps the material AO
// alone. Here: a depth pre-pass at half resolution, a hemisphere test of
// 0.4 m around each point (the reach the Blender add-on matched to the
// creator's face and eyes) and a depth-aware blur.
public partial class ModelViewport
{
    private uint _aoDepthFramebuffer, _aoDepth, _aoRawFramebuffer, _aoRaw, _aoBlurFramebuffer, _aoBlurred;
    private uint _aoProgram, _aoBlurProgram;
    private uint _aoWidth, _aoHeight;
    private int _useAoLocation = -1, _aoTextureLocation = -1, _viewportSizeLocation = -1, _showAoLocation = -1;

    /// <summary>Diagnostic: draw the screen-space occlusion instead of the shading.</summary>
    public bool ShowAmbientOcclusion { get; set; }

    private const string AoFragmentShader = """
        #version 300 es
        precision highp float;
        in vec2 vUv;
        uniform sampler2D uDepth;
        uniform mat4 uViewProjection;
        uniform mat4 uInverseViewProjection;
        uniform vec3 uCameraPosition;
        uniform vec2 uTexel;
        out vec4 FragColor;

        const float RADIUS = 0.4;
        const int SAMPLES = 16;

        vec3 worldAt(vec2 uv, float depth)
        {
            vec4 p = uInverseViewProjection * vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            return p.xyz / p.w;
        }

        float hash(vec2 p)
        {
            return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
        }

        void main()
        {
            float depth = texture(uDepth, vUv).r;
            if (depth >= 1.0)
            {
                FragColor = vec4(1.0);
                return;
            }

            vec3 p = worldAt(vUv, depth);
            vec3 px = worldAt(vUv + vec2(uTexel.x, 0.0), texture(uDepth, vUv + vec2(uTexel.x, 0.0)).r);
            vec3 py = worldAt(vUv + vec2(0.0, uTexel.y), texture(uDepth, vUv + vec2(0.0, uTexel.y)).r);
            vec3 n = normalize(cross(px - p, py - p));
            vec3 toCamera = uCameraPosition - p;
            if (dot(n, toCamera) < 0.0) n = -n;

            // a per-pixel turn of the sample set, blurred away afterwards
            float turn = hash(gl_FragCoord.xy) * 6.2831853;
            vec3 helper = abs(n.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
            vec3 t = normalize(cross(helper, n));
            vec3 b = cross(n, t);
            float occluded = 0.0;
            for (int i = 0; i < SAMPLES; i++)
            {
                // a spiral over the hemisphere, nearer samples weighted in
                float fi = float(i) + 0.5;
                float z = 1.0 - fi / float(SAMPLES);
                float r = sqrt(1.0 - z * z);
                float a = fi * 2.3999632 + turn;
                float reach = mix(0.15, 1.0, (fi / float(SAMPLES)) * (fi / float(SAMPLES)));
                vec3 direction = t * (cos(a) * r) + b * (sin(a) * r) + n * z;
                vec3 s = p + direction * (RADIUS * reach);

                vec4 clip = uViewProjection * vec4(s, 1.0);
                vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
                if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) continue;
                vec3 surface = worldAt(uv, texture(uDepth, uv).r);
                float surfaceDistance = length(surface - uCameraPosition);
                float sampleDistance = length(s - uCameraPosition);
                if (surfaceDistance < sampleDistance - 0.01)
                {
                    // occluders far behind the sample do not count
                    occluded += smoothstep(0.0, 1.0, RADIUS / max(length(surface - p), 1e-4));
                }
            }

            FragColor = vec4(vec3(1.0 - occluded / float(SAMPLES)), 1.0);
        }
        """;

    // A 7x7 blur that keeps to surfaces at the same depth.
    private const string AoBlurFragmentShader = """
        #version 300 es
        precision highp float;
        in vec2 vUv;
        uniform sampler2D uAo;
        uniform sampler2D uDepth;
        uniform vec2 uTexel;
        out vec4 FragColor;

        void main()
        {
            float centerDepth = texture(uDepth, vUv).r;
            float sum = 0.0;
            float weight = 0.0;
            for (int y = -3; y <= 3; y++)
            {
                for (int x = -3; x <= 3; x++)
                {
                    vec2 uv = vUv + vec2(x, y) * uTexel;
                    float d = texture(uDepth, uv).r;
                    float w = exp(-float(x * x + y * y) / 18.0) *
                              (1.0 / (1.0 + abs(d - centerDepth) * 4000.0));
                    sum += texture(uAo, uv).r * w;
                    weight += w;
                }
            }
            FragColor = vec4(vec3(sum / max(weight, 1e-5)), 1.0);
        }
        """;

    private void CreateAoResources(GL api)
    {
        _aoProgram = CreateProgram(api, ToneVertexShader, AoFragmentShader);
        _aoBlurProgram = CreateProgram(api, ToneVertexShader, AoBlurFragmentShader);
        _useAoLocation = api.GetUniformLocation(_program, "uUseAo");
        _aoTextureLocation = api.GetUniformLocation(_program, "uAoTexture");
        _viewportSizeLocation = api.GetUniformLocation(_program, "uViewportSize");
        _showAoLocation = api.GetUniformLocation(_program, "uShowAo");
    }

    private unsafe void EnsureAoTargets(GL api, uint width, uint height)
    {
        width = Math.Max(1u, width / 2);
        height = Math.Max(1u, height / 2);
        if (_aoDepthFramebuffer != 0 && width == _aoWidth && height == _aoHeight) return;
        DeleteAoTargets(api);

        _aoDepth = api.GenTexture();
        api.BindTexture(TextureTarget.Texture2D, _aoDepth);
        api.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
            width, height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
        SetNearestClamp(api);
        _aoDepthFramebuffer = api.GenFramebuffer();
        api.BindFramebuffer(FramebufferTarget.Framebuffer, _aoDepthFramebuffer);
        api.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _aoDepth, 0);
        GLEnum none = GLEnum.None;
        api.DrawBuffers(1, &none);

        (_aoRawFramebuffer, _aoRaw) = CreateColorTarget(api, width, height);
        (_aoBlurFramebuffer, _aoBlurred) = CreateColorTarget(api, width, height);
        _aoWidth = width;
        _aoHeight = height;
    }

    private static unsafe (uint Framebuffer, uint Texture) CreateColorTarget(GL api, uint width, uint height)
    {
        var texture = api.GenTexture();
        api.BindTexture(TextureTarget.Texture2D, texture);
        api.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, width, height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        var framebuffer = api.GenFramebuffer();
        api.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        api.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, texture, 0);
        return (framebuffer, texture);
    }

    private static void SetNearestClamp(GL api)
    {
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    private void DeleteAoTargets(GL api)
    {
        foreach (var framebuffer in new[] { _aoDepthFramebuffer, _aoRawFramebuffer, _aoBlurFramebuffer })
        {
            if (framebuffer != 0) api.DeleteFramebuffer(framebuffer);
        }

        foreach (var texture in new[] { _aoDepth, _aoRaw, _aoBlurred })
        {
            if (texture != 0) api.DeleteTexture(texture);
        }

        _aoDepthFramebuffer = _aoRawFramebuffer = _aoBlurFramebuffer = 0;
        _aoDepth = _aoRaw = _aoBlurred = 0;
        _aoWidth = _aoHeight = 0;
    }

    private void DeleteAoResources(GL api)
    {
        DeleteAoTargets(api);
        if (_aoProgram != 0) api.DeleteProgram(_aoProgram);
        if (_aoBlurProgram != 0) api.DeleteProgram(_aoBlurProgram);
        _aoProgram = _aoBlurProgram = 0;
    }

    /// <summary>
    /// The occlusion of the solid surfaces seen from the camera, blurred, at
    /// half the viewport's resolution. Leaves the AO texture bound nowhere.
    /// </summary>
    private unsafe void RenderAmbientOcclusion(
        GL api, Matrix4x4 viewProjection, Vector3 camera, Matrix4x4 modelTransform,
        IEnumerable<GpuMesh> meshes, uint width, uint height)
    {
        EnsureAoTargets(api, width, height);

        // depth of what the main pass draws solid (opaque, hollow, blendalpha's cut)
        api.BindFramebuffer(FramebufferTarget.Framebuffer, _aoDepthFramebuffer);
        api.Viewport(0, 0, _aoWidth, _aoHeight);
        api.DepthMask(true);
        api.Disable(EnableCap.Blend);
        api.Clear((uint)ClearBufferMask.DepthBufferBit);
        api.UseProgram(_shadowProgram);
        api.UniformMatrix4(_shadowViewProjectionLocation, 1, false, (float*)&viewProjection);
        api.UniformMatrix4(_shadowModelTransformLocation, 1, false, (float*)&modelTransform);
        api.Uniform1(_shadowUseSkinningLocation, 1);
        api.Uniform1(_shadowDiffuseLocation, 0);
        api.ActiveTexture(TextureUnit.Texture0);
        foreach (var mesh in meshes)
        {
            if (mesh.BlendMode == MaterialBlendMode.Additive) continue;
            api.Uniform1(_shadowBlendModeLocation, mesh.BlendMode == MaterialBlendMode.AlphaBlend ? 4 : (int)mesh.BlendMode);
            api.Uniform1(_shadowAlphaCutoffLocation, mesh.AlphaCutoff);
            api.Uniform1(_shadowHasTextureLocation, mesh.Texture != 0 ? 1 : 0);
            api.Uniform1(_shadowUvSetLocation, mesh.TextureUvSets.Diffuse);
            api.BindTexture(TextureTarget.Texture2D, mesh.Texture);
            api.BindBufferBase(BufferTargetARB.UniformBuffer, 0, mesh.BoneBuffer);
            api.BindVertexArray(mesh.VertexArray);
            api.DrawElements(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, null);
        }

        Matrix4x4.Invert(viewProjection, out var inverse);
        api.Disable(EnableCap.DepthTest);
        api.BindVertexArray(_toneVao);

        api.BindFramebuffer(FramebufferTarget.Framebuffer, _aoRawFramebuffer);
        api.UseProgram(_aoProgram);
        api.ActiveTexture(TextureUnit.Texture0);
        api.BindTexture(TextureTarget.Texture2D, _aoDepth);
        api.Uniform1(api.GetUniformLocation(_aoProgram, "uDepth"), 0);
        api.UniformMatrix4(api.GetUniformLocation(_aoProgram, "uViewProjection"), 1, false, (float*)&viewProjection);
        api.UniformMatrix4(api.GetUniformLocation(_aoProgram, "uInverseViewProjection"), 1, false, (float*)&inverse);
        api.Uniform3(api.GetUniformLocation(_aoProgram, "uCameraPosition"), camera.X, camera.Y, camera.Z);
        api.Uniform2(api.GetUniformLocation(_aoProgram, "uTexel"), 1f / _aoWidth, 1f / _aoHeight);
        api.DrawArrays(PrimitiveType.Triangles, 0, 3);

        api.BindFramebuffer(FramebufferTarget.Framebuffer, _aoBlurFramebuffer);
        api.UseProgram(_aoBlurProgram);
        api.ActiveTexture(TextureUnit.Texture0);
        api.BindTexture(TextureTarget.Texture2D, _aoRaw);
        api.ActiveTexture(TextureUnit.Texture1);
        api.BindTexture(TextureTarget.Texture2D, _aoDepth);
        api.Uniform1(api.GetUniformLocation(_aoBlurProgram, "uAo"), 0);
        api.Uniform1(api.GetUniformLocation(_aoBlurProgram, "uDepth"), 1);
        api.Uniform2(api.GetUniformLocation(_aoBlurProgram, "uTexel"), 1f / _aoWidth, 1f / _aoHeight);
        api.DrawArrays(PrimitiveType.Triangles, 0, 3);

        api.BindVertexArray(0);
        api.Enable(EnableCap.DepthTest);
    }
}
