using System.Numerics;
using AquaModelLibrary.Data.PSO2.Aqua;
using Pso2ShapeStudio.Character;

namespace Pso2ShapeStudio.Formats;

public static class AqpLoader
{
    public static RenderModel Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var textures = Directory.EnumerateFiles(directory, "*.dds", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                file => Path.GetFileName(file)!,
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

        return Load(File.ReadAllBytes(fullPath), fullPath, textures);
    }

    public static RenderModel Load(
        ReadOnlyMemory<byte> packageData,
        string sourceName,
        IReadOnlyDictionary<string, byte[]>? textureData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var package = new AquaPackage(packageData.ToArray());
        if (package.models.Count == 0)
        {
            throw new InvalidDataException($"AQP contains no models: {sourceName}");
        }

        var model = package.models[0];

        // Past 0xC32 (NGS is 0xC33) one vertex buffer serves several meshes,
        // so vtxlList and meshList do not line up until this splits them.
        if (model.objc.type > 0xC32)
        {
            model.splitVSETPerMesh();
        }

        // Fills trueVertWeights / trueVertWeightIndices - the normalised form
        // of the raw weights. Required; see the loop that consumes them.
        model.CreateTrueVertWeights();

        var genericMaterials = model.GetUniqueMaterials(out var meshMaterialMapping);
        var decodedTextures = new Dictionary<string, RenderTexture>(StringComparer.OrdinalIgnoreCase);
        var materials = genericMaterials.Select(material =>
        {
            var logicalTextures = material.texNames?.Select(value => value?.ToString() ?? "").ToArray() ?? [];
            var logicalUvSets = material.texUVSets?.ToArray() ?? [];
            RenderTexture? Decode(string? textureName)
            {
                if (textureName is null || textureData is null)
                {
                    return null;
                }

                if (!decodedTextures.TryGetValue(textureName, out var texture))
                {
                    texture = DdsTextureDecoder.Decode(textureName, textureData[textureName]);
                    decodedTextures.Add(textureName, texture);
                }

                return texture;
            }

            var availableNames = textureData?.Keys ?? [];
            var diffuseReference = ResolveTexture(logicalTextures, logicalUvSets, availableNames, TextureSlot.Diffuse);
            var maskReference = ResolveTexture(logicalTextures, logicalUvSets, availableNames, TextureSlot.Mask);
            var normalReference = ResolveTexture(logicalTextures, logicalUvSets, availableNames, TextureSlot.Normal);
            var multiReference = ResolveTexture(logicalTextures, logicalUvSets, availableNames, TextureSlot.Multi);
            var diffuse = Decode(diffuseReference?.Name);
            var mask = Decode(maskReference?.Name);
            var normal = Decode(normalReference?.Name);
            var multi = Decode(multiReference?.Name);
            var usesSkinTexture = IsSkinMaterial(material.shaderNames, logicalTextures);
            return new RenderMaterial(
                material.matName?.ToString() ?? "material",
                ClampColor(material.diffuseRGBA),
                diffuse,
                material.twoSided != 0,
                checked((byte)Math.Clamp(material.alphaCutoff, 0, byte.MaxValue)),
                usesSkinTexture,
                mask,
                normal,
                multi,
                ResolveColorMapping(logicalTextures, usesSkinTexture),
                new RenderTextureUvSets(
                    diffuseReference?.UvSet ?? 0,
                    maskReference?.UvSet ?? 0,
                    normalReference?.UvSet ?? 0,
                    multiReference?.UvSet ?? 0),
                ResolveBlendMode(material.blendType));
        }).ToArray();

        if (model.vtxlList.Count != model.meshList.Count ||
            model.strips.Count != model.meshList.Count)
        {
            throw new InvalidDataException(
                $"Mesh buffers are not aligned after splitting: " +
                $"vtxl={model.vtxlList.Count}, mesh={model.meshList.Count}, strips={model.strips.Count}");
        }

        // Bone palettes live in two places with two widths: per vertex buffer
        // as ushort on split models, and on the object as uint when the model
        // was never split. Take whichever is populated - see the per-mesh
        // choice below. A weight index is an index INTO the palette, and the
        // palette entry is the AQN node id; two hops, not one.
        var objectPalette = model.bonePalette.Select(value => (int)value).ToArray();
        var meshes = new RenderMesh[model.meshList.Count];

        for (var meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
        {
            var source = model.vtxlList[meshIndex];
            var mesh = model.meshList[meshIndex];
            var positions = source.vertPositions.ToArray();
            var normals = CopyOrFill(source.vertNormals, positions.Length, Vector3.UnitZ);
            var uv = CopyOrFill(source.uv1List, positions.Length, Vector2.Zero);
            var uv2 = CopyOrFallback(source.uv2List, positions.Length, uv);
            var uv3 = CopyOrFallback(source.uv3List, positions.Length, uv);
            var palette = source.bonePalette.Count > 0
                ? source.bonePalette.Select(value => (int)value).ToArray()
                : objectPalette;

            // trueVertWeights, not vertWeights: the raw list is NOT normalised
            // - a measured vertex reads (0.994, 0.113, 0.0005, 0), summing to
            // 1.107. Skinning with those inflates the mesh by a few millimetres
            // everywhere, which looks like a rigging bug rather than a parsing
            // one. The "true" lists are the same weights divided through.
            var weights = new Vector4[positions.Length];
            var indices = new Byte4[positions.Length];
            for (var vertexIndex = 0; vertexIndex < positions.Length; vertexIndex++)
            {
                if (source.trueVertWeights.Count > vertexIndex &&
                    source.trueVertWeightIndices.Count > vertexIndex)
                {
                    weights[vertexIndex] = source.trueVertWeights[vertexIndex];
                    indices[vertexIndex] = ToByte4(source.trueVertWeightIndices[vertexIndex]);
                }
                else
                {
                    weights[vertexIndex] = new Vector4(1, 0, 0, 0);
                    indices[vertexIndex] = new Byte4(0, 0, 0, 0);
                }
            }

            // AML tracks whether this PSET stores a real strip or a flat NGS
            // triangle list and applies the correct conversion for both.
            var sourceTriangles = model.strips[meshIndex].GetTriangles();
            var triangles = new int[checked(sourceTriangles.Count * 3)];
            for (var index = 0; index < sourceTriangles.Count; index++)
            {
                var triangle = sourceTriangles[index];
                var destination = index * 3;
                triangles[destination] = checked((int)triangle.X);
                triangles[destination + 1] = checked((int)triangle.Y);
                triangles[destination + 2] = checked((int)triangle.Z);
            }

            if (triangles.Length > 0 && triangles.Max() >= positions.Length)
            {
                throw new InvalidDataException(
                    $"Triangle index exceeds vertex count in mesh {meshIndex}.");
            }

            meshes[meshIndex] = new RenderMesh(
                positions,
                normals,
                uv,
                weights,
                indices,
                triangles,
                palette,
                $"mesh_{meshIndex:000}",
                meshIndex < meshMaterialMapping.Count
                    ? meshMaterialMapping[meshIndex]
                    : mesh.mateIndex,
                uv2,
                uv3);
        }

        return new RenderModel(sourceName, meshes, materials, DetectBodyType(sourceName));
    }

    public static Pso2BodyType DetectBodyType(string sourceName)
    {
        const string marker = "pl_rbd_";
        var markerIndex = sourceName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return Pso2BodyType.Unknown;
        }

        var idStart = markerIndex + marker.Length;
        if (idStart + 6 > sourceName.Length ||
            !int.TryParse(sourceName.AsSpan(idStart, 6), out var id))
        {
            return Pso2BodyType.Unknown;
        }

        return id switch
        {
            >= 100000 and < 200000 => Pso2BodyType.Type1,
            >= 200000 and < 300000 => Pso2BodyType.Type2,
            _ => Pso2BodyType.Unknown,
        };
    }

