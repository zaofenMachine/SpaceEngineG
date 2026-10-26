using System;

namespace SpaceEngine.Core
{
    /// <summary>
    /// Integer sectors plus a bounded double offset, in metres. Local axes are in [-5e11, 5e11).
    /// Adjacent positions must be subtracted BEFORE converting sector indices to double.
    /// This keeps sub-millimetre precision even at galactic coordinates.
    /// </summary>
    [Serializable]
    public struct UniversalPosition : IEquatable<UniversalPosition>
    {
        public const double SectorSize = 1e12;
        public long sectorX, sectorY, sectorZ;
        public Double3 local;
        public static UniversalPosition Origin => new UniversalPosition(0, 0, 0, Double3.Zero);

        public UniversalPosition(long sectorX, long sectorY, long sectorZ, Double3 local)
        {
            this.sectorX = sectorX; this.sectorY = sectorY; this.sectorZ = sectorZ; this.local = local;
            NormalizeAxis(ref this.sectorX, ref this.local.x);
            NormalizeAxis(ref this.sectorY, ref this.local.y);
            NormalizeAxis(ref this.sectorZ, ref this.local.z);
        }

        public UniversalPosition Add(Double3 delta) => new UniversalPosition(sectorX, sectorY, sectorZ, local + delta);

        public Double3 RelativeTo(UniversalPosition origin) => new Double3(
            AxisDifference(sectorX, local.x, origin.sectorX, origin.local.x),
            AxisDifference(sectorY, local.y, origin.sectorY, origin.local.y),
            AxisDifference(sectorZ, local.z, origin.sectorZ, origin.local.z));

        private static double AxisDifference(long aSector, double aLocal, long bSector, double bLocal)
        {
            // decimal subtraction is exact for the entire Int64 range and cannot overflow as Int64 subtraction can.
            double sectors = (double)((decimal)aSector - bSector);
            return sectors * SectorSize + (aLocal - bLocal);
        }

        private static void NormalizeAxis(ref long sector, ref double offset)
        {
            if (double.IsNaN(offset) || double.IsInfinity(offset)) throw new ArgumentOutOfRangeException(nameof(offset), "Position must be finite.");
            double shift = Math.Truncate(offset / SectorSize);
            double remainder = offset - shift * SectorSize;
            if (remainder >= SectorSize * 0.5) { shift += 1; remainder -= SectorSize; }
            if (remainder < -SectorSize * 0.5) { shift -= 1; remainder += SectorSize; }
            sector = checked((long)((decimal)sector + (decimal)shift));
            offset = remainder;
        }

        public bool Equals(UniversalPosition other) => sectorX == other.sectorX && sectorY == other.sectorY && sectorZ == other.sectorZ && local.Equals(other.local);
        public override bool Equals(object obj) => obj is UniversalPosition other && Equals(other);
        public override int GetHashCode() { unchecked { return (((sectorX.GetHashCode() * 397) ^ sectorY.GetHashCode()) * 397 ^ sectorZ.GetHashCode()) * 397 ^ local.GetHashCode(); } }
        public override string ToString() => $"[{sectorX},{sectorY},{sectorZ}] + {local} m";
    }
}
