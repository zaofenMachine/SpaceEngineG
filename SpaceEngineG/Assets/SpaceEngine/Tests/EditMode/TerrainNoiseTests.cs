using System;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public class TerrainNoiseTests
    {
        private static PlanetDefinition Planet(int seed) => new PlanetDefinition { Seed = seed, TerrainHeight = 12000, HasOcean = true };

        [Test]
        public void SurfaceIsDeterministicAndDependsOnSeed()
        {
            var dir = new Double3(0.23, 0.54, -0.14).Normalized;
            double a = TerrainNoise.Height(dir, Planet(1234));
            Assert.That(TerrainNoise.Height(dir, Planet(1234)), Is.EqualTo(a));
            Assert.That(Math.Abs(TerrainNoise.Height(dir, Planet(4321)) - a), Is.GreaterThan(1));
            Assert.That(TerrainNoise.Height(dir * 9, Planet(1234)), Is.EqualTo(a).Within(1e-8));
        }

        [Test]
        public void LongitudeSeamAndPolesAreContinuous()
        {
            var planet = Planet(1234);
            Double3[] a = { new Double3(-1, 0.1, 1e-9), new Double3(1e-9, 1, 0), new Double3(1e-9, -1, 0) };
            Double3[] b = { new Double3(-1, 0.1, -1e-9), new Double3(-1e-9, 1, 0), new Double3(-1e-9, -1, 0) };
            for (int i = 0; i < a.Length; i++)
                Assert.That(Math.Abs(TerrainNoise.Height(a[i], planet) - TerrainNoise.Height(b[i], planet)), Is.LessThan(0.1));
        }

        [Test]
        public void GlobeHasBoundedVariedTerrainAndBothLandAndSeabed()
        {
            var planet = Planet(1234);
            double min = double.MaxValue, max = double.MinValue, sum = 0, sumSquares = 0;
            const int count = 1024;
            for (int i = 0; i < count; i++)
            {
                double y = 1 - 2 * (i + 0.5) / count;
                double azimuth = i * Math.PI * (3 - Math.Sqrt(5));
                double r = Math.Sqrt(1 - y * y);
                double height = TerrainNoise.Height(new Double3(r * Math.Cos(azimuth), y, r * Math.Sin(azimuth)), planet);
                Assert.That(height, Is.InRange(-planet.TerrainHeight, planet.TerrainHeight));
                min = Math.Min(min, height); max = Math.Max(max, height); sum += height; sumSquares += height * height;
            }
            Assert.That(min, Is.LessThan(-100)); Assert.That(max, Is.GreaterThan(100));
            Assert.That(Math.Sqrt(sumSquares / count - (sum / count) * (sum / count)), Is.GreaterThan(500));
        }
    }
}