    /// <summary>
    /// Preserve PSO2's four distinct render paths. In particular, hollow is
    /// alpha-tested rather than blended, and opaque materials must not consume
    /// diffuse alpha. Treating all of them as SrcAlpha blending makes tiny
    /// compressed-alpha values leak the surface underneath as bright specks.
    /// </summary>
    private static MaterialBlendMode ResolveBlendMode(string? blendType) =>
        blendType?.ToLowerInvariant() switch
        {
            "hollow" => MaterialBlendMode.Cutout,
            "blendalpha" => MaterialBlendMode.AlphaBlend,
            "add" => MaterialBlendMode.Additive,
            _ => MaterialBlendMode.Opaque,
        };

    private static bool IsSkinMaterial(
        IEnumerable<string>? shaderNames,
        IEnumerable<string> logicalTextures) =>
        shaderNames?.Any(name =>
            string.Equals(name, "1102", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "1102p", StringComparison.OrdinalIgnoreCase)) == true ||
        logicalTextures.Any(name =>
            name.Contains("pl_body_skin_", StringComparison.OrdinalIgnoreCase));

    private static Vector4 ClampColor(Vector4 color)
    {
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) ||
            !float.IsFinite(color.Z) || !float.IsFinite(color.W))
        {
            return new Vector4(0.63f, 0.67f, 0.72f, 1f);
        }

        return Vector4.Clamp(color, Vector4.Zero, Vector4.One);
    }

    private static ResolvedTexture? ResolveTexture(
        IReadOnlyList<string> logicalTextures,
        IReadOnlyList<int> logicalUvSets,
        IEnumerable<string> availableNames,
        TextureSlot slot)
    {
        var available = availableNames.ToArray();
        var logicalIndex = -1;
        for (var index = 0; index < logicalTextures.Count; index++)
        {
            if (IsLogicalSlot(logicalTextures[index], slot))
            {
                logicalIndex = index;
                break;
            }
        }

        if (logicalIndex < 0)
        {
            return null;
        }

        var logical = logicalTextures[logicalIndex];
        var uvSet = logicalIndex < logicalUvSets.Count && logicalUvSets[logicalIndex] is >= 0 and <= 2
            ? logicalUvSets[logicalIndex]
            : 0;

        var exact = available.FirstOrDefault(name =>
            string.Equals(Path.GetFileName(name), Path.GetFileName(logical), StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new ResolvedTexture(exact, uvSet);
        }

        var categories = TextureCategoryTokens(logical);
        foreach (var suffix in SlotSuffixes(slot, logical))
        {
            var match = available.FirstOrDefault(name =>
            {
                var stem = Path.GetFileNameWithoutExtension(name);
                if (!stem.EndsWith($"_{suffix}", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return categories.Count == 0 || categories.Any(token =>
                    stem.Contains($"_{token}_", StringComparison.OrdinalIgnoreCase));
            });
            if (match is not null)
            {
                return new ResolvedTexture(match, uvSet);
            }
        }

        return null;
    }

    private static bool IsLogicalSlot(string name, TextureSlot slot)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        return slot switch
        {
            TextureSlot.Diffuse => name.Contains("diffuse", StringComparison.OrdinalIgnoreCase) ||
                                   stem.EndsWith("_d", StringComparison.OrdinalIgnoreCase),
            TextureSlot.Mask => name.Contains("mask", StringComparison.OrdinalIgnoreCase) ||
                                stem.EndsWith("_m", StringComparison.OrdinalIgnoreCase),
            TextureSlot.Normal => !name.Contains("subnormal", StringComparison.OrdinalIgnoreCase) &&
                                  (name.Contains("normal", StringComparison.OrdinalIgnoreCase) ||
                                   stem.EndsWith("_n", StringComparison.OrdinalIgnoreCase)),
            TextureSlot.Multi => name.Contains("multi", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("specular", StringComparison.OrdinalIgnoreCase) ||
                                 stem.EndsWith("_s", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static IReadOnlyList<char> SlotSuffixes(TextureSlot slot, string logicalName) =>
        slot switch
        {
            TextureSlot.Diffuse => ['d'],
            TextureSlot.Mask when logicalName.Contains("body_base", StringComparison.OrdinalIgnoreCase) => ['m', 'l'],
            TextureSlot.Mask => ['m'],
            TextureSlot.Normal => ['n'],
            TextureSlot.Multi => ['s'],
            _ => [],
        };

    private static Pso2ColorMapping ResolveColorMapping(
        IReadOnlyList<string> logicalTextures,
        bool usesSkinTexture)
    {
        if (usesSkinTexture)
        {
            return new Pso2ColorMapping(Pso2ColorChannel.MainSkin, Pso2ColorChannel.SubSkin);
        }

        var joined = string.Join('|', logicalTextures);
        if (joined.Contains("body_base", StringComparison.OrdinalIgnoreCase))
        {
            return new Pso2ColorMapping(Pso2ColorChannel.Base1, Pso2ColorChannel.Base2);
        }
        if (joined.Contains("body_outer", StringComparison.OrdinalIgnoreCase))
        {
            return new Pso2ColorMapping(Pso2ColorChannel.Outer1, Pso2ColorChannel.Outer2);
        }
        if (joined.Contains("face", StringComparison.OrdinalIgnoreCase) ||
            joined.Contains("ears", StringComparison.OrdinalIgnoreCase))
        {
            return new Pso2ColorMapping(Pso2ColorChannel.MainSkin, Pso2ColorChannel.SubSkin);
        }
        if (joined.Contains("hair", StringComparison.OrdinalIgnoreCase))
        {
            return new Pso2ColorMapping(Pso2ColorChannel.Hair1, Pso2ColorChannel.Hair2);
        }
        if (joined.Contains("eyebrow", StringComparison.OrdinalIgnoreCase))
        {
            return new Pso2ColorMapping(Pso2ColorChannel.Eyebrow, Pso2ColorChannel.Unused);
        }
        if (joined.Contains("eyelash", StringComparison.OrdinalIgnoreCase))
        {
            return new Pso2ColorMapping(Pso2ColorChannel.Eyelash, Pso2ColorChannel.Unused);
        }

        return default;
    }

    private static IReadOnlyList<string> TextureCategoryTokens(string logicalName)
    {
        var value = logicalName.ToLowerInvariant();
        if (value.Contains("body_base")) return ["bw", "bd", "ow", "iw"];
        if (value.Contains("body_outer")) return ["ow"];
        if (value.Contains("body_skin")) return ["sk"];
        if (value.Contains("face")) return ["fc", "f1", "f2"];
        if (value.Contains("hair")) return ["hr"];
        if (value.Contains("eyebrow")) return ["eb"];
        if (value.Contains("eyelash")) return ["el"];
        if (value.Contains("eye")) return ["ey"];
        if (value.Contains("ears")) return ["ea"];
        if (value.Contains("dental")) return ["de"];
        return [];
    }

    private static T[] CopyOrFill<T>(IReadOnlyCollection<T> source, int count, T fallback)
    {
        if (source.Count == count)
        {
            return source.ToArray();
        }

        return Enumerable.Repeat(fallback, count).ToArray();
    }

    private static T[] CopyOrFallback<T>(IReadOnlyCollection<T> source, int count, IReadOnlyList<T> fallback)
    {
        if (source.Count == count)
        {
            return source.ToArray();
        }

        if (fallback.Count != count)
        {
            throw new ArgumentException("Fallback vertex channel does not match the mesh vertex count.", nameof(fallback));
        }

        return fallback.ToArray();
    }

    private static Byte4 ToByte4(IReadOnlyList<int> source)
    {
        static byte Read(IReadOnlyList<int> values, int index)
        {
            if (index >= values.Count)
            {
                return 0;
            }

            return checked((byte)values[index]);
        }

        return new Byte4(Read(source, 0), Read(source, 1), Read(source, 2), Read(source, 3));
    }

    private enum TextureSlot
    {
        Diffuse,
        Mask,
        Normal,
        Multi,
    }

    private sealed record ResolvedTexture(string Name, int UvSet);
}
