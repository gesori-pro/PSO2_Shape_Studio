using System.Numerics;
using AquaModelLibrary.Data.PSO2.Aqua;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.Core.Tests.Character;

public sealed class ShapeAdjustFileTests
{
    private static string AqnPath => TestPaths.ReferenceAqn;
    private static string OriginalPath => TestPaths.ReferenceShape;
    private static string GoldenPath => TestPaths.GoldenShape;

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void OriginalAqmRoundTripPreservesEveryKeyValue()
    {
        var original = new AquaMotion(File.ReadAllBytes(OriginalPath));
        var serialized = original.GetBytesNIFL();
        var reloaded = new AquaMotion(serialized);
        AssertMotionsEqual(original, reloaded, 0f);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void WaistProfileWithCarriedOutfitMatchesGoldenKeys()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var carried = ShapeAdjustFile.Load(OriginalPath);
        var profile = carried.ToProfile();
        profile["waist"] = new ShapeValue(new Vector3(1.35f), Vector3.Zero, Vector3.Zero);
        var built = ShapeAdjustFile.Build(skeleton, profile, carried.Adjustments);
        var bytes = built.Motion.GetBytesNIFL();
        var reloaded = new AquaMotion(bytes);
        var golden = new AquaMotion(File.ReadAllBytes(GoldenPath));

        Assert.Equal(172, reloaded.motionKeys.Count);
        AssertMotionsEqual(golden, reloaded, 1e-5f);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void LoadMapsExistingAqmBackToSliderValues()
    {
        var profile = ShapeAdjustFile.Load(GoldenPath).ToProfile();
        var waist = profile["waist"];
        Assert.InRange(waist.Scale.X, 1.34999f, 1.35001f);
        Assert.InRange(waist.Scale.Y, 1.34999f, 1.35001f);
        Assert.InRange(waist.Scale.Z, 1.34999f, 1.35001f);
    }

    private static void AssertMotionsEqual(AquaMotion expected, AquaMotion actual, float tolerance)
    {
        Assert.Equal(expected.moHeader.variant, actual.moHeader.variant);
        Assert.Equal(expected.moHeader.endFrame, actual.moHeader.endFrame);
        Assert.Equal(expected.motionKeys.Count, actual.motionKeys.Count);
        for (var nodeIndex = 0; nodeIndex < expected.motionKeys.Count; nodeIndex++)
        {
            var left = expected.motionKeys[nodeIndex];
            var right = actual.motionKeys[nodeIndex];
            Assert.Equal(left.mseg.nodeId, right.mseg.nodeId);
            Assert.Equal(left.keyData.Count, right.keyData.Count);
            for (var keyIndex = 0; keyIndex < left.keyData.Count; keyIndex++)
            {
                var leftKey = left.keyData[keyIndex];
                var rightKey = right.keyData[keyIndex];
                Assert.Equal(leftKey.keyType, rightKey.keyType);
                Assert.Equal(leftKey.dataType, rightKey.dataType);
                Assert.True(leftKey.vector4Keys.Count == rightKey.vector4Keys.Count,
                    $"node={nodeIndex}, key={leftKey.keyType}, expectedCount={leftKey.vector4Keys.Count}, actualCount={rightKey.vector4Keys.Count}");
                for (var valueIndex = 0; valueIndex < leftKey.vector4Keys.Count; valueIndex++)
                {
                    var a = leftKey.vector4Keys[valueIndex];
                    var b = rightKey.vector4Keys[valueIndex];
                    var error = Math.Max(
                        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)),
                        Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W)));
                    Assert.True(error <= tolerance,
                        $"node={nodeIndex}, key={leftKey.keyType}, value={valueIndex}, error={error:G9}");
                }
            }
        }
    }
}
