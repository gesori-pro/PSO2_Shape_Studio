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
        uniform bool uGameMaterial;
        uniform samplerCube uGameEnvironment;
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

        vec3 srgbEncode(vec3 color)
        {
            color = max(color, vec3(0.0));
            return mix(color * 12.92, 1.055 * pow(color, vec3(1.0 / 2.4)) - 0.055,
                       step(0.0031308, color));
        }

        vec3 srgbDecode(vec3 color)
        {
            color = max(color, vec3(0.0));
            return mix(color / 12.92, pow((color + 0.055) / 1.055, vec3(2.4)),
                       step(0.04045, color));
        }

        // The game composites a character's textures once, at load, in
        // sRGB space (reboot_*_diffuse): the diffuse as stored, each colour
        // as its 0-255 value over 255 squeezed to 0.005..0.995, blended over
        // it in turn. Skin (multiply) builds the same blend from white and
        // multiplies the texture by it once. Blended as linear values
        // instead, a part-strength mask moves the colour far less: skin came
        // out paler, its blood colour barely showing.
        vec3 colorize(vec3 linearColor, vec4 mask)
        {
            vec3 source = srgbEncode(linearColor);
            vec3 layered = uMultiplyColor ? vec3(1.0) : source;
            layered = mix(layered, srgbEncode(uColor1.rgb) * 0.99 + 0.005, clamp(mask.r, 0.0, 1.0));
            layered = mix(layered, srgbEncode(uColor2.rgb) * 0.99 + 0.005, clamp(mask.g, 0.0, 1.0));
            layered = mix(layered, srgbEncode(uColor3.rgb) * 0.99 + 0.005, clamp(mask.b, 0.0, 1.0));
            layered = mix(layered, srgbEncode(uColor4.rgb) * 0.99 + 0.005, clamp(mask.a, 0.0, 1.0));
            return srgbDecode(uMultiplyColor ? source * layered : layered);
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

        // ---- Demo lighting: the game's own shading. What the character
        // creator's G-buffer shaders (1100g costume, 1102g skin) make of a
        // material, lit the way its deferred lighting pass lights models 0
        // and 4, with the creator's headlight. The same as the Blender
        // add-on's game shading, which matched the lighting pass of a
        // captured creator frame to 1.2% (median per pixel). There are no
        // shadows or screen-space occlusion here: every point sees the sun.
        const float PI = 3.14159265;
        // Every character shader darkens the diffuse texture by this much.
        const float ALBEDO_SCALE = 0.61;
        // How far a soft area wraps light round to its dark side.
        const float SKIN_SUBSURFACE = 0.153;
        const float COSTUME_SUBSURFACE = 0.25;
        // What skin's wrapped light is tinted by: a deep red, squared.
        const vec3 SKIN_SCATTER = vec3(0.31557, 0.00017, 0.0);
        const float EXPOSURE = 0.84;
        const float SUN_STRENGTH = 12.56;
        const vec3 SKY_COLOR = vec3(0.0375, 0.075, 0.125);
        // The point light on the creator's camera: 1.665 / (1 + 0.24 d).
        const float HEADLIGHT = 1.665;
        const float HEADLIGHT_ATTENUATION = 0.24;

        float normalAlpha()
        {
            if (!uHasNormal)
            {
                return 1.0;
            }
            vec2 normalUv = selectUv(uNormalUvSet);
            float alpha = texture(uNormalTexture, normalUv).a;
            if (uSkinMaterial && uHasMuscleNormal)
            {
                alpha = mix(alpha, texture(uMuscleNormalTexture, normalUv).a, uMuscularity);
            }
            return alpha;
        }

        float smithG1(float x, float a2)
        {
            float inner = x * x * (1.0 - a2) + a2;
            return 2.0 * x * x / (inner * inner + x);
        }

        // GGX with the game's own geometry term and Fresnel, times N.L.
        vec3 gameSpecular(vec3 n, vec3 v, vec3 l, float ndl, vec3 f0, float a2)
        {
            vec3 h = normalize(v + l);
            float ndv = max(dot(n, v), 1e-3);
            float ndh = max(dot(n, h), 1e-3);
            float vdh = max(dot(v, h), 1e-3);
            vec3 fresnel = f0 + (1.0 - f0) * exp2(-12.5378895 * vdh);
            float den = ndh * ndh * (a2 - 1.0) + 1.0;
            float d = a2 / (PI * den * den);
            float g = smithG1(ndv, a2) * smithG1(ndl, a2);
            return fresnel * (d * g / (4.0 * ndv * ndl + 1e-7) * ndl);
        }

        // Lambert on what is not soft; the soft part wraps round by its
        // amount and takes the tint.
        vec3 gameDiffuse(float ndl, float soft, float occlusion, vec3 tint)
        {
            float wrap = max((ndl + 2.0) * soft / 3.0, 0.0);
            return tint * wrap + vec3(clamp(ndl, 0.0, 1.0) * occlusion * (1.0 - soft));
        }

        vec3 gameShade(vec3 colorized, vec4 multi, vec3 n, vec3 vertexNormal)
        {
            vec3 v = normalize(uCameraPosition - vPosition);

            // The normal map's alpha marks soft areas: at or below one half
            // they wrap by the full amount, fading out towards one.
            float softAlpha = clamp(normalAlpha() * 1.02, 0.0, 1.0);
            float hardness = clamp(softAlpha * 2.0 - 1.0, 0.0, 1.0);
            float subsurface = (1.0 - hardness) *
                (uSkinMaterial ? SKIN_SUBSURFACE : COSTUME_SUBSURFACE);

            // Skin gloss (the signed skinGloss / 127), on the body only where
            // it is soft: wet is up to 70% glossier and 20% darker, dry
            // goes towards fully rough.
            float roughness = multi.g;
            float wetColor = 1.0;
            if (uSkinMaterial)
            {
                float wet = clamp(uSkinGloss, -1.0, 1.0) * clamp(subsurface * 8.0, 0.0, 1.0);
                float wetter = clamp(wet, 0.0, 1.0);
                roughness = mix(roughness * (1.0 - 0.7 * wetter), 1.0, clamp(-wet, 0.0, 1.0));
                wetColor = 1.0 - 0.2 * wetter;
            }

            // The multi map's alpha glows above 0.02; what glows is taken
            // out of the diffuse and carries no subsurface.
            vec3 albedo = colorized * ALBEDO_SCALE;
            float gate = clamp(multi.a - 0.02, 0.0, 1.0);
            float glowing = gate < 1e-3 ? 0.0 : 1.0;
            float soft = subsurface * (1.0 - glowing);
            vec3 glow = pow(
                75.0 * gate * gate * (albedo * albedo * 0.995 + 0.005),
                vec3(1.3)) / EXPOSURE;
            vec3 scatter = uSkinMaterial ? SKIN_SCATTER * wetColor : albedo;
            scatter *= soft > 1.0 / 255.0 - 1e-6 ? 1.0 : 0.0;
            scatter = mix(scatter, glow, glowing);
            vec3 base = albedo * (1.0 - gate) * wetColor;

            // Soft silhouettes take light from behind.
            float rimMask = clamp(softAlpha * -2.0 + 1.0, 0.0, 1.0);
            float facing = 1.0 - dot(vertexNormal, v);
            float rim = min(facing * facing * abs(facing) * rimMask, 1.0) * soft;

            float metal = multi.r * multi.r;
            float occlusion = multi.b;
            vec3 diffuseColor = base * (1.0 - metal);
            vec3 f0 = mix(vec3(0.04), base, metal);
            vec3 tintA = base * (1.0 - soft) + scatter * soft * soft;
            vec3 tintB = base * (1.0 - soft) + scatter * soft;
            float alpha = pow(max(roughness, 0.05), 2.0);
            float a2 = alpha * alpha;

            // Characters never see the sun dimmer than 1 / (0.7 exposure),
            // and take a fill of 0.3 of that from the opposite side.
            vec3 sun = normalize(uLightDirection);
            // Skin sees it with its vertical component cut to a quarter.
            vec3 l = uSkinMaterial ? normalize(vec3(sun.x, sun.y * 0.25, sun.z)) : sun;
            float floorLight = 1.0 / (0.7 * EXPOSURE);
            float sunLight = max(SUN_STRENGTH, floorLight);
            float fill = 0.3 * floorLight;

            // the sun
            float ndl = dot(n, l);
            float ndlS = clamp(ndl, 0.0, 1.0);
            vec3 direct = gameDiffuse(ndl, soft, occlusion, tintA) +
                          tintB * ((0.5 - 0.5 * ndl) * rim);
            direct *= sunLight * diffuseColor;

            // the ambient: the environment round the normal (the game reads
            // its cube at mip 7.5, here the two levels around it) and the
            // sky tint from above
            vec3 environment = textureLod(uGameEnvironment, vec3(n.x, n.y, -n.z), 0.5).rgb;
            float up = clamp(n.y * 0.5 + 0.5, 0.0, 1.0);
            vec3 indirect = mix(base, scatter, soft * 0.5) *
                            (environment + SKY_COLOR * up) * occlusion;

            vec3 color = (direct / PI + indirect) * (1.0 - metal) +
                         gameSpecular(n, v, l, ndlS, f0, a2) * sunLight;

            // the fill: colourless, from the opposite side
            float ndl2 = dot(n, -l);
            color += gameDiffuse(ndl2, soft, occlusion, tintA) * diffuseColor *
                     (fill * (1.0 - metal) / PI);
            color += gameSpecular(n, v, -l, clamp(ndl2, 0.0, 1.0), f0, a2) * (fill * 0.5);

            // the headlight, from the camera: no light from behind, and the
            // occlusion on its specular only
            float lamp = HEADLIGHT /
                ((1.0 + HEADLIGHT_ATTENUATION * length(uCameraPosition - vPosition)) * EXPOSURE);
            float ndl3 = dot(n, v);
            color += gameDiffuse(ndl3, soft, 1.0, tintA) * diffuseColor *
                     (lamp * (1.0 - metal) / PI);
            color += gameSpecular(n, v, v, clamp(ndl3, 0.0, 1.0), f0, a2) * (lamp * occlusion);

            // glow: what is not soft carries emission in the same channel
            color += scatter * (1.0 - clamp(soft * 1e5, 0.0, 1.0));
            return max(color, vec3(0.0));
        }

        void main()
        {
            vec3 geometryNormal = normalize(vNormal);
            vec3 normal = geometryNormal;
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
                albedo.rgb = colorize(albedo.rgb, mask * uColorChannels);
            }
            vec3 colorized = albedo.rgb;
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
            float outputAlpha = uBlendMode >= 2 ? albedo.a : 1.0;
            if (uDemoLighting && uGameMaterial)
            {
                FragColor = vec4(gameShade(colorized, multi, normal, geometryNormal), outputAlpha);
                return;
            }

            // Below: the plain viewport lighting, and the floor guide's in
            // demo lighting.
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
                // environment for the broad indirect term (the floor guide).
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
        // what the background was cleared to; alpha 0 marks it
        uniform vec3 uBackground;
        in vec2 vUv;
        out vec4 FragColor;
        // The creator's glare (capture frame 16602): the frame blends 5%
        // towards a blur of itself, a Gaussian of 0.178 of its height, at
        // 0.72 of the brightness. The blur is read from the frame's mips:
        // 9 x 9 taps half a sigma apart, each from the mip whose texels are
        // that size. The viewport's background is not the creator's: where
        // the blur takes it in, it takes the creator's grey backdrop (1.13
        // before tone mapping) instead - a white one lifted every dark
        // surface grey - and the background itself keeps its colour.
        const float GLARE_RATE = 0.05;
        const float GLARE_GAIN = 0.72;
        const float GLARE_SIGMA = 0.178;
        const float CREATOR_BACKDROP = 1.13;
        vec3 glare() {
            vec2 size = vec2(textureSize(uScene, 0));
            float sigma = GLARE_SIGMA * size.y;
            float lod = log2(max(sigma * 0.5, 1.0));
            vec2 step = vec2(sigma * 0.5) / size;
            vec4 sum = vec4(0.0);
            float total = 0.0;
            for (int j = -4; j <= 4; j++) {
                for (int i = -4; i <= 4; i++) {
                    float weight = exp(-0.125 * float(i * i + j * j));
                    sum += textureLod(uScene, vUv + vec2(float(i), float(j)) * step, lod) * weight;
                    total += weight;
                }
            }
            sum /= total;
            return sum.rgb + (1.0 - clamp(sum.a, 0.0, 1.0)) * (vec3(CREATOR_BACKDROP) - uBackground);
        }
        void main() {
            vec4 center = texelFetch(uScene, ivec2(gl_FragCoord.xy), 0);
            vec3 scene = center.rgb;
            if (center.a > 0.0) {
                vec3 glared = scene * (1.0 - GLARE_RATE) + glare() * (GLARE_RATE * GLARE_GAIN);
                scene = mix(scene, glared, min(center.a, 1.0));
            }
            vec3 x = max(scene, vec3(0.0)) * 0.84;
            vec3 mapped = clamp(x * (3.0 * x + 0.03) / (x * (3.0 * x + 1.0) + 0.14), 0.0, 1.0);
            FragColor = vec4(pow(mapped, vec3(1.0 / 2.2)), 1.0);
        }
        """;
}
