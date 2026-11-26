using System.Numerics;
using AquaModelLibrary.Data.PSO2.Aqua;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Rigging.Cloth;

/// <summary>
/// The character creator's standing idle (pl_cmakeidle01_fh/mh.aqm, the
/// win32reboot copy the NGS creator actually plays), held at its first
/// frame, so cloth can settle the way it hangs in the creator.
/// </summary>
public sealed class IdlePose
{
    /// <summary>win32reboot\19\91879171c21a8ea5d8363830f00c1c: the creator motions.</summary>
    public const string ArchiveHash = "1991879171c21a8ea5d8363830f00c1c";
    public const string FemaleMotion = "pl_cmakeidle01_fh.aqm";
    public const string MaleMotion = "pl_cmakeidle01_mh.aqm";

    private readonly Dictionary<string, (Vector3? Position, Quaternion? Rotation, Vector3? Scale)> _locals;

    private IdlePose(string name, Dictionary<string, (Vector3?, Quaternion?, Vector3?)> locals)
    {
        Name = name;
        _locals = locals;
    }

    public string Name { get; }

    public static IdlePose Load(ReadOnlyMemory<byte> data, string name)
    {
        var motion = new AquaMotion(data.ToArray());
        var locals = new Dictionary<string, (Vector3?, Quaternion?, Vector3?)>(StringComparer.Ordinal);
        foreach (var node in motion.motionKeys)
        {
            Vector3? position = null, scale = null;
            Quaternion? rotation = null;
            foreach (var key in node.keyData.Where(key => key.vector4Keys.Count > 0))
            {
                var value = key.vector4Keys[0];
                switch (key.keyType)
                {
                    case 1: position = new Vector3(value.X, value.Y, value.Z); break;
                    case 2: rotation = Quaternion.Normalize(new Quaternion(value.X, value.Y, value.Z, value.W)); break;
                    case 3: scale = new Vector3(value.X, value.Y, value.Z); break;
                }
            }

            locals[node.mseg.nodeName.GetString()] = (position, rotation, scale);
        }

        return new IdlePose(name, locals);
    }

    /// <summary>The idle motion of a creator archive, female unless <paramref name="male"/>.</summary>
    public static IdlePose? FromArchive(string archivePath, bool male)
    {
        var name = male ? MaleMotion : FemaleMotion;
        var entry = IceArchive.Load(archivePath).Entries
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : Load(entry.Data, name);
    }

    /// <summary>
    /// Poses <paramref name="pose"/> into the idle: each bone's motion local
    /// transform takes the place of its bind local, with the pose's
    /// proportion and shape channels kept on top. The root keeps its place.
    /// </summary>
    public Matrix4x4[] Apply(AqnSkeleton skeleton, SkeletonPose pose) => pose.WorldWithLocals(Locals(skeleton));

    private Dictionary<int, Matrix4x4> Locals(AqnSkeleton skeleton)
    {
        var locals = new Dictionary<int, Matrix4x4>();
        foreach (var bone in skeleton.Bones)
        {
            if (bone.ParentIndex < 0 || !_locals.TryGetValue(bone.Name, out var key) ||
                !Matrix4x4.Decompose(bone.LocalFromBind, out var bindScale, out var bindRotation, out var bindPosition))
            {
                continue;
            }

            locals[bone.Index] = Matrix4x4.CreateScale(key.Scale ?? bindScale) *
                                 Matrix4x4.CreateFromQuaternion(key.Rotation ?? bindRotation) *
                                 Matrix4x4.CreateTranslation(key.Position ?? bindPosition);
        }

        return locals;
    }
}
