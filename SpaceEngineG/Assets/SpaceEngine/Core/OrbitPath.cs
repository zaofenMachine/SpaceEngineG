using System;
namespace SpaceEngine.Core
{
    /// <summary>Samples the same Kepler solution as the body; points stay parent-relative SI.</summary>
    public static class OrbitPath
    {
        public static Double3[] Sample(PlanetDefinition body,double parentMass,int segments=160)
        {
            if(segments<16||segments>2048)throw new ArgumentOutOfRangeException(nameof(segments));
            var result=new Double3[segments+1];double period=Orbit.Period(body,parentMass);
            for(int i=0;i<segments;i++)result[i]=Orbit.Evaluate(body,parentMass,period*i/segments);
            result[segments]=result[0];return result;
        }
    }
}
