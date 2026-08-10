using System.Numerics;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.Core.Tests.Rigging;

public sealed class SkeletonPoseTests
{
    private static string AqpPath => TestPaths.ReferenceAqp;
    private static string AqnPath => TestPaths.ReferenceAqn;

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void BindPoseSkinMatricesAndAllVerticesAreIdentity()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var pose = new SkeletonPose(skeleton);
        var model = AqpLoader.Load(AqpPath);

        var matrixMaximum = pose.SkinMatrices.Max(matrix =>
            AqnSkeleton.MatrixMaximumDifference(matrix, Matrix4x4.Identity));
        var vertexMaximum = 0f;
        var vertices = 0;

        foreach (var mesh in model.Meshes)
        {
            for (var index = 0; index < mesh.Positions.Length; index++)
            {
                var transformed = CpuSkinning.TransformPosition(
                    mesh.Positions[index],
                    mesh.Weights[index],
                    mesh.PaletteIndices[index],
                    mesh.Palette,
                    pose.SkinMatrices);
                vertexMaximum = Math.Max(vertexMaximum, Vector3.Distance(mesh.Positions[index], transformed));
                vertices++;
            }
        }

        Assert.Equal(78_973, vertices);
        Assert.True(matrixMaximum < 1e-5f, $"Bind skin matrix error was {matrixMaximum:G9}");
        Assert.True(vertexMaximum < 1e-5f, $"Bind vertex error was {vertexMaximum:G9}");
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void BreastScaleMovesWeightedVertices()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var pose = new SkeletonPose(skeleton);
        pose.SetDelta("l_breast", new BoneDelta(new Vector3(1.3f), Vector3.Zero, Quaternion.Identity));
        pose.Rebuild();
        var model = AqpLoader.Load(AqpPath);

        var moved = 0;
        foreach (var mesh in model.Meshes)
        {
            for (var index = 0; index < mesh.Positions.Length; index++)
            {
                var transformed = CpuSkinning.TransformPosition(
                    mesh.Positions[index], mesh.Weights[index], mesh.PaletteIndices[index], mesh.Palette, pose.SkinMatrices);
                if (Vector3.DistanceSquared(mesh.Positions[index], transformed) > 1e-12f)
                {
                    moved++;
                }
            }
        }

        Assert.True(moved > 0, "l_breast scale did not move any vertices.");
    }
}
