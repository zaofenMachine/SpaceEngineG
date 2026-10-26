using System;
using SpaceEngine.Core;

namespace SpaceEngine.Terrain
{
    /// <summary>Integer address of one deterministic cube-sphere tile.</summary>
    public readonly struct TerrainPatchKey : IEquatable<TerrainPatchKey>
    {
        public readonly int Face, Level, X, Y;
        public TerrainPatchKey(int face, int level, int x, int y)
        {
            Face = face; Level = level; X = x; Y = y;
        }
        public TerrainPatchKey Child(int index) => new TerrainPatchKey(Face, Level + 1, X * 2 + (index & 1), Y * 2 + (index >> 1));
        public bool Equals(TerrainPatchKey other) => Face == other.Face && Level == other.Level && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is TerrainPatchKey other && Equals(other);
        public override int GetHashCode() => unchecked((((Face * 397) ^ Level) * 397 ^ X) * 397 ^ Y);
        public override string ToString() => $"F{Face}/L{Level}/{X}/{Y}";
    }

    /// <summary>
    /// Six consistent face frames. Heights are sampled in normalized 3D space,
    /// so the two charts meeting at a cube edge sample exactly the same field.
    /// </summary>
    public static class CubeSphere
    {
        public static Double3 Direction(int face, double u, double v)
        {
            Double3 p;
            switch (face)
            {
                case 0: p = new Double3(1, v, -u); break;
                case 1: p = new Double3(-1, v, u); break;
                case 2: p = new Double3(u, 1, -v); break;
                case 3: p = new Double3(u, -1, v); break;
                case 4: p = new Double3(u, v, 1); break;
                case 5: p = new Double3(-u, v, -1); break;
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
            return p.Normalized;
        }

        public static void Bounds(TerrainPatchKey key, out double u, out double v, out double size)
        {
            size = 2.0 / (1 << key.Level);
            u = -1.0 + key.X * size;
            v = -1.0 + key.Y * size;
        }

        public static Double3 CenterDirection(TerrainPatchKey key)
        {
            Bounds(key, out double u, out double v, out double size);
            return Direction(key.Face, u + size * .5, v + size * .5);
        }

        public static void ProjectToFace(int face, Double3 direction, out double u, out double v)
        {
            double denominator;
            switch (face)
            {
                case 0: denominator = Math.Max(1e-15, direction.x); u = -direction.z / denominator; v = direction.y / denominator; break;
                case 1: denominator = Math.Max(1e-15, -direction.x); u = direction.z / denominator; v = direction.y / denominator; break;
                case 2: denominator = Math.Max(1e-15, direction.y); u = direction.x / denominator; v = -direction.z / denominator; break;
                case 3: denominator = Math.Max(1e-15, -direction.y); u = direction.x / denominator; v = direction.z / denominator; break;
                case 4: denominator = Math.Max(1e-15, direction.z); u = direction.x / denominator; v = direction.y / denominator; break;
                case 5: denominator = Math.Max(1e-15, -direction.z); u = -direction.x / denominator; v = direction.y / denominator; break;
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        public static double Dot(Double3 a, Double3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Double3 Cross(Double3 a, Double3 b) => new Double3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }
}
