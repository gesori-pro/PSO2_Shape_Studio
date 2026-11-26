using System.Numerics;
using System.Text.Json;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.Rigging;
using Pso2ShapeStudio.Rigging.Cloth;
using Xunit.Abstractions;

namespace Pso2ShapeStudio.Core.Tests.Rigging;

public sealed class ClothSimulationTests(ITestOutputHelper output)
{
    /// <summary>
    /// A folder with a model's .aqn and .fltd and blender_settle30.json: the
    /// blender_pso2_tools FLTD preview (bit-exact to the game's solver) after
    /// 30 settling steps on the bind pose, as native world rows per bone.
    /// </summary>
    private const string ReferenceVariable = "PSO2_CLOTH_REFERENCE_DIR";

    [ExternalDataFact(ReferenceVariable)]
    public void SettledBonesMatchTheBlenderPreview()
    {
        var root = Environment.GetEnvironmentVariable(ReferenceVariable)!;
        var skeleton = AqnSkeleton.Load(Directory.GetFiles(root, "*.aqn").Single());
        var fltd = File.ReadAllBytes(Directory.GetFiles(root, "*.fltd").Single());
        var cloth = ClothSimulation.TryCreate(fltd, skeleton, out var problem);
        Assert.Null(problem);
        Assert.NotNull(cloth);

        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "blender_settle30.json")));
        var expected = reference.RootElement.GetProperty("output");
        var mapped = reference.RootElement.GetProperty("mapped").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var pose = skeleton.Bones.Select(bone => bone.WorldBind).ToArray();
        var settled = cloth!.Settle(pose);

        var worstPosition = 0f;
        var worstAxis = 0f;
        var worstMoved = 0f;
        foreach (var index in mapped)
        {
            var rows = expected[index].EnumerateArray().Select(row => row.EnumerateArray().Select(v => v.GetSingle()).ToArray()).ToArray();
            var position = new Vector3(rows[3][0], rows[3][1], rows[3][2]);
            var actual = settled[index];
            worstPosition = MathF.Max(worstPosition, Vector3.Distance(position, actual.Translation));
            worstMoved = MathF.Max(worstMoved, Vector3.Distance(position, pose[index].Translation));
            for (var row = 0; row < 3; row++)
            {
                var want = new Vector3(rows[row][0], rows[row][1], rows[row][2]);
                var got = row switch
                {
                    0 => new Vector3(actual.M11, actual.M12, actual.M13),
                    1 => new Vector3(actual.M21, actual.M22, actual.M23),
                    _ => new Vector3(actual.M31, actual.M32, actual.M33),
                };
                worstAxis = MathF.Max(worstAxis, Vector3.Distance(want, got));
            }
        }

        output.WriteLine($"parts {cloth.PartCount}, particles {cloth.ParticleCount}, colliders off {cloth.DisabledColliders}");
        output.WriteLine($"cloth moved up to {worstMoved:F4}; worst position error {worstPosition:F5}, axis error {worstAxis:F5}");
        // The add-on reproduces the game's SSE reciprocal estimates; the
        // preview divides exactly, so allow a millimetre over 30 steps.
        Assert.True(worstPosition < 1e-3f, $"worst position error {worstPosition}");
        Assert.True(worstAxis < 1e-2f, $"worst axis error {worstAxis}");
    }
}

public sealed class IdlePoseTests(ITestOutputHelper output)
{
    /// <summary>
    /// A folder with pl_cmakeidle01_fh.aqm and blender_idle_frame0.json: bone
    /// heads (game x, y, z) after blender_pso2_tools imports that motion onto
    /// the model given by PSO2_CLOTH_REFERENCE_DIR, at frame 0.
    /// </summary>
    private const string IdleVariable = "PSO2_CLOTH_IDLE_DIR";

    [ExternalDataFact(IdleVariable, "PSO2_CLOTH_REFERENCE_DIR")]
    public void IdlePoseMatchesTheBlenderMotionImport()
    {
        var root = Environment.GetEnvironmentVariable(IdleVariable)!;
        var model = Environment.GetEnvironmentVariable("PSO2_CLOTH_REFERENCE_DIR")!;
        var skeleton = AqnSkeleton.Load(Directory.GetFiles(model, "*.aqn").Single());
        var idle = IdlePose.Load(File.ReadAllBytes(Path.Combine(root, IdlePose.FemaleMotion)), IdlePose.FemaleMotion);
        // The reference was imported without the outfit's _sa.aqm: the add-on's
        // default importer inherits a pelvis scale down the legs, where the
        // game (and SkeletonPose) keeps 0x1C0 bones at their own scale.
        var posed = idle.Apply(skeleton, new SkeletonPose(skeleton));

        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "blender_idle_frame0.json")));
        var worst = (Error: 0f, Name: "");
        foreach (var bone in skeleton.Bones)
        {
            if (!reference.RootElement.TryGetProperty(bone.Name, out var head)) continue;
            var want = new Vector3(head[0].GetSingle(), head[1].GetSingle(), head[2].GetSingle());
            var error = Vector3.Distance(want, posed[bone.Index].Translation);
            if (error > worst.Error) worst = (error, bone.Name);
        }

        output.WriteLine($"worst bone {worst.Name}: {worst.Error:F5}");
        Assert.True(worst.Error < 1e-3f, $"{worst.Name} is off by {worst.Error}");
    }
}
