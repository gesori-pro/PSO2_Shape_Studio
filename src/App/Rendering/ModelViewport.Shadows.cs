using System.Numerics;
using Pso2ShapeStudio.Formats;
using Silk.NET.OpenGL;

namespace Pso2ShapeStudio.App.Rendering;

// Demo lighting's sun shadow. The character creator shadows the sun on the
// characters (the face and eyes excepted, which this viewer does not show);
// its fill light and the camera headlight cast none. A depth map from the
// sun over the character's box, sampled with a small PCF kernel.
public partial class ModelViewport
{
    private const int ShadowMapSize = 2048;
    // Half the side of the box the shadow map covers, around the character.
    private const float ShadowExtent = 1.25f;

    private uint _shadowFramebuffer, _shadowDepth, _shadowProgram;
    private int _shadowViewProjectionLocation = -1, _shadowModelTransformLocation = -1;
    private int _shadowUseSkinningLocation = -1, _shadowDiffuseLocation = -1, _shadowHasTextureLocation = -1;
    private int _shadowBlendModeLocation = -1, _shadowAlphaCutoffLocation = -1, _shadowUvSetLocation = -1;
    private int _useShadowLocation = -1, _shadowMatrixLocation = -1, _shadowMapLocation = -1;

    private const string ShadowVertexShader = """
        #version 300 es
        precision highp float;
        precision highp int;
        layout(location = 0) in vec3 aPosition;
        layout(location = 2) in vec2 aUv1;
        layout(location = 3) in vec2 aUv2;
        layout(location = 4) in vec2 aUv3;
        layout(location = 5) in vec4 aWeights;
        layout(location = 6) in uvec4 aBones;

        layout(std140) uniform Bones
        {
            mat4 uBones[256];
        };

        uniform mat4 uShadowViewProjection;
        uniform mat4 uModelTransform;
        uniform bool uUseSkinning;
        uniform int uUvSet;
        out vec2 vUv;

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
            vec2 uv = uUvSet == 1 ? aUv2 : uUvSet == 2 ? aUv3 : aUv1;
            vUv = vec2(uv.x, 1.0 - uv.y);
            gl_Position = uShadowViewProjection * (uModelTransform * skin * vec4(aPosition, 1.0));
        }
        """;

    // Only what the main pass would cut out is cut here: hollow at its
    // cutoff, blendalpha's solid part at 0.9931.
    private const string ShadowFragmentShader = """
        #version 300 es
        precision highp float;
        in vec2 vUv;
        uniform sampler2D uDiffuseTexture;
        uniform bool uHasTexture;
        uniform int uBlendMode;
        uniform float uAlphaCutoff;

        void main()
        {
            float alpha = uHasTexture ? texture(uDiffuseTexture, vUv).a : 1.0;
            if (uBlendMode == 1 && alpha <= uAlphaCutoff) discard;
            if (uBlendMode == 4 && alpha < 0.9931) discard;
        }
        """;

