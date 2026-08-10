using System.Numerics;
using System.Text.Json;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.Core.Tests.Rigging;

public sealed class ShapeSlidersTests
{
    private static string AqpPath => TestPaths.ReferenceAqp;
    private static string AqnPath => TestPaths.ReferenceAqn;
    private static string ShapePath => TestPaths.ReferenceShape;
    private static string FnpPath => TestPaths.FocusliteFnp;
    private static string PoseGoldenPath => TestPaths.GoldenFocuslitePose;

    [Fact]
    public void DefinitionsMatchNormativeFourteenGroupOrder()
    {
        Assert.Equal(14, ShapeSliders.Groups.Count);
        Assert.Equal(
            ["breast", "breast2", "cbreast2", "clav", "waist", "hip", "pelvis", "hiptw", "thigh", "thightw", "thightw2", "calf0", "calf", "foot"],
            ShapeSliders.Groups.Select(group => group.Key));
        Assert.False(ShapeSliders.Groups.Single(group => group.Key == "clav").SupportsRotation);
    }

    [Fact]
    public void ShapeProfileCloneIsIndependentAndValueComparable()
    {
        var source = new ShapeProfile
        {
            ["waist"] = new ShapeValue(new Vector3(1.25f), Vector3.Zero, Vector3.Zero),
        };
        var clone = source.Clone();

        Assert.True(source.ValueEquals(clone));
        clone["waist"] = ShapeValue.Identity;

        Assert.False(source.ValueEquals(clone));
        Assert.Equal(1.25f, source["waist"].Scale.X);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void WaistScaleReferenceHasExpectedVertexEffect()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var model = AqpLoader.Load(AqpPath);
        var profile = new ShapeProfile
        {
            ["waist"] = new ShapeValue(new Vector3(1.35f), Vector3.Zero, Vector3.Zero),
        };
        var pose = ShapeSliders.Apply(skeleton, profile);

        var moved = 0;
        var movedAbove = 0;
        var maximum = 0f;
        foreach (var mesh in model.Meshes)
        {
            for (var index = 0; index < mesh.Positions.Length; index++)
            {
                var original = mesh.Positions[index];
                var transformed = CpuSkinning.TransformPosition(
                    original, mesh.Weights[index], mesh.PaletteIndices[index], mesh.Palette, pose.SkinMatrices);
                var distance = Vector3.Distance(original, transformed);
                if (distance > 1e-5f)
                {
                    moved++;
                }
                if (distance > 1e-4f && original.Y > 1.30f)
                {
                    movedAbove++;
                }

                maximum = Math.Max(maximum, distance);
            }
        }

        Assert.Equal(4_408, moved);
        Assert.InRange(maximum * 1000f, 32f, 35f);
        Assert.Equal(20, movedAbove);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void ShapeAdjustBreastRootsMatchBlenderGolden()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var composer = new BodyPoseComposer(skeleton);
        SetAppShape(composer, ShapeAdjustFile.Load(ShapePath));

        AssertMatchesBlenderSkinMatrices(
            skeleton,
            composer.Build(),
            "shape",
            ["l_breast", "r_breast"]);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA", "PSO2_SHAPE_FOCUSLITE_FNP")]
    public void FnpAndShapeAdjustPoseMatchesBlenderGolden()
    {
        var skeleton = AqnSkeleton.Load(AqnPath);
        var composer = new BodyPoseComposer(skeleton);
        var proportions = Proportions.Compute(CharacterFile.Load(FnpPath), applyOutfitAdjust: false);
        foreach (var (name, value) in proportions.Bones)
        {
            composer.SetProportion(
                name,
                new BoneDelta(
                    ToVector3(value.Scale),
                    ToVector3(value.Pos),
                    Quaternion.Normalize(new Quaternion(
                        (float)value.RotQuat[0], (float)value.RotQuat[1],
                        (float)value.RotQuat[2], (float)value.RotQuat[3]))));
        }

        SetAppShape(composer, ShapeAdjustFile.Load(ShapePath));

        var comparedBones = proportions.Bones.Keys
            .Where(name => !name.StartsWith("drs", StringComparison.OrdinalIgnoreCase))
            .Concat(ShapeSliders.Groups.SelectMany(group => group.RightBone is null
                ? new[] { group.LeftBone }
                : new[] { group.LeftBone, group.RightBone }))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertMatchesBlenderSkinMatrices(
            skeleton,
            composer.Build(),
            "combined",
            comparedBones);
    }

    private static void AssertMatchesBlenderSkinMatrices(
        AqnSkeleton skeleton,
        SkeletonPose pose,
        string layer,
        IReadOnlyCollection<string> boneNames)
    {
        using var golden = JsonDocument.Parse(File.ReadAllBytes(PoseGoldenPath));
        var expectedBase = golden.RootElement.GetProperty("base");
        var expectedPose = golden.RootElement.GetProperty(layer);
        var selected = boneNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blenderToPso2 = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, -1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);
        Assert.True(Matrix4x4.Invert(blenderToPso2, out var pso2ToBlender));

        var maximum = 0f;
        var maximumBone = "";
        var compared = 0;
        foreach (var bone in skeleton.Bones)
        {
            if (!selected.Contains(bone.Name) ||
                !expectedBase.TryGetProperty(bone.Name, out var baseValues) ||
                !expectedPose.TryGetProperty(bone.Name, out var poseValues))
            {
                continue;
            }

            var baseWorld = BlenderMatrixToSystem(baseValues.GetProperty("world"));
            var posedWorld = BlenderMatrixToSystem(poseValues.GetProperty("world"));
            Assert.True(Matrix4x4.Invert(baseWorld, out var inverseBase));
            var blenderSkin = inverseBase * posedWorld;
            var expected = pso2ToBlender * blenderSkin * blenderToPso2;
            var actual = pose.SkinMatrices[bone.Index];
            var difference = AqnSkeleton.MatrixMaximumDifference(expected, actual);
            if (difference > maximum)
            {
                maximum = difference;
                maximumBone = bone.Name;
            }
            compared++;
        }

        Assert.True(
            compared >= selected.Count - 1,
            $"Only compared {compared} of {selected.Count} Blender golden bones.");
        Assert.True(
            maximum < 1e-4f,
            $"Blender golden skin-matrix error was {maximum:G9} at {maximumBone}.");
    }

