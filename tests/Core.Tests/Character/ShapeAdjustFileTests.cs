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

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void BodyRootYScaleWritesNodeOneAndRoundTrips()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var profile = new ShapeProfile
        {
            ["bodyroot"] = new ShapeValue(new Vector3(1f, 1.06f, 1f), Vector3.Zero, Vector3.Zero),
        };

        var built = ShapeAdjustFile.Build(skeleton, profile);
        var root = Assert.Contains(1, built.Adjustments);
        Assert.Equal("body_root", root.Name);
        Assert.Equal(new Vector3(1f, 1.06f, 1f), root.Scale);

        var reloaded = ShapeAdjustFile.Load(built.Motion.GetBytesNIFL()).ToProfile()["bodyroot"];
        Assert.InRange(reloaded.Scale.Y, 1.05999f, 1.06001f);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void BodyRootIdentityRemovesCarriedHeightAdjustment()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var carried = new Dictionary<int, ShapeAdjustment>
        {
            [1] = new("body_root", new Vector3(1f, 1.08f, 1f), null, null),
        };

        var built = ShapeAdjustFile.Build(skeleton, new ShapeProfile(), carried);

        Assert.DoesNotContain(1, built.Adjustments.Keys);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void CustomPairUsesSkeletonNodeIdsAndRoundTripsAsOneSlider()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var nodeIds = skeleton.Bones
            .Where(bone => bone.Index < ShapeAdjustFile.NodeCount)
            .ToDictionary(bone => bone.Name, bone => bone.Index, StringComparer.OrdinalIgnoreCase);
        var groups = ShapeSliders.ConfigureGroups(
            ShapeSliders.Groups.Select(group => group.Key),
            [new CustomShapeGroupDefinition(
                "custom_clavicle", "Custom Clavicle", "l_clavicle", "r_clavicle")],
            nodeIds);
        var profile = new ShapeProfile
        {
            ["custom_clavicle"] = new ShapeValue(
                new Vector3(1.1f, 1.2f, 1.3f),
                new Vector3(0.01f, 0.02f, 0.03f),
                new Vector3(5f, 10f, 15f)),
        };

        var built = ShapeAdjustFile.Build(skeleton, profile, groups: groups);
        var left = Assert.Contains(nodeIds["l_clavicle"], built.Adjustments);
        var right = Assert.Contains(nodeIds["r_clavicle"], built.Adjustments);
        Assert.Equal(profile["custom_clavicle"].Scale, left.Scale);
        Assert.Equal(ShapeSliders.MirrorPosition(profile["custom_clavicle"].Position), right.Position);

        var reloaded = ShapeAdjustFile.Load(built.Motion.GetBytesNIFL()).ToProfile(groups);
        Assert.InRange(reloaded["custom_clavicle"].Scale.X, 1.09999f, 1.10001f);
        Assert.InRange(reloaded["custom_clavicle"].Position.X, 0.00999f, 0.01001f);
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
