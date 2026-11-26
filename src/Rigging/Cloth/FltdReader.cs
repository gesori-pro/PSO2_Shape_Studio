using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Pso2ShapeStudio.Rigging.Cloth;

/// <summary>
/// Reads the version-10 FLTD cloth file: per-part solver settings
/// (28F4440/29090D0) and the chains and colliders bound to a skeleton
/// (28BF0A0/28C3490/28BBE60). Ported from blender_pso2_tools'
/// native_cloth_initialization and native_cloth_file.
/// </summary>
internal static class FltdReader
{
    private static readonly int[] RuntimeTag = [1, 0, 4, 2, 3, 5, 6, 7, 8];
    private static readonly int[] PrefixTags = [0, 2, 7, 8, 9];

    /// <summary>False for the empty FLTD that models without cloth ship with.</summary>
    public static bool DeclaresCloth(byte[] data)
    {
        var file = new FltdFile(data);
        return file.Byte(file.Header + 1) != 0 || file.Byte(file.Header + 2) != 0;
    }

    public static IReadOnlyList<ClothPart> ReadParts(byte[] data, IReadOnlyList<string> names, IReadOnlyList<int> parents)
    {
        var file = new FltdFile(data);
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
        {
            byName.TryAdd(names[i], i);
        }

        var children = Enumerable.Range(0, names.Count).Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < parents.Count; i++)
        {
            if (parents[i] >= 0)
            {
                children[parents[i]].Add(i);
            }
        }

        var definitions = ReadDefinitions(file).ToDictionary(d => (d.PartIndex, d.MirrorSign));
        var mainCount = file.Byte(file.Header + 1);
        var main = file.Pointer(file.Header + 4, mainCount * 20);
        var parts = new List<ClothPart>();
        var used = new HashSet<int>();
        for (var group = 0; group < mainCount; group++)
        {
            var offset = main + group * 20;
            int index = file.Byte(offset), rootCount = file.Byte(offset + 1);
            int shapeCount = file.Byte(offset + 2), groupBits = file.Byte(offset + 3);
            if (!definitions.TryGetValue((index, groupBits & 3), out var definition) || rootCount == 0 || (groupBits & ~7) != 0)
            {
                throw new InvalidDataException("Unsupported FLTD owner/mirror configuration.");
            }

            var rootVector = file.Pointer(offset + 4, rootCount * 4);
            var mapping = new List<int>();
            var records = new List<int[]>();
            int? stride = null;

            void Visit(int node, int previous)
            {
                // 28C3490: depth first, a node then its first child and that
                // child's later siblings. A first child is kept only with a
                // drs-/drs_/drs2-/drs2_ name, a later sibling only with drs-.
                if (mapping.Contains(node) || mapping.Count >= 64)
                {
                    throw new InvalidDataException("Cyclic or oversized FLTD chain.");
                }

                var me = mapping.Count;
                mapping.Add(node);
                for (var k = 0; k < children[node].Count; k++)
                {
                    var child = children[node][k];
                    var name = names[child];
                    var keep = k == 0
                        ? name.StartsWith("drs-", StringComparison.Ordinal) || name.StartsWith("drs_", StringComparison.Ordinal) ||
                          name.StartsWith("drs2-", StringComparison.Ordinal) || name.StartsWith("drs2_", StringComparison.Ordinal)
                        : name.StartsWith("drs-", StringComparison.Ordinal);
                    if (!keep)
                    {
                        continue;
                    }

                    records.Add([me, mapping.Count, previous, -1, -1]);
                    Visit(child, records.Count - 1);
                }
            }

            for (var i = 0; i < rootCount; i++)
            {
                var root = file.String(file.U32(rootVector + i * 4));
                if (!byName.TryGetValue(root, out var rootNode))
                {
                    throw new InvalidDataException($"FLTD chain root is not in this model: {root}");
                }

                Visit(rootNode, -1);
                stride ??= mapping.Count;
            }

            if (mapping.Count != definition.ParticleCount)
            {
                throw new InvalidDataException(
                    $"FLTD part {index} declares {definition.ParticleCount} particles but the skeleton walks to {mapping.Count}.");
            }

            if (mapping.Any(used.Contains) || records.Count == 0)
            {
                throw new InvalidDataException("FLTD groups overlap or a chain has no edge.");
            }

            used.UnionWith(mapping);
            foreach (var record in records)
            {
                record[3] = Math.Max(-1, record[0] - stride!.Value);
                record[4] = record[0] + stride.Value;
            }

            var shapes = new List<FileCollider>();
            var shapesStart = file.Pointer(offset + 8, shapeCount * 92);
            for (var i = 0; i < shapeCount; i++)
            {
                shapes.Add(ReadShape(file, shapesStart + i * 92, byName, mapping.Count));
            }

            // 28F2920 inserts tags 0/2/7/8/9 before the suffix.
            var ordered = shapes.Where(s => PrefixTags.Contains(s.Kind))
                .Concat(shapes.Where(s => !PrefixTags.Contains(s.Kind)))
                .ToArray();
            parts.Add(new ClothPart(
                definition,
                mapping.ToArray(),
                records.ToArray(),
                ordered,
                ordered.Count(s => PrefixTags.Contains(s.Kind)),
                (groupBits & 4) != 0));
        }

