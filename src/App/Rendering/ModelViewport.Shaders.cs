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
        uniform mat4 uModelTransform;
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
            vec4 position = uModelTransform * skin * vec4(aPosition, 1.0);
            vNormal = normalize(mat3(uModelTransform) * mat3(skin) * aNormal);
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
        uniform bool uDemoLighting;
        uniform bool uSkinMaterial;
        uniform float uMuscularity;
        uniform float uSkinGloss;
        uniform vec3 uLightDirection;
        uniform vec3 uCameraPosition;
        uniform samplerCube uDemoEnvironment;
        uniform vec4 uBaseColor;
        uniform bool uHasTexture;
        uniform sampler2D uDiffuseTexture;
        uniform bool uHasMask;
        uniform sampler2D uMaskTexture;
        uniform bool uHasNormal;
        uniform sampler2D uNormalTexture;
        uniform bool uHasMulti;
        uniform sampler2D uMultiTexture;
        uniform bool uHasMuscleDiffuse;
        uniform sampler2D uMuscleDiffuseTexture;
        uniform bool uHasMuscleMask;
        uniform sampler2D uMuscleMaskTexture;
        uniform bool uHasMuscleNormal;
        uniform sampler2D uMuscleNormalTexture;
        uniform bool uHasMuscleMulti;
        uniform sampler2D uMuscleMultiTexture;
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
            vec3 encodedNormal = texture(uNormalTexture, normalUv).xyz;
            if (uSkinMaterial && uHasMuscleNormal)
            {
                encodedNormal = mix(
                    encodedNormal,
                    texture(uMuscleNormalTexture, normalUv).xyz,
                    uMuscularity);
            }
            vec3 tangentNormal = encodedNormal * 2.0 - 1.0;
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

            vec3 lightDirection = normalize(uLightDirection);
            if (uDemoLighting && uSkinMaterial)
            {
                // Capture event 1332, material IDs 4/13: the skin branch
                // quarters the vertical component before normalizing.
                lightDirection = normalize(vec3(
                    uLightDirection.x,
                    uLightDirection.y * 0.25,
                    uLightDirection.z));
            }
            float diffuse = max(dot(normal, lightDirection), 0.0);
            float hemisphere = 0.38 + 0.22 * (normal.y * 0.5 + 0.5);
            vec4 albedo = uHasTexture
                ? texture(uDiffuseTexture, selectUv(uDiffuseUvSet))
                : uBaseColor;
            if (uSkinMaterial && uHasMuscleDiffuse)
            {
                albedo = mix(
                    albedo,
                    texture(uMuscleDiffuseTexture, selectUv(uDiffuseUvSet)),
                    uMuscularity);
            }
            if (uHasMask)
            {
                vec4 mask = texture(uMaskTexture, selectUv(uMaskUvSet));
                if (uSkinMaterial && uHasMuscleMask)
                {
                    mask = mix(
                        mask,
                        texture(uMuscleMaskTexture, selectUv(uMaskUvSet)),
                        uMuscularity);
                }
                mask *= uColorChannels;
                albedo.rgb = colorize(albedo.rgb, uColor1.rgb, mask.r);
                albedo.rgb = colorize(albedo.rgb, uColor2.rgb, mask.g);
                albedo.rgb = colorize(albedo.rgb, uColor3.rgb, mask.b);
                albedo.rgb = colorize(albedo.rgb, uColor4.rgb, mask.a);
            }
            // PSO2's ObjectCustomPS constant u_SkinWet is the signed
            // character skinGloss value normalized by 127. The captured skin
            // shader darkens only positive wetness, by at most 20 percent.
            float skinWet = uSkinMaterial
                ? clamp(uSkinGloss, -1.0, 1.0)
                : 0.0;
            float wet = max(skinWet, 0.0);
            albedo.rgb *= 1.0 - 0.2 * wet;
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
            if (uSkinMaterial && uHasMuscleMulti)
            {
                multi = mix(
                    multi,
                    texture(uMuscleMultiTexture, selectUv(uMultiUvSet)),
                    uMuscularity);
            }
            float ambientOcclusion = uHasMulti ? multi.b : 1.0;
            // Skin's _s texture is PSO2's SpecMaskTex. The captured skin
            // pixel shader feeds its green channel to the wet/dry transform
            // directly. The 0.2..1.0 remap is only the generic material
            // preview convention and would mute the skin wetness response.
            float roughness = uSkinMaterial
                ? multi.g
                : mix(0.2, 1.0, multi.g);
            // Captured PSO2 skin shader:
            //   rough *= 1 - 0.7 * saturate(u_SkinWet)
            //   rough += saturate(-u_SkinWet) * (1 - rough)
            roughness *= 1.0 - 0.7 * wet;
            float dry = max(-skinWet, 0.0);
            roughness = mix(roughness, 1.0, dry);
            float metallic = multi.r;
            vec3 viewDirection = normalize(uCameraPosition - vPosition);
            vec3 halfDirection = normalize(lightDirection + viewDirection);
            float specularPower = mix(96.0, 6.0, roughness);
            float specular = pow(max(dot(normal, halfDirection), 0.0), specularPower);
            vec3 specularColor = mix(vec3(0.04), albedo.rgb, metallic);
            vec3 lit;
            if (uDemoLighting)
            {
                // Use a heavily blurred level of the procedural studio
                // environment for the broad indirect term.
                vec3 environmentDirection = vec3(normal.x, normal.y, -normal.z);
                vec3 indirect = textureLod(
                    uDemoEnvironment, environmentDirection, 7.5).rgb * 0.5;
                indirect += vec3(0.0375, 0.075, 0.125) * max(normal.y, 0.0);
                float direct = diffuse * 12.56 * 0.159160;

                vec3 reflectionDirection = reflect(-viewDirection, normal);
                float environmentLod = mix(2.0, 8.0, roughness);
                vec3 environmentSpecular = textureLod(
                    uDemoEnvironment,
                    vec3(reflectionDirection.x, reflectionDirection.y, -reflectionDirection.z),
                    environmentLod).rgb;
                float viewFresnel = pow(1.0 - max(dot(normal, viewDirection), 0.0), 5.0);
                vec3 fresnel = specularColor + (vec3(1.0) - specularColor) * viewFresnel;

                lit = albedo.rgb * (indirect * ambientOcclusion + direct) +
                      environmentSpecular * fresnel * ambientOcclusion * 0.35 +
                      specularColor * specular * 0.12;
            }
            else
            {
                lit = albedo.rgb * (hemisphere * ambientOcclusion + diffuse * 0.55) +
                      specularColor * specular * (0.2 + metallic * 0.35);
            }
            vec3 outColor = lit + albedo.rgb * multi.a;
            // Textures are sampled through SRGB8 (linear values) and the
            // palette arrives linear, but the swapchain is not an sRGB
            // target - encode on the way out or every midtone reads dark.
            float outputAlpha = uBlendMode >= 2 ? albedo.a : 1.0;
            FragColor = vec4(uDemoLighting ? max(outColor, vec3(0.0))
                : pow(clamp(outColor, 0.0, 1.0), vec3(1.0 / 2.2)), outputAlpha);
        }
        """;
    private const string ToneVertexShader = """
        #version 300 es
        precision highp float;
        out vec2 vUv;
        void main() {
            vUv = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(vUv * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string ToneFragmentShader = """
        #version 300 es
        precision highp float;
        uniform sampler2D uScene;
        in vec2 vUv;
        out vec4 FragColor;
        void main() {
            vec3 x = max(texture(uScene, vUv).rgb, vec3(0.0)) * 0.84;
            vec3 mapped = clamp(x * (3.0 * x + 0.03) / (x * (3.0 * x + 1.0) + 0.14), 0.0, 1.0);
            FragColor = vec4(pow(mapped, vec3(1.0 / 2.2)), 1.0);
        }
        """;
}
