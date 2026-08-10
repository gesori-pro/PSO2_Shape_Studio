using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Core.Tests.Formats;

public sealed class AqnSkeletonTests
{
    private static string AqnPath => TestPaths.ReferenceAqn;

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void LoadReferenceSkeletonMatchesIndependentCountsAndLandmarks()
    {
        Assert.True(File.Exists(AqnPath), $"Reference skeleton is missing: {AqnPath}");

        var skeleton = AqnSkeleton.Load(AqnPath);

        Assert.Equal(223, skeleton.Bones.Count);
        Assert.Equal(10, skeleton.AuxiliaryNodeCount);
        Assert.Equal("hip", skeleton.Bones[2].Name);
        Assert.Equal(1, skeleton.Bones[2].ParentIndex);
        // HANDOFF reports three decimals (0.898); retain enough room for the
        // unrounded bind-matrix value read by AML.
        Assert.InRange(skeleton.Bones[2].WorldBind.M42, 0.8975f, 0.8985f);
        Assert.Equal("l_breast", skeleton.Bones[41].Name);
        Assert.InRange(skeleton.Bones[41].WorldBind.M41, 0.0699f, 0.0701f);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void SourceTrsUsesXzyWithinObservedStoredEulerQuantization()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var documentedGate = skeleton.ValidateSourceTrs(1e-4f);
        var observedEnvelope = skeleton.ValidateSourceTrs(2e-4f);

        // The supplied reference AQN itself has seven source-Euler vs inverse-bind
        // residuals just over the documented 1e-4 gate. Keep that discrepancy as
        // a characterization instead of weakening production validation silently.
        Assert.Equal(7, documentedGate.Failures.Count);
        Assert.InRange(documentedGate.MaximumError, 1.55e-4f, 1.56e-4f);
        Assert.Empty(observedEnvelope.Failures);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void AuthoritativeBindLocalsDecomposeAndRecomposeWithinGate()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var maximum = 0f;
        foreach (var bone in skeleton.Bones)
        {
            Assert.True(System.Numerics.Matrix4x4.Decompose(
                bone.LocalFromBind, out var scale, out var rotation, out var translation));
            var rebuilt = System.Numerics.Matrix4x4.CreateScale(scale) *
                          System.Numerics.Matrix4x4.CreateFromQuaternion(rotation) *
                          System.Numerics.Matrix4x4.CreateTranslation(translation);
            maximum = Math.Max(maximum, AqnSkeleton.MatrixMaximumDifference(bone.LocalFromBind, rebuilt));
        }

        Assert.True(maximum < 1e-4f, $"Authoritative local matrix error was {maximum:G9}");
    }
}
