using Silk.NET.OpenGL;

namespace Pso2ShapeStudio.App.Rendering;

// GLSL sources and program compilation. The GL ES 3.0 pair implements
// skinning, the PSO2 mask colorizer, and the linear-to-sRGB output encode.
public sealed partial class ModelViewport
{
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