    private static Matrix4x4 BlenderMatrixToSystem(JsonElement values)
    {
        var source = values.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        Assert.Equal(16, source.Length);
        return new Matrix4x4(
            source[0], source[4], source[8], source[12],
            source[1], source[5], source[9], source[13],
            source[2], source[6], source[10], source[14],
            source[3], source[7], source[11], source[15]);
    }

    private static void SetAppShape(BodyPoseComposer composer, ShapeAdjustFile shape)
    {
        var sliderBones = ShapeSliders.Groups
            .SelectMany(group => group.RightBone is null
                ? new[] { group.LeftBone }
                : new[] { group.LeftBone, group.RightBone })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var adjustment in shape.Adjustments.Values.Where(value => !sliderBones.Contains(value.Name)))
        {
            composer.SetShape(
                adjustment.Name,
                new BoneDelta(
                    adjustment.Scale ?? Vector3.One,
                    adjustment.Position ?? Vector3.Zero,
                    adjustment.Rotation ?? Quaternion.Identity));
        }

        var profile = shape.ToProfile();
        foreach (var group in ShapeSliders.Groups)
        {
            var value = profile[group.Key];
            if (value.IsIdentity)
            {
                continue;
            }

            var rotation = ShapeSliders.EulerDegreesToQuaternion(value.EulerDegrees);
            composer.SetShape(group.LeftBone, new BoneDelta(value.Scale, value.Position, rotation));
            if (group.RightBone is not null)
            {
                composer.SetShape(
                    group.RightBone,
                    new BoneDelta(
                        value.Scale,
                        ShapeSliders.MirrorPosition(value.Position),
                        ShapeSliders.MirrorQuaternion(rotation)));
            }
        }
    }

    private static Vector3 ToVector3(IReadOnlyList<double> value) =>
        new((float)value[0], (float)value[1], (float)value[2]);

    [Fact]
    public void EulerAndMirroringMatchShapeAdjustConvention()
    {
        var q = ShapeSliders.EulerDegreesToQuaternion(new Vector3(10, 20, 30));
        Assert.InRange(q.X, 0.0380f, 0.0382f);
        Assert.InRange(q.Y, 0.1892f, 0.1894f);
        Assert.InRange(q.Z, 0.2392f, 0.2394f);
        Assert.InRange(q.W, 0.9514f, 0.9516f);

        Assert.Equal(new Vector3(1, -2, 3), ShapeSliders.MirrorPosition(new Vector3(1, 2, 3)));
        Assert.Equal(new Quaternion(-q.X, q.Y, -q.Z, q.W), ShapeSliders.MirrorQuaternion(q));
    }
}
