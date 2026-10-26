using System;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public class CoordinateTests
    {
        [Test]
        public void MillimetreMotionSurvivesGalacticAndInt64SectorCoordinates()
        {
            foreach (long sector in new[] { 0L, 900000000L, -900000000L, long.MaxValue - 2, long.MinValue + 2 })
            {
                var start = new UniversalPosition(sector, sector, sector, new Double3(100, -200, 300));
                Double3 actual = start.Add(new Double3(0.001, -0.002, 0.003)).RelativeTo(start);
                Assert.That(actual.x, Is.EqualTo(0.001).Within(1e-10));
                Assert.That(actual.y, Is.EqualTo(-0.002).Within(1e-10));
                Assert.That(actual.z, Is.EqualTo(0.003).Within(1e-10));
            }
        }

        [Test]
        public void SmallMovementAcrossSectorBoundaryRemainsAccurate()
        {
            var start = new UniversalPosition(900000000, -900000000, 0, new Double3(5e11 - 0.25, -5e11 + 0.25, 0));
            var end = start.Add(new Double3(0.5, -0.5, 0.125));
            Assert.That(end.sectorX, Is.EqualTo(start.sectorX + 1));
            Assert.That(end.sectorY, Is.EqualTo(start.sectorY - 1));
            Assert.That(end.RelativeTo(start).x, Is.EqualTo(0.5).Within(0.0002));
            Assert.That(end.RelativeTo(start).y, Is.EqualTo(-0.5).Within(0.0002));
            Assert.That(end.RelativeTo(start).z, Is.EqualTo(0.125));
        }

        [Test]
        public void WorstCaseSectorOffsetStillResolvesMillimetresWithinPointOneMillimetre()
        {
            var start = new UniversalPosition(long.MaxValue - 2, 0, 0, new Double3(5e11 - 100, 0, 0));
            Assert.That(start.Add(new Double3(0.001, 0, 0)).RelativeTo(start).x, Is.EqualTo(0.001).Within(0.0001));
        }

        [TestCase(500000000000d, 1, -500000000000d)]
        [TestCase(-500000000000d, 0, -500000000000d)]
        [TestCase(-500000000001d, -1, 499999999999d)]
        [TestCase(2250000000000d, 2, 250000000000d)]
        [TestCase(-2250000000000d, -2, -250000000000d)]
        [TestCase(-0.001d, 0, -0.001d)]
        public void SectorNormalizationUsesSymmetricHalfOpenIntervals(double offset, long expectedSector, double expectedOffset)
        {
            var p = new UniversalPosition(0, 0, 0, new Double3(offset, 0, 0));
            Assert.That(p.sectorX, Is.EqualTo(expectedSector));
            Assert.That(p.local.x, Is.EqualTo(expectedOffset).Within(0.0001));
            Assert.That(p.RelativeTo(UniversalPosition.Origin).x, Is.EqualTo(offset).Within(0.0001));
        }

        [Test]
        public void RelativePositionDoesNotOverflowWhenSectorsSpanInt64Range()
        {
            var a = new UniversalPosition(long.MaxValue, 0, 0, Double3.Zero);
            var b = new UniversalPosition(long.MinValue, 0, 0, Double3.Zero);
            double distance = a.RelativeTo(b).x;
            Assert.That(distance, Is.GreaterThan(1e31));
            Assert.That(double.IsInfinity(distance), Is.False);
        }

        [Test]
        public void InvalidPositionsAndRepresentableRangeOverflowAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniversalPosition(0, 0, 0, new Double3(double.NaN, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniversalPosition(0, 0, 0, new Double3(0, double.PositiveInfinity, 0)));
            Assert.Throws<OverflowException>(() => new UniversalPosition(long.MaxValue, 0, 0, new Double3(1e12, 0, 0)));
        }

        [Test]
        public void VectorBasisAndNormalizationAreConsistent()
        {
            var cross = Double3.Cross(new Double3(1, 0, 0), new Double3(0, 1, 0));
            Assert.That(cross.Equals(new Double3(0, 0, 1)), Is.True);
            Assert.That(new Double3(3, 4, 0).Normalized.Length, Is.EqualTo(1).Within(1e-14));
            Assert.That(Double3.Zero.Normalized.Equals(Double3.Zero), Is.True);
        }
    }
}
