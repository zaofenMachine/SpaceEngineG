using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>Double precision SI vector. Convert to float only after subtracting the render origin.</summary>
    [Serializable]
    public struct Double3 : IEquatable<Double3>
    {
        public double x, y, z;
        public Double3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static Double3 Zero => new Double3(0, 0, 0);
        public double SqrLength => x * x + y * y + z * z;
        public double Length => Math.Sqrt(SqrLength);
        public Double3 Normalized => Length > 0 ? this / Length : Zero;
        public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);
        public static Double3 FromVector3(Vector3 value) => new Double3(value.x, value.y, value.z);
        public static double Dot(Double3 a, Double3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Double3 Cross(Double3 a, Double3 b) => new Double3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Double3 operator -(Double3 a) => new Double3(-a.x, -a.y, -a.z);
        public static Double3 operator *(Double3 a, double b) => new Double3(a.x * b, a.y * b, a.z * b);
        public static Double3 operator *(double a, Double3 b) => b * a;
        public static Double3 operator /(Double3 a, double b) => new Double3(a.x / b, a.y / b, a.z / b);
        public bool Equals(Double3 other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);
        public override bool Equals(object obj) => obj is Double3 other && Equals(other);
        public override int GetHashCode() { unchecked { return (x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode(); } }
        public override string ToString() => $"({x:G17}, {y:G17}, {z:G17})";
    }
}
