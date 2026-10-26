using System;

namespace SpaceEngine.Core
{
    /// <summary>Explicit SplitMix64 stream, independent of UnityEngine.Random and runtime hash randomization.</summary>
    public struct DeterministicRandom
    {
        private ulong state;
        public DeterministicRandom(ulong seed) { state = seed; }
        public ulong NextUInt64() { unchecked { state += 0x9e3779b97f4a7c15UL; return Mix(state); } }
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
        public double Range(double min, double max) => min + (max - min) * NextDouble();
        public int Range(int min, int exclusiveMax)
        {
            if (exclusiveMax <= min) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            return min + (int)(NextDouble() * ((long)exclusiveMax - min));
        }
        public static ulong Mix(ulong value)
        {
            unchecked
            {
                value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }
        public static ulong Hash(int seed, long x, long y, long z)
        {
            unchecked
            {
                ulong h = Mix((ulong)(long)seed + 0x9e3779b97f4a7c15UL);
                h = Mix(h ^ (ulong)x); h = Mix(h ^ (ulong)y + 0x632be59bd9b4e019UL);
                return Mix(h ^ (ulong)z + 0x8cb92ba72f3d8dd7UL);
            }
        }
    }
}
