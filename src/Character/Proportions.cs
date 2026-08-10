using System.Text.Json;

namespace Pso2ShapeStudio.Character;

public sealed class BoneProportion
{
    public int? Index { get; set; }

    public double[] Scale { get; set; } = [1.0, 1.0, 1.0];

    public double[] Pos { get; set; } = [0.0, 0.0, 0.0];

    public double[] RotQuat { get; set; } = [0.0, 0.0, 0.0, 1.0];
}

public sealed record ProportionSlider(string Key, string Field, int Value);

public sealed record ProportionResult(
    string Source,
    IReadOnlyList<ProportionSlider> Sliders,
    int OutfitAdjustBones,
    IReadOnlyDictionary<string, BoneProportion> Bones);

public static class Proportions
{
    private const string MinimumKey = "atMin(slider=-127)";
    private const string MaximumKey = "atMax(slider=+127)";

    private static readonly HashSet<string> TopLevelFields =
    [
        "neckVerts.X", "neckVerts.Y", "neckVerts.Z",
        "waistVerts.X", "waistVerts.Y", "waistVerts.Z",
        "hands.X", "hands.Y", "hands.Z", "neckAngle",
    ];

    public static ProportionResult Compute(CharacterFile character, bool applyOutfitAdjust = true)
    {
        var female = Convert.ToInt32(character["baseDOC.gender"]) == 1;
        var source = female ? "pl_cmakemot_b_fh_rb.json" : "pl_cmakemot_b_mh_rb.json";
        using var stream = EmbeddedData.Open($"Data.Proportions.{source}");
        using var table = JsonDocument.Parse(stream);

        var accum = new Dictionary<string, BoneProportion>(StringComparer.Ordinal);
        var applied = new List<ProportionSlider>();
        foreach (var slider in table.RootElement.GetProperty("sliders").EnumerateObject())
        {
            if (slider.Value.ValueKind != JsonValueKind.Object ||
                !slider.Value.TryGetProperty("affectedBones", out var affected) ||
                affected.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var field = SliderToField(slider.Name);
            if (!character.Contains(field))
            {
                continue;
            }

            var value = Convert.ToInt32(character[field]);
            applied.Add(new ProportionSlider(slider.Name, field, value));
            foreach (var bone in affected.EnumerateObject())
            {
                var (index, name) = SplitBoneKey(bone.Name);
                var slot = Slot(accum, name, index);
                var low = bone.Value.GetProperty(MinimumKey);
                var high = bone.Value.GetProperty(MaximumKey);

                slot.Scale = Multiply(slot.Scale, Blend(
                    value,
                    [1.0, 1.0, 1.0],
                    Vector(low, "scaleMul"),
                    Vector(high, "scaleMul")));
                slot.Pos = Add(slot.Pos, Blend(
                    value,
                    [0.0, 0.0, 0.0],
                    Vector(low, "posDelta"),
                    Vector(high, "posDelta")));

                if (low.TryGetProperty("rotDeltaQuat", out var lowRotation))
                {
                    var rotation = BlendQuaternion(
                        value,
                        Vector(lowRotation),
                        Vector(high.GetProperty("rotDeltaQuat")));
                    slot.RotQuat = QuaternionMultiply(slot.RotQuat, rotation);
                }
            }
        }

        ApplyBaseTable(table.RootElement, "_baseCorrection", accum, (slot, value) =>
            slot.Scale = Multiply(slot.Scale, value));
        ApplyBaseTable(table.RootElement, "_baseCorrectionPos", accum, (slot, value) =>
            slot.Pos = Add(slot.Pos, value));
        ApplyBaseTable(table.RootElement, "_baseCorrectionRot", accum, (slot, value) =>
            slot.RotQuat = QuaternionMultiply(value, slot.RotQuat));

        var outfit = applyOutfitAdjust ? OutfitAdjust(character) : [];
        foreach (var (boneKey, multiplier) in outfit)
        {
            var (index, name) = SplitBoneKey(boneKey);
            var slot = Slot(accum, name, index);
            slot.Scale = Multiply(slot.Scale, multiplier);
        }

        return new ProportionResult(source, applied, outfit.Count, accum);
    }

    public static string SliderToField(string key)
    {
        var head = key.Split(' ')[0];
        if (key.EndsWith("(ngsSLID)", StringComparison.Ordinal))
        {
            return "ngsSLID." + head;
        }

        return TopLevelFields.Contains(head) ? head : "baseFIGR." + head;
    }

    public static double[] Blend(int value, double[] neutral, double[] atMinimum, double[] atMaximum)
    {
        var t = value / 127.0;
        var target = t < 0.0 ? atMinimum : atMaximum;
        t = Math.Abs(t);
        return neutral.Zip(target, (center, edge) => center + (edge - center) * t).ToArray();
    }

    public static double[] QuaternionMultiply(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var (ax, ay, az, aw) = (left[0], left[1], left[2], left[3]);
        var (bx, by, bz, bw) = (right[0], right[1], right[2], right[3]);
        return
        [
            aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz,
        ];
    }

    public static double[] QuaternionScale(IReadOnlyList<double> quaternion, double t)
    {
        var (x, y, z, w) = (quaternion[0], quaternion[1], quaternion[2], quaternion[3]);
        if (w < 0.0)
        {
            (x, y, z, w) = (-x, -y, -z, -w);
        }

        w = Math.Clamp(w, -1.0, 1.0);
        var half = Math.Acos(w);
        var sine = Math.Sin(half);
        if (sine < 1e-9)
        {
            return [0.0, 0.0, 0.0, 1.0];
        }

        var a = Math.Sin((1.0 - t) * half) / sine;
        var b = Math.Sin(t * half) / sine;
        return [x * b, y * b, z * b, a + w * b];
    }

    private static double[] BlendQuaternion(int value, double[] atMinimum, double[] atMaximum)
    {
        var t = value / 127.0;
        return QuaternionScale(t < 0.0 ? atMinimum : atMaximum, Math.Abs(t));
    }

    private static void ApplyBaseTable(
        JsonElement root,
        string property,
        Dictionary<string, BoneProportion> accum,
        Action<BoneProportion, double[]> apply)
    {
        if (!root.TryGetProperty(property, out var table))
        {
            return;
        }

        foreach (var bone in table.EnumerateObject())
        {
            var (index, name) = SplitBoneKey(bone.Name);
            apply(Slot(accum, name, index), Vector(bone.Value));
        }
    }

    private static Dictionary<string, double[]> OutfitAdjust(CharacterFile character)
    {
        using var adjustStream = EmbeddedData.Open("Data.Proportions.outfit_adjust.json");
        using var linksStream = EmbeddedData.Open("Data.Proportions.cmx_idlinks.json");
        using var adjust = JsonDocument.Parse(adjustStream);
        using var links = JsonDocument.Parse(linksStream);
        if (!character.Contains("baseSLCT.costumePart"))
        {
            return [];
        }

        var costume = Convert.ToInt32(character["baseSLCT.costumePart"]).ToString();
        var result = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var (slot, linkTable) in new[]
                 {
                     ("ow", "outerWearIdLink"), ("bw", "baseWearIdLink"), ("b1", "innerWearIdLink"),
                 })
        {
            if (!links.RootElement.TryGetProperty(linkTable, out var linkMap) ||
                !linkMap.TryGetProperty(costume, out var partIdElement))
            {
                continue;
            }

            var partId = partIdElement.ValueKind == JsonValueKind.String
                ? partIdElement.GetString()!
                : partIdElement.GetRawText();
            if (!adjust.RootElement.TryGetProperty(slot, out var slots) ||
                !slots.TryGetProperty(partId, out var bones))
            {
                continue;
            }

            foreach (var bone in bones.EnumerateObject())
            {
                var multiplier = Vector(bone.Value);
                result[bone.Name] = result.TryGetValue(bone.Name, out var current)
                    ? Multiply(current, multiplier)
                    : multiplier;
            }
        }

        return result;
    }

    private static BoneProportion Slot(
        IDictionary<string, BoneProportion> accum,
        string name,
        int? index)
    {
        if (!accum.TryGetValue(name, out var slot))
        {
            slot = new BoneProportion();
            accum[name] = slot;
        }

        if (index is not null && slot.Index is null)
        {
            slot.Index = index;
        }

        return slot;
    }

    private static (int? Index, string Name) SplitBoneKey(string key)
    {
        var separator = key.IndexOf(':');
        if (separator > 0 && int.TryParse(key.AsSpan(0, separator), out var index))
        {
            return (index, key[(separator + 1)..]);
        }

        return (null, key);
    }

    private static double[] Vector(JsonElement parent, string property) => Vector(parent.GetProperty(property));

    private static double[] Vector(JsonElement element) =>
        element.EnumerateArray().Select(value => value.GetDouble()).ToArray();

    private static double[] Add(IReadOnlyList<double> left, IReadOnlyList<double> right) =>
        left.Zip(right, (a, b) => a + b).ToArray();

    private static double[] Multiply(IReadOnlyList<double> left, IReadOnlyList<double> right) =>
        left.Zip(right, (a, b) => a * b).ToArray();
}
