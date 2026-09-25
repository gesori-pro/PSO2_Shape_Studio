using System.Numerics;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.GameData;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.Core.Tests.Rigging;

public sealed class GroundContactTests
{
    /// <summary>
    /// Soffiera's WM Clothes T2-V2 carries the unset legLength of 1.0 while
    /// its heels need about 1.05: posed by the CMX alone it sank 45 mm. The
    /// lift puts its lowest foot vertex back on the floor.
    /// </summary>
    [ExternalDataFact("PSO2_GAME_DIR")]
    public void LiftStandsAnOutfitWithAnUnsetLegLengthOnItsSoles()
    {
        var (model, skeleton) = Load("character/making_reboot/pl_bw_216100.ice");
        var pose = new BodyPoseComposer(skeleton).Build();

        var lift = GroundContact.Lift([model], skeleton, pose.SkinMatrices);

        Assert.NotNull(lift);
        Assert.True(lift > 0.02f, $"expected the heels to need lifting, got {lift * 1000:F1} mm");
        var lowestFoot = model.Meshes
            .SelectMany(mesh => GroundContact.FootVertices(mesh, skeleton)
                .Select(index => Skin(mesh, index, pose.SkinMatrices).Y))
            .Min();
        Assert.Equal(0f, lowestFoot + lift!.Value, 4);
    }

    /// <summary>
    /// Only the soles count: whatever hangs lower than them (a cape, a
    /// train, an ornament) is left under the floor rather than lifting the
    /// character off it.
    /// </summary>
    [ExternalDataFact("PSO2_GAME_DIR")]
    public void FootVerticesAreWeightedToFootOrToeBones()
    {
        var (model, skeleton) = Load("character/making_reboot/pl_bw_201600.ice");

        var feet = model.Meshes.Sum(mesh => GroundContact.FootVertices(mesh, skeleton).Length);

        Assert.InRange(feet, 1, model.VertexCount / 2);
    }

    private static (RenderModel Model, AqnSkeleton Skeleton) Load(string fileName)
    {
        var locator = new Pso2DataLocator(TestPaths.GameDirectory);
        var path = locator.Resolve(fileName)?.Path;
        Assert.NotNull(path);
        var archive = ModelArchiveLoader.Load(path);
        Assert.NotNull(archive.Skeleton);
        return (Assert.Single(archive.Models), archive.Skeleton);
    }

    private static Vector3 Skin(RenderMesh mesh, int index, IReadOnlyList<Matrix4x4> skin) =>
        CpuSkinning.TransformPosition(
            mesh.Positions[index], mesh.Weights[index], mesh.PaletteIndices[index], mesh.Palette, skin);
}
