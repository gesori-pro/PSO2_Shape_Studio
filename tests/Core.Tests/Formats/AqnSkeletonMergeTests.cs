using System.Numerics;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.GameData;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.Core.Tests.Formats;

/// <summary>
/// Outerwear over basewear (reported with Lapinchic Hirondel T2 [Ou] 216960
/// over basewear 219550): each model's palette holds node ids of its own AQN,
/// so the outer must be posed on the basewear's skeleton through a name map.
/// </summary>
public sealed class AqnSkeletonMergeTests
{
    private static (GameModelArchive Basewear, GameModelArchive Outerwear) LoadPair()
    {
        var locator = new Pso2DataLocator(TestPaths.GameDirectory);
        GameModelArchive Load(string name)
        {
            var file = locator.Resolve(name) ?? throw new FileNotFoundException(name);
            return ModelArchiveLoader.Load(file.Path);
        }

        return (Load("character/making_reboot/pl_bw_219550.ice"),
                Load("character/making_reboot/pl_ow_216960.ice"));
    }

    private static readonly (string Bone, Vector3 Axis, float Degrees)[] Bend =
    [
        ("l_upperarm", Vector3.UnitZ, 35f), ("r_forearm", Vector3.UnitY, 40f),
        ("l_hand", Vector3.UnitX, 30f), ("r_thigh", Vector3.UnitZ, 25f),
        ("l_calf", Vector3.UnitY, 30f), ("r_foot", Vector3.UnitX, 20f),
        ("spine2", Vector3.UnitX, 15f),
    ];

    private static SkeletonPose Pose(AqnSkeleton skeleton, ISet<string> bent)
    {
        var pose = new SkeletonPose(skeleton);
        foreach (var (bone, axis, degrees) in Bend.Where(b => bent.Contains(b.Bone)))
        {
            pose.SetDelta(bone, new BoneDelta(Vector3.One, Vector3.Zero,
                Quaternion.CreateFromAxisAngle(axis, degrees * MathF.PI / 180f)));
        }

        pose.Rebuild();
        return pose;
    }

    private static Vector3[] Skin(RenderModel model, IReadOnlyList<Matrix4x4> skin) =>
        model.Meshes.SelectMany(mesh => Enumerable.Range(0, mesh.VertexCount).Select(i =>
            CpuSkinning.TransformPosition(mesh.Positions[i], mesh.Weights[i],
                mesh.PaletteIndices[i], mesh.Palette, skin))).ToArray();

    [ExternalDataFact("PSO2_GAME_DIR")]
    public void OuterwearFollowsTheBasewearSkeletonThroughTheMergedMap()
    {
        var (basewear, outerwear) = LoadPair();
        var body = basewear.Skeleton!;
        var outerSkeleton = outerwear.Skeleton!;
        var merged = body.Merge(outerSkeleton, out var map);

        // Existing bones keep their indices, so the basewear needs no remap.
        for (var i = 0; i < body.Bones.Count; i++)
        {
            Assert.Equal(body.Bones[i].Name, merged.Bones[i].Name);
        }

        Assert.All(map, index => Assert.InRange(index, 0, merged.Bones.Count - 1));
        Assert.All(merged.Bones, bone => Assert.True(bone.ParentIndex < bone.Index));

        // An outer AQN carries only the body bones it needs (no r_thigh here),
        // so bend the ones both skeletons have.
        var bent = outerSkeleton.Bones.Select(b => b.Name)
            .Intersect(body.Bones.Select(b => b.Name)).ToHashSet();
        Assert.True(Bend.Count(b => bent.Contains(b.Bone)) >= 3, "too few shared bones to bend");
        Assert.True(outerSkeleton.Bones.Count < body.Bones.Count);

        var outer = outerwear.Models[0];
        var expected = Skin(outer, Pose(outerSkeleton, bent).SkinMatrices);
        var merge = Skin(outer.WithRemappedBones(map), Pose(merged, bent).SkinMatrices);
        var old = Skin(outer, Pose(body, bent).SkinMatrices);   // the reported bug: own palette, other skeleton

        float Worst(Vector3[] a) => a.Zip(expected, Vector3.Distance).Max();
        // Merged: the outer moves with the body exactly where the two AQNs
        // agree; proxies keep its own bind where they do not.
        Assert.True(Worst(merge) < 1e-3f, $"merged worst {Worst(merge):G4} m");
        Assert.True(Worst(old) > 0.05f, $"old path worst {Worst(old):G4} m (expected the bug)");
    }
}
