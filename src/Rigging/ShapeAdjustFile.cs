using System.Numerics;
using AquaModelLibrary.Data.DataTypes.SetLengthStrings;
using AquaModelLibrary.Data.PSO2.Aqua;
using AquaModelLibrary.Data.PSO2.Aqua.AquaMotionData;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Rigging;

public sealed record ShapeAdjustment(
    string Name,
    Vector3? Scale,
    Vector3? Position,
    Quaternion? Rotation);

public sealed class ShapeAdjustFile
{
    public const int NodeCount = 172;

    private ShapeAdjustFile(
        int variant,
        int endFrame,
        IReadOnlyDictionary<int, ShapeAdjustment> adjustments,
        AquaMotion motion)
    {
        Variant = variant;
        EndFrame = endFrame;
        Adjustments = adjustments;
        Motion = motion;
    }

    public int Variant { get; }

    public int EndFrame { get; }

    public IReadOnlyDictionary<int, ShapeAdjustment> Adjustments { get; }

    public AquaMotion Motion { get; }

    public static ShapeAdjustFile Load(string path)
    {
        return Load(File.ReadAllBytes(path));
    }

    public static ShapeAdjustFile Load(ReadOnlyMemory<byte> data)
    {
        var motion = new AquaMotion(data.ToArray());
        var adjustments = ExtractAdjustments(motion);
        return new ShapeAdjustFile(motion.moHeader.variant, motion.moHeader.endFrame, adjustments, motion);
    }

    public ShapeProfile ToProfile()
    {
        var byName = Adjustments.Values.ToDictionary(
            entry => entry.Name,
            StringComparer.OrdinalIgnoreCase);
        var profile = new ShapeProfile();
        foreach (var group in ShapeSliders.Groups)
        {
            if (!byName.TryGetValue(group.LeftBone, out var entry))
            {
                continue;
            }

            profile[group.Key] = new ShapeValue(
                entry.Scale ?? Vector3.One,
                entry.Position ?? Vector3.Zero,
                entry.Rotation is { } rotation ? QuaternionToEulerDegrees(rotation) : Vector3.Zero);
        }

        return profile;
    }

    public static ShapeAdjustFile Build(
        AqnSkeleton skeleton,
        ShapeProfile profile,
        IReadOnlyDictionary<int, ShapeAdjustment>? carried = null)
    {
        var adjusted = carried?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        foreach (var group in ShapeSliders.Groups)
        {
            foreach (var nodeId in group.NodeIds.Values)
            {
                adjusted.Remove(nodeId);
            }

            var value = profile[group.Key];
            if (value.IsIdentity)
            {
                continue;
            }

            var rotation = ShapeSliders.EulerDegreesToQuaternion(value.EulerDegrees);
            Put(group.LeftBone, value.Position, rotation);
            if (group.RightBone is not null)
            {
                Put(
                    group.RightBone,
                    ShapeSliders.MirrorPosition(value.Position),
                    ShapeSliders.MirrorQuaternion(rotation));
            }

            void Put(string name, Vector3 position, Quaternion quaternion)
            {
                var index = group.NodeIds.GetValueOrDefault(name, -1);
                if (index >= 0)
                {
                    adjusted[index] = new ShapeAdjustment(name, value.Scale, position, quaternion);
                }
            }
        }

        var motion = new AquaMotion();
        motion.moHeader = new MOHeader
        {
            variant = 0x10002,
            loopPoint = 0,
            endFrame = 1,
            frameSpeed = 30f,
            unkInt0 = 2,
            nodeCount = NodeCount,
            boneTableOffset = 0x50,
            testString = new PSO2String("test"),
        };

        for (var index = 0; index < NodeCount; index++)
        {
            var name = index < skeleton.Bones.Count ? skeleton.Bones[index].Name : $"node{index}";
            var node = new KeyData
            {
                mseg = new MSEG
                {
                    nodeType = 0x2,
                    nodeDataCount = 3,
                    nodeName = new PSO2String(name),
                    nodeId = index,
                },
            };

            if (!adjusted.TryGetValue(index, out var entry))
            {
                node.keyData.Add(Key(1, 1, [Vector4.Zero]));
                node.keyData.Add(Key(2, 3, [new Vector4(0, 0, 0, 1)]));
                node.keyData.Add(Key(3, 1, [new Vector4(1, 1, 1, 0)]));
            }
            else
            {
                var position = entry.Position ?? Vector3.Zero;
                var rotation = Quaternion.Normalize(entry.Rotation ?? Quaternion.Identity);
                var scale = entry.Scale ?? Vector3.One;
                node.keyData.Add(Key(1, 1, [Vector4.Zero, new Vector4(position, 0)]));
                node.keyData.Add(Key(2, 3, [new Vector4(0, 0, 0, 1), new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W)]));
                node.keyData.Add(Key(3, 1, [new Vector4(1, 1, 1, 0), new Vector4(scale, 0)]));
            }

            motion.motionKeys.Add(node);
        }