    private unsafe void CreateShadowResources(GL api)
    {
        _shadowProgram = CreateProgram(api, ShadowVertexShader, ShadowFragmentShader);
        _shadowViewProjectionLocation = api.GetUniformLocation(_shadowProgram, "uShadowViewProjection");
        _shadowModelTransformLocation = api.GetUniformLocation(_shadowProgram, "uModelTransform");
        _shadowUseSkinningLocation = api.GetUniformLocation(_shadowProgram, "uUseSkinning");
        _shadowDiffuseLocation = api.GetUniformLocation(_shadowProgram, "uDiffuseTexture");
        _shadowHasTextureLocation = api.GetUniformLocation(_shadowProgram, "uHasTexture");
        _shadowBlendModeLocation = api.GetUniformLocation(_shadowProgram, "uBlendMode");
        _shadowAlphaCutoffLocation = api.GetUniformLocation(_shadowProgram, "uAlphaCutoff");
        _shadowUvSetLocation = api.GetUniformLocation(_shadowProgram, "uUvSet");
        api.UniformBlockBinding(_shadowProgram, api.GetUniformBlockIndex(_shadowProgram, "Bones"), 0);

        _useShadowLocation = api.GetUniformLocation(_program, "uUseShadow");
        _shadowMatrixLocation = api.GetUniformLocation(_program, "uShadowMatrix");
        _shadowMapLocation = api.GetUniformLocation(_program, "uShadowMap");

        _shadowDepth = api.GenTexture();
        api.BindTexture(TextureTarget.Texture2D, _shadowDepth);
        api.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
            ShadowMapSize, ShadowMapSize, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        api.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        _shadowFramebuffer = api.GenFramebuffer();
        api.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);
        api.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _shadowDepth, 0);
        GLEnum none = GLEnum.None;
        api.DrawBuffers(1, &none);
        if (api.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("The shadow map framebuffer is incomplete.");
        }

        api.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void DeleteShadowResources(GL api)
    {
        if (_shadowFramebuffer != 0) api.DeleteFramebuffer(_shadowFramebuffer);
        if (_shadowDepth != 0) api.DeleteTexture(_shadowDepth);
        if (_shadowProgram != 0) api.DeleteProgram(_shadowProgram);
        _shadowFramebuffer = _shadowDepth = _shadowProgram = 0;
    }

    /// <summary>
    /// Renders the sun's depth map of the solid surfaces and returns the
    /// matrix taking a world point to its shadow-map coordinates (0..1).
    /// </summary>
    private unsafe Matrix4x4 RenderShadowMap(GL api, Vector3 sun, Matrix4x4 modelTransform, IEnumerable<GpuMesh> meshes)
    {
        var center = Vector3.Transform(new Vector3(0f, 0.95f, 0f), modelTransform);
        var up = MathF.Abs(sun.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(center + sun * 5f, center, up);
        var projection = Matrix4x4.CreateOrthographic(2f * ShadowExtent, 2f * ShadowExtent, 0.1f, 10f);
        var viewProjection = view * projection;

        api.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);
        api.Viewport(0, 0, ShadowMapSize, ShadowMapSize);
        api.DepthMask(true);
        api.Disable(EnableCap.Blend);
        api.Clear((uint)ClearBufferMask.DepthBufferBit);
        api.Enable(EnableCap.PolygonOffsetFill);
        api.PolygonOffset(2f, 4f);
        api.UseProgram(_shadowProgram);
        api.UniformMatrix4(_shadowViewProjectionLocation, 1, false, (float*)&viewProjection);
        api.UniformMatrix4(_shadowModelTransformLocation, 1, false, (float*)&modelTransform);
        api.Uniform1(_shadowUseSkinningLocation, 1);
        api.Uniform1(_shadowDiffuseLocation, 0);
        api.ActiveTexture(TextureUnit.Texture0);
        foreach (var mesh in meshes)
        {
            if (mesh.BlendMode == MaterialBlendMode.Additive)
            {
                continue;
            }

            var mode = mesh.BlendMode == MaterialBlendMode.AlphaBlend ? 4 : (int)mesh.BlendMode;
            api.Uniform1(_shadowBlendModeLocation, mode);
            api.Uniform1(_shadowAlphaCutoffLocation, mesh.AlphaCutoff);
            api.Uniform1(_shadowHasTextureLocation, mesh.Texture != 0 ? 1 : 0);
            api.Uniform1(_shadowUvSetLocation, mesh.TextureUvSets.Diffuse);
            api.BindTexture(TextureTarget.Texture2D, mesh.Texture);
            api.BindBufferBase(BufferTargetARB.UniformBuffer, 0, mesh.BoneBuffer);
            api.BindVertexArray(mesh.VertexArray);
            api.DrawElements(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, null);
        }

        api.Disable(EnableCap.PolygonOffsetFill);
        api.BindVertexArray(0);
        // clip space -> 0..1 texture and depth range
        return viewProjection * Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0.5f);
    }
}
