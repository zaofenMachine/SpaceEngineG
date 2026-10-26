using NUnit.Framework;
using SpaceEngine.Core;
namespace SpaceEngine.Tests
{
    public sealed class OrbitPathTests
    {
        [Test] public void ClosedPathUsesTheActualKeplerSolution()
        {
            var system=UniverseGenerator.GenerateSystem(20261026,0,0,0);
            foreach(var body in system.Planets)
            {
                double mass=body.ParentIndex<0?system.StarMass:system.Planets[body.ParentIndex].Mass;
                var path=OrbitPath.Sample(body,mass,128);Assert.That(path.Length,Is.EqualTo(129));Assert.That((path[128]-path[0]).Length,Is.Zero);
                for(int i=0;i<128;i++)Assert.That((path[i]-Orbit.Evaluate(body,mass,Orbit.Period(body,mass)*i/128)).Length,Is.LessThan(.001));
            }
        }
        [TestCase(0)] [TestCase(15)] [TestCase(2049)]
        public void InvalidSamplingBudgetsAreRejected(int count)
        {var s=UniverseGenerator.GenerateSystem(1,0,0,0);Assert.Throws<System.ArgumentOutOfRangeException>(()=>OrbitPath.Sample(s.Planets[0],s.StarMass,count));}
    }
}