        return new ShapeAdjustFile(0x10002, 1, adjusted, motion);
    }

    public void Save(string path) => File.WriteAllBytes(path, Motion.GetBytesNIFL());

    public static IReadOnlyDictionary<int, ShapeAdjustment> ExtractAdjustments(AquaMotion motion)
    {
        var result = new Dictionary<int, ShapeAdjustment>();
        for (var fallbackIndex = 0; fallbackIndex < motion.motionKeys.Count; fallbackIndex++)
        {
            var node = motion.motionKeys[fallbackIndex];
            var index = node.mseg.nodeId >= 0 ? node.mseg.nodeId : fallbackIndex;
            Vector3? scale = null;
            Vector3? position = null;
            Quaternion? rotation = null;

            foreach (var key in node.keyData.Where(key => key.vector4Keys.Count > 0))
            {
                var frames = DecodeFrames(key);
                var byFrame = frames.Zip(key.vector4Keys).ToDictionary(pair => pair.First, pair => pair.Second);
                if (key.keyType == 3)
                {
                    Vector3 multiplier;
                    if (byFrame.TryGetValue(0, out var f0) && byFrame.TryGetValue(1, out var f1))
                    {
                        multiplier = new Vector3(
                            SafeDivide(f1.X, f0.X), SafeDivide(f1.Y, f0.Y), SafeDivide(f1.Z, f0.Z));
                    }
                    else if (byFrame.Count == 1)
                    {
                        var value = byFrame.Values.Single();
                        multiplier = new Vector3(value.X, value.Y, value.Z);
                        if (multiplier.LengthSquared() < 1e-18f)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    if (MathF.Abs(multiplier.X - 1f) > 1e-6f ||
                        MathF.Abs(multiplier.Y - 1f) > 1e-6f ||
                        MathF.Abs(multiplier.Z - 1f) > 1e-6f)
                    {
                        scale = multiplier;
                    }

                    continue;
                }

                if (!byFrame.TryGetValue(0, out var frame0) || !byFrame.TryGetValue(1, out var frame1))
                {
                    continue;
                }

                if (key.keyType == 1)
                {
                    var difference = new Vector3(frame1.X - frame0.X, frame1.Y - frame0.Y, frame1.Z - frame0.Z);
                    if (difference.LengthSquared() > 1e-18f)
                    {
                        position = difference;
                    }
                }
                else if (key.keyType == 2)
                {
                    var q0 = Quaternion.Normalize(new Quaternion(frame0.X, frame0.Y, frame0.Z, frame0.W));
                    var q1 = Quaternion.Normalize(new Quaternion(frame1.X, frame1.Y, frame1.Z, frame1.W));
                    var delta = Quaternion.Normalize(Quaternion.Inverse(q0) * q1);
                    if (2f * MathF.Acos(Math.Clamp(MathF.Abs(delta.W), 0f, 1f)) > 1e-6f)
                    {
                        rotation = delta;
                    }
                }
            }

            if (scale is not null || position is not null || rotation is not null)
            {
                result[index] = new ShapeAdjustment(node.mseg.nodeName.GetString(), scale, position, rotation);
            }
        }

        return result;
    }

    private static MKEY Key(int keyType, int dataType, IReadOnlyList<Vector4> values)
    {
        var key = new MKEY
        {
            keyType = keyType,
            dataType = dataType,
            unkInt0 = 0,
            keyCount = values.Count,
            vector4Keys = values.ToList(),
        };
        if (values.Count > 1)
        {
            key.frameTimings.Add(0);
            key.frameTimings.Add(0x10);
        }

        return key;
    }

    private static int[] DecodeFrames(MKEY key)
    {
        if (key.frameTimings.Count == 0)
        {
            return Enumerable.Repeat(0, key.vector4Keys.Count).ToArray();
        }

        var multiplier = (key.dataType & 0x80) != 0 ? 0x100 : 0x10;
        return key.frameTimings.Select(value => (int)value / multiplier).ToArray();
    }

    private static float SafeDivide(float numerator, float denominator) =>
        MathF.Abs(denominator) > 1e-9f ? numerator / denominator : 1f;

    private static Vector3 QuaternionToEulerDegrees(Quaternion quaternion)
    {
        var q = Quaternion.Normalize(quaternion);
        var x = MathF.Atan2(2f * (q.W * q.X + q.Y * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        var sineY = Math.Clamp(2f * (q.W * q.Y - q.Z * q.X), -1f, 1f);
        var y = MathF.Asin(sineY);
        var z = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
        return new Vector3(x, y, z) * (180f / MathF.PI);
    }
}
