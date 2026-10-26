using System;
using System.Collections.Generic;
using SpaceEngine.Core;

namespace SpaceEngine.Runtime
{
    /// <summary>A generated address, independent of the current loaded reference frame.</summary>
    public sealed class CelestialTarget
    {
        public long X,Y,Z;
        public int BodyIndex=-1;
        public StarSystemDefinition System;
        public bool IsStar => BodyIndex<0;
        public PlanetDefinition Body => IsStar?null:System.Planets[BodyIndex];
        public string Name => IsStar?System.Name:Body.Name;
        public double Radius => IsStar?System.StarRadius:Body.Radius;
        public string Id => X+"/"+Y+"/"+Z+":"+BodyIndex;
        public UniversalPosition Position(double time)
        {
            if(IsStar)return System.Position;
            var sum=Double3.Zero;int index=BodyIndex;
            for(int depth=0;depth<System.Planets.Length;depth++)
            {
                var body=System.Planets[index];int parent=body.ParentIndex;
                sum+=Orbit.Evaluate(body,parent<0?System.StarMass:System.Planets[parent].Mass,time);
                if(parent<0)return System.Position.Add(sum);
                index=parent;
            }
            throw new InvalidOperationException("Cyclic orbit hierarchy");
        }
    }

    public sealed class CatalogueFilter
    {
        public string Query="";
        public int PlanetKind=-1;
        public bool LandableOnly,OceanOnly,AtmosphereOnly,MoonsOnly;
        public double MaximumDistanceLy=100;
        public bool Matches(CelestialTarget target,UniversalPosition observer,double time)
        {
            if(target==null||target.System==null||!target.System.IsOccupied)return false;
            if(target.IsStar)
            {if(PlanetKind>=0||LandableOnly||OceanOnly||AtmosphereOnly||MoonsOnly)return false;}
            else
            {
                var p=target.Body;
                if(PlanetKind>=0&&(int)p.Kind!=PlanetKind)return false;
                if(LandableOnly&&!p.IsLandable||OceanOnly&&!p.HasOcean||AtmosphereOnly&&p.AtmosphereHeight<=0||MoonsOnly&&!p.IsMoon)return false;
            }
            if(target.Position(time).RelativeTo(observer).Length>Math.Max(0,MaximumDistanceLy)*PhysicalConstants.LightYear)return false;
            string query=(Query??"").Trim();
            if(query.Length==0)return true;
            string text=target.Name+" "+target.System.Name+" "+target.Id+" "+(target.IsStar?target.System.Kind.ToString()+" 恒星 "+StellarName(target.System.Kind):target.Body.Kind.ToString()+" "+PlanetName(target.Body.Kind)+(target.Body.IsMoon?" 卫星":" 行星")+(target.Body.HasOcean?" 海洋":""));
            foreach(string term in query.Split(new[]{' ','　'},StringSplitOptions.RemoveEmptyEntries))
                if(text.IndexOf(term,StringComparison.OrdinalIgnoreCase)<0)return false;
            return true;
        }
        public static string PlanetName(PlanetKind kind)=>ChineseText.PlanetKind(kind);
        public static string StellarName(StellarKind kind)=>ChineseText.StellarKind(kind);
    }

    public static class FlightSpeedControl
    {
        // Input System devices report either one unit or the Windows 120-unit detent.
        public static double WheelNotches(float delta)
        {if(float.IsNaN(delta)||float.IsInfinity(delta))return 0;return Math.Abs(delta)>=10?delta/120.0:delta;}
        public static double Adjust(double multiplier,double notches,double factor=1.6)
        {if(!double.IsFinite(notches)||!double.IsFinite(multiplier))return 1;return Math.Clamp(multiplier*Math.Pow(Math.Clamp(factor,1.01,10),notches),.0001,1e12);}
    }
}