        return parts;
    }

    private static FileCollider ReadShape(FltdFile file, int entry, Dictionary<string, int> byName, int particleCount)
    {
        int kind = file.Byte(entry), filters = file.Byte(entry + 1), options = file.Byte(entry + 3);
        int extraCount = file.Byte(entry + 84), blend = file.Byte(entry + 85);
        int scaleFlags = file.Byte(entry + 86), globalFlags = file.Byte(entry + 87);
        if (kind is < 0 or > 8 || kind == 2 || globalFlags != 0 || (scaleFlags & ~0xF) != 0 ||
            ((options & 0x10) != 0 && kind == 5))
        {
            throw new InvalidDataException("Unsupported FLTD collision callback/filter/growth variant.");
        }

        (float, float)? growth = null;
        if ((scaleFlags & 8) != 0 && file.U32(entry + 88) is not (0 or 16))
        {
            if (kind != 7 || (options & 8) == 0)
            {
                throw new InvalidDataException("Unsupported FLTD capsule growth variant.");
            }

            var pair = file.Floats(file.Pointer(entry + 88, 8), 2);
            growth = (pair[0], pair[1]);
        }

        if (extraCount != (kind == 7 ? 1 : 0))
        {
            throw new InvalidDataException("FLTD collider transform slot count differs.");
        }

        var bindings = new List<int?> { Lookup(file.String(file.U32(entry + 68))) };
        if (extraCount != 0)
        {
            var vector = file.Pointer(entry + 80, 4);
            bindings.Add(Lookup(file.String(file.U32(vector))));
        }

        var flags = 1 | ((options & 7) != 0 ? 2 : 0) | ((options & 8) != 0 ? 4 : 0) | ((options & 0x10) != 0 ? 8 : 0);
        flags |= ((scaleFlags & 7) << 7) | (kind == 5 ? 0x2000 : 0) | (growth is not null ? 0x800 : 0);
        flags |= bindings[^1] is int last && last is >= 0 and < 2 ? 0x4000 : 0;

        var filter = Array.Empty<byte>();
        if (filters != 0)
        {
            var pointer = file.Pointer(entry + 72, filters);
            filter = file.Bytes(pointer, filters);
            if (filter.Any(i => i >= particleCount))
            {
                throw new InvalidDataException("FLTD particle filter exceeds the chain.");
            }
        }

        var values = file.Floats(entry + 4, 16);
        var mirror = new[] { (options & 0x20) != 0, (options & 0x40) != 0, (options & 0x80) != 0 };
        if (kind is not (6 or 8))
        {
            for (var axis = 0; axis < 3; axis++)
            {
                if (!mirror[axis])
                {
                    continue;
                }

                values[kind != 7 ? axis : 5 + axis] *= -1f;
                if (kind == 7)
                {
                    values[8 + axis] *= -1f;
                }

                if (kind is 3 or 4)
                {
                    values[3 + axis] *= -1f;
                }
            }
        }

        return new FileCollider(RuntimeTag[kind], bindings.ToArray(), flags, blend * 0.01f, values, filter, mirror, growth);

        // The character's top node lives in the parent resource (28BFE30
        // bank 1); in a model's own space it is the origin.
        int? Lookup(string name) =>
            byName.TryGetValue(name, out var node) ? node : name == "character_top" ? FileCollider.CharacterTop : null;
    }

    private static IEnumerable<ClothPartDefinition> ReadDefinitions(FltdFile file)
    {
        var header = file.Header;
        int count = file.Byte(header + 2), mainCount = file.Byte(header + 1);
        var first = file.Pointer(header + 8, count * 28);
        var main = file.Pointer(header + 4, mainCount * 20);
        var mirror = new Dictionary<int, SortedSet<int>>();
        for (var i = 0; i < mainCount; i++)
        {
            int part = file.Byte(main + i * 20), bits = file.Byte(main + i * 20 + 3) & 3;
            if (!mirror.TryGetValue(part, out var set))
            {
                mirror[part] = set = [];
            }

            set.Add(bits);
        }

        for (var part = 0; part < count; part++)
        {
            if (!mirror.TryGetValue(part, out var signs))
            {
                continue;
            }

            foreach (var sign in signs)
            {
                yield return ReadDefinition(file, first + part * 28, part, sign);
            }
        }
    }

    private static ClothPartDefinition ReadDefinition(FltdFile file, int offset, int part, int sign)
    {
        int partFlags = file.Byte(offset), n = file.Byte(offset + 1), wrapperOptions = file.Byte(offset + 3);
        int skipCount = file.Byte(offset + 4), low = file.Byte(offset + 5), modifierCount = file.Byte(offset + 7);
        if (n is < 2 or > 64)
        {
            throw new InvalidDataException("FLTD cloth particle count must be within 2..64.");
        }

        var overrides = ReadModifiers(file, offset, modifierCount, n);
        var skip = file.Pointer(offset + 8, skipCount);
        var skipMask = 0UL;
        for (var i = 0; i < skipCount; i++)
        {
            var id = file.Byte(skip + i);
            if (id >= n)
            {
                throw new InvalidDataException("FLTD skip list exceeds the particle count.");
            }

            skipMask |= 1UL << id;
        }

        var config = file.Pointer(offset + 12, 60);
        var head = file.Floats(config, 3);
        float mass = head[0], damping = head[1], air = head[2];
        if (MathF.Abs(mass) < ClothMath.Small || damping < 0 || air < 0)
        {
            throw new InvalidDataException("Invalid FLTD mass/drag coefficients.");
        }

        int version = file.Byte(config + 12), multiplier = file.Byte(config + 13), modes = file.Byte(config + 14);
        if (version is not (31 or 32 or 33))
        {
            throw new InvalidDataException($"FLTD solver version {version} is not supported (31..33 only).");
        }

        int targetCount = file.Byte(config + 17), angleCount = file.Byte(config + 18), distanceCount = file.Byte(config + 19);
        var target = file.Pointer(config + 32, targetCount * 24);
        var angle = file.Pointer(config + 36, angleCount * 24);
        var distance = file.Pointer(config + 40, distanceCount * 20);
        var target0 = file.Floats(file.Pointer(config + 44, n * 4), n);
        var target1 = file.Floats(file.Pointer(config + 48, n * 4), n);
        var extras = file.Pointer(config + 56, 16);
        var particles = file.Pointer(extras, n * 32);
        var contacts = file.Pointer(extras + 4, distanceCount * 20);
        var normal = file.Pointer(config + 20, n);

        var inverseMass = Enumerable.Repeat(1f / mass, n).ToArray();
        var force0 = Enumerable.Repeat(mass, n).ToArray();
        var force1 = Enumerable.Repeat(1f, n).ToArray();
        var radius = new float[n];
        var friction0 = Enumerable.Repeat(1f, n).ToArray();
        var friction1 = Enumerable.Repeat(1f, n).ToArray();
        var particleFlags = new uint[n];
        for (var i = 0; i < n; i++)
        {
            var entry = particles + i * 32;
            var p = file.Floats(entry, 5);
            if (p[0] >= ClothMath.Small)
            {
                force0[i] = p[0];
                inverseMass[i] = 1f / p[0];
            }

            if (p[1] <= 0)
            {
                force1[i] = 1f / -9.8f * p[1];
            }

            if (p[2] >= 0) radius[i] = p[2];
            if (p[3] is >= 0 and <= 1) friction0[i] = p[3];
            if (p[4] is >= 0 and <= 1) friction1[i] = p[4];
            particleFlags[i] = file.U32(entry + 20);
        }

        for (var i = 0; i < n; i++)
        {
            if ((skipMask >> i & 1) != 0) inverseMass[i] = 0f;
        }

        var distances = new DistanceConstraint[distanceCount];
        for (var i = 0; i < distanceCount; i++)
        {
            var entry = distance + i * 20;
            int bits = file.Byte(entry), a = file.Byte(entry + 1), b = file.Byte(entry + 2), percentage = file.Byte(entry + 3);
            var v = file.Floats(entry + 4, 4);
            var contact = file.Floats(contacts + i * 20, 2);
            if (a >= n || b >= n)
            {
                throw new InvalidDataException("FLTD distance endpoints exceed the particle count.");
            }

            var flags = (bits & 1) | (a == b - 1 ? 2 : 0) | (v[0] > 0 || v[1] > 0 ? 4 : 0) | ((bits & 0x1E) << 2);
            distances[i] = new DistanceConstraint(a, b, v[2], percentage * 0.01f, v[3], flags, v[0], v[1], contact[0], contact[1]);
        }

        var angular = ReadAngles(file, angle, angleCount, n, false, (sign & 1) != 0, (sign & 2) != 0, version);
        var targetAngles = ReadAngles(file, target, targetCount, n, true, (sign & 1) != 0, (sign & 2) != 0, version);

        var packed = new byte[32];
        var hasNormal = false;
        for (var i = 0; i < n; i++)
        {
            var value = file.Byte(normal + i);
            if (value >= 5)
            {
                throw new InvalidDataException("Unsupported FLTD reflection normal mode.");
            }

            packed[i / 2] |= (byte)(value << (4 * (i & 1)));
            hasNormal |= value != 0;
        }

        var solverFlags = 0x1F | ((partFlags & 8) != 0 ? 0x20 : 0) | ((partFlags & 16) != 0 ? 0x40 : 0);
        solverFlags |= (hasNormal ? 0x80 : 0) | ((modes & 1) != 0 ? 0x400 : 0) | ((modes & 2) != 0 ? 0x2000 : 0);
        solverFlags |= ((sign & 1) != 0 ? 0x100 : 0) | ((sign & 2) != 0 ? 0x200 : 0);

        ulong Mask(uint bit)
        {
            var mask = 0UL;
            for (var i = 0; i < n; i++)
            {
                if ((particleFlags[i] & bit) != 0) mask |= 1UL << i;
            }

            return mask;
        }

        var wrapperFlags = 1 | ((partFlags & 1) != 0 ? 2 : 0) | ((partFlags & 2) != 0 ? 0x40 : 0);
        wrapperFlags |= ((partFlags & 0x40) != 0 ? 8 : 0) | ((partFlags & 0x80) != 0 ? 0x10 : 0);
        wrapperFlags |= ((wrapperOptions & 2) != 0 ? 0x800 : 0) | ((wrapperOptions & 4) != 0 ? 0x1000 : 0);
        wrapperFlags |= (wrapperOptions & 1) != 0 ? 0x20 : 0;

        return new ClothPartDefinition
        {
            PartIndex = part,
            ParticleCount = n,
            MirrorSign = sign,
            InverseMass = inverseMass,
            Constraints = distances,
            ForceWeights0 = force0,
            ForceWeights1 = force1,
            TargetWeights0 = target0,
            TargetWeights1 = target1,
            NormalIterations = low,
            AlternateIterations = (int)file.U32(offset + 20),
            Damping = damping,
            AirResistance = air,
            Flags = solverFlags,
            SolverVersion = version,
            MassCoefficient = mass,
            Angular = angular,
            TargetAngular = targetAngles,
            CollisionRadii = radius,
            CollisionWeights0 = friction0,
            CollisionWeights1 = friction1,
            PackedNormalModes = packed,
            ParticleFlags = particleFlags,
            UnitMask = Mask(0x10),
            UniformMask = Mask(0x20),
            OrthogonalMask = Mask(8),
            ExclusionMask = Mask(0x40),
            SkipMask = skipMask,
            WrapperFlags = wrapperFlags,
            ConvergenceMultiplier = Math.Max(1, multiplier),
            ReferenceOverrides = overrides,
        };
    }

    private static AngularConstraint[] ReadAngles(
        FltdFile file, int start, int count, int n, bool target, bool flip0, bool flip1, int version)
    {
        var result = new AngularConstraint[count];
        for (var i = 0; i < count; i++)
        {
            var offset = start + i * 24;
            int a = file.Byte(offset), b = file.Byte(offset + 1), c = file.Byte(offset + 2), d = file.Byte(offset + 3);
            var firstAngle = target ? c : d;
            var secondAngle = file.Byte(offset + 20);
            var (cos0, sin0) = firstAngle <= 90 ? CosSin(firstAngle) : (0f, 0f);
            var (cos1, sin1) = secondAngle < 90 ? CosSin(secondAngle) : (0f, 0f);
            var xy = file.Floats(offset + 12, 2);
            var x = flip0 ? -xy[0] : xy[0];
            var y = flip1 ? -xy[1] : xy[1];
            var bits = file.Byte(offset + 21);
            var flags = ((bits & 1) != 0 && version >= 31 ? 1 : 0) | ((bits & 2) != 0 && version >= 32 ? 2 : 0);
            int parent, child, reference;
            bool relative;
            if (target)
            {
                (parent, child, reference, relative) = (a, b, -1, Math.Abs(a - b) >= 2);
                if (a >= n || b >= n) throw new InvalidDataException("FLTD angular endpoints exceed the particle count.");
            }
            else
            {
                (reference, parent, child, relative) = (a, b, c, false);
                if (a >= n || b >= n || c >= n) throw new InvalidDataException("FLTD angular endpoints exceed the particle count.");
            }

            result[i] = new AngularConstraint(cos0, sin0, x, y, cos1, sin1, 1f, flags, parent, child, reference, relative);
        }

        return result;
    }

    private static (float Cos, float Sin) CosSin(int degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return ((float)Math.Cos(radians), (float)Math.Sin(radians));
    }

    /// <summary>
    /// Type-2 modifiers only (the reference-pose override); 2978EE0 runs one
    /// only in mode 0. Each 0x1C-byte entry: particle, pad, position, degrees.
    /// </summary>
    private static ReferenceOverride[] ReadModifiers(FltdFile file, int offset, int count, int n)
    {
        if (count == 0)
        {
            return [];
        }

        var records = file.Pointer(offset + 16, count * 16);
        var output = new List<ReferenceOverride>();
        for (var k = 0; k < count; k++)
        {
            var kind = file.U32(records + k * 16);
            var data = file.U32(records + k * 16 + 4);
            if (kind != 2)
            {
                throw new InvalidDataException("FLTD modifier callbacks other than the reference-pose override are not supported.");
            }

            var head = file.Base + (int)data;
            file.Require(head, 8);
            int mode = file.Byte(head), entries = file.Byte(head + 1);
            if (mode != 0)
            {
                continue;
            }

            var table = file.Base + (int)file.U32(head + 4);
            file.Require(table, entries * 0x1C);
            for (var e = 0; e < entries; e++)
            {
                var at = table + e * 0x1C;
                var particle = file.Byte(at);
                if (particle >= n) throw new InvalidDataException("FLTD modifier particle exceeds the count.");
                var position = file.Floats(at + 4, 3);
                var angles = file.Floats(at + 16, 3);
                output.Add(new ReferenceOverride(
                    particle,
                    new Vector3(position[0], position[1], position[2]),
                    new Vector3(angles[0], angles[1], angles[2])));
            }
        }

        return output.ToArray();
    }

    private sealed class FltdFile
    {
        private readonly byte[] _data;

        public FltdFile(byte[] data)
        {
            _data = data;
            Require(0, 32);
            if (Encoding.ASCII.GetString(_data, 0, 4) != "NIFL") throw new InvalidDataException("FLTD is not an unpacked NIFL file.");
            Base = (int)U32(12);
            Require(Base, 16);
            if (Encoding.ASCII.GetString(_data, Base, 4) != "REL0") throw new InvalidDataException("FLTD has no REL0 section.");
            Header = Base + (int)U32(Base + 8);
            Require(Header, 20);
            if (_data[Header] != 10) throw new InvalidDataException("Only FLTD file version 10 is supported.");
        }

        public int Base { get; }

        public int Header { get; }

        public void Require(int start, int size)
        {
            if (start < 0 || size < 0 || start + size > _data.Length)
            {
                throw new InvalidDataException("FLTD field points outside the file.");
            }
        }

        public int Byte(int offset)
        {
            Require(offset, 1);
            return _data[offset];
        }

        public byte[] Bytes(int offset, int count)
        {
            Require(offset, count);
            return _data.AsSpan(offset, count).ToArray();
        }

        public uint U32(int offset)
        {
            Require(offset, 4);
            return BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset));
        }

        public int Pointer(int offset, int size)
        {
            var pointer = U32(offset);
            if (pointer is 0 or 16)
            {
                if (size != 0) throw new InvalidDataException("A required FLTD vector is missing.");
                return 0;
            }

            var result = Base + (int)pointer;
            Require(result, size);
            return result;
        }

        public float[] Floats(int offset, int count)
        {
            Require(offset, count * 4);
            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(offset + i * 4));
                if (!float.IsFinite(result[i])) throw new InvalidDataException("FLTD coefficients must be finite.");
            }

            return result;
        }

        public string String(uint pointer)
        {
            if (pointer is 0 or 16) throw new InvalidDataException("Missing FLTD node name.");
            var start = Base + (int)pointer;
            Require(start, 1);
            var end = Array.IndexOf(_data, (byte)0, start, Math.Min(512, _data.Length - start));
            if (end < 0) throw new InvalidDataException("Unterminated FLTD node name.");
            return Encoding.UTF8.GetString(_data, start, end - start);
        }
    }
}
