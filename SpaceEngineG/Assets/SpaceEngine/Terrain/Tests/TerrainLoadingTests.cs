using System;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Terrain.Tests
{
    public sealed class TerrainLoadingTests
    {
        [TestCase(1,.23,.41)]
        [TestCase(-1,.23,.41)]
        [TestCase(.23,1,.41)]
        [TestCase(.23,-1,.41)]
        [TestCase(.23,.41,1)]
        [TestCase(.23,.41,-1)]
        public void FirstSubmittedColdFrameHasFacingProceduralSurfaceWithinOneBuildBudget(double x,double y,double z)
        {
            var root=new GameObject("Cold terrain frame test");var material=new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                var planet=UniverseGenerator.GenerateSystem(20261026,0,0,0).Planets[0];
                Double3 direction=new Double3(x,y,z).Normalized;
                Double3 from=TerrainPatchMesh.SurfacePoint(direction,planet)+direction*600000;
                var terrain=root.AddComponent<PlanetTerrain>();terrain.BuildsPerFrame=1;terrain.BuildBudgetMilliseconds=1000;
                terrain.MaterialMapResolution=64;terrain.Configure(planet,material);
                Assert.That(terrain.ActivePatchCount,Is.Zero,"Configure has no camera-relative frame; the caller must submit before rendering.");
                terrain.Tick(from,.05,Vector3.zero,direction.ToVector3(),1f/30);
                Assert.That(terrain.BuiltThisFrame,Is.EqualTo(1));Assert.That(terrain.ActivePatchCount,Is.GreaterThan(0));
                Assert.That(terrain.TrySampleRenderedSurface(direction,out Double3 surface,out _,out _),Is.True);
                Assert.That(double.IsNaN(surface.Length)||double.IsInfinity(surface.Length),Is.False);
                Assert.That(surface.Length,Is.GreaterThan(planet.Radius-planet.TerrainHeight*4));
                Assert.That(terrain.PendingBuildCount,Is.GreaterThan(0),"Coverage must already exist while higher detail is still loading.");
                var states=new System.Collections.Generic.List<TerrainPatchState>();terrain.CopyActivePatchStates(states);
                Assert.That(states.Count,Is.GreaterThan(0));foreach(var state in states)Assert.That(state.Morph,Is.EqualTo(1));
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
