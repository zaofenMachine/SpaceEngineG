using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Terrain.Tests
{
    public sealed class TerrainLodContinuityTests
    {
        static PlanetDefinition Planet => new PlanetDefinition
        { Name="Continuity",Seed=123456,Radius=6371000,TerrainHeight=12000,HasOcean=false,Kind=PlanetKind.Rocky,SurfaceTint=Color.gray };

        [TestCase(3,5)]
        [TestCase(12,12)]
        [TestCase(19,8)]
        public void EveryChildStartsOnActualParentTrianglesWithIdenticalMaterialInputs(int level,int requestedResolution)
        {
            int resolution=TerrainPatchMesh.CompatibleResolution(requestedResolution);
            for(int face=0;face<6;face++)
            {
                int middle=1<<(level-1);
                var parentKey=new TerrainPatchKey(face,level,middle,middle);
                var parent=TerrainPatchMesh.Build(parentKey,Planet,requestedResolution);
                CubeSphere.Bounds(parentKey,out _,out _,out double span);
                double floatBound=.001+Planet.Radius*span*2e-7;
                for(int childIndex=0;childIndex<4;childIndex++)
                {
                    var key=parentKey.Child(childIndex);
                    var child=TerrainPatchMesh.Build(key,Planet,requestedResolution);child.SetParent(key,parent);
                    for(int y=0;y<=resolution;y++)for(int x=0;x<=resolution;x++)
                    {
                        parent.Sample(((key.X&1)*resolution+x)*.5,((key.Y&1)*resolution+y)*.5,1,
                            out Double3 expected,out Vector3 n,out Vector4 color,out float height);
                        child.Sample(x,y,0,out Double3 actual,out Vector3 cn,out Vector4 cc,out float ch);
                        Assert.That((actual-expected).Length,Is.LessThan(floatBound),$"{key} vertex{x},{y}");
                        Assert.That((cn-n).magnitude,Is.LessThan(2e-6));
                        Assert.That((cc-color).magnitude,Is.LessThan(2e-6));
                        Assert.That(Math.Abs(ch-height),Is.LessThan(.003));
                        int i=y*(resolution+1)+x;
                        Assert.That(child.MorphBounds.Contains(child.Vertices[i]),Is.True);
                        Assert.That(child.MorphBounds.Contains(child.Vertices[i]+(Vector3)child.ParentData[i]),Is.True);
                    }
                    // Interior samples catch a bilinear interpolation or a
                    // diagonally incompatible topology despite matching corners.
                    for(int sample=0;sample<20;sample++)
                    {
                        double x=(sample*.731%1)*resolution,y=(sample*.379%1)*resolution;
                        parent.Sample(((key.X&1)*resolution+x)*.5,((key.Y&1)*resolution+y)*.5,1,out Double3 expected,out _,out _,out _);
                        child.Sample(x,y,0,out Double3 actual,out _,out _,out _);
                        Assert.That((actual-expected).Length,Is.LessThan(floatBound*2));
                        child.Sample(x,y,1,out Double3 fine,out _,out _,out _);
                        child.Sample(x,y,.5f,out Double3 halfway,out _,out _,out _);
                        Assert.That((halfway-(actual+fine)*.5).Length,Is.LessThan(floatBound));
                    }
                }
            }
        }

        [Test]
        public void StationaryViewStopsBuildingSwitchingAndChangingTheVisibleSurface()
        {
            WithTerrain(terrain=>
            {
                Double3 direction=new Double3(1,.123,.456).Normalized;
                Double3 camera=TerrainPatchMesh.SurfacePoint(direction,Planet)+direction*2;
                Settle(terrain,camera);
                var before=new List<TerrainPatchState>();var after=new List<TerrainPatchState>();terrain.CopyActivePatchStates(before);
                Assert.That(terrain.TrySampleRenderedSurface(direction,out Double3 point,out Vector3 normal,out float height),Is.True);
                for(int frame=0;frame<120;frame++)
                {
                    terrain.Tick(camera,1,Vector3.zero,Vector3.up,1f/30);
                    Assert.That(terrain.BuiltThisFrame,Is.Zero);Assert.That(terrain.LodSwitchesThisFrame,Is.Zero);
                    Assert.That(terrain.IsSettled,Is.True);terrain.CopyActivePatchStates(after);SameStates(before,after);
                    terrain.TrySampleRenderedSurface(direction,out Double3 next,out Vector3 nextNormal,out float nextHeight);
                    Assert.That(next,Is.EqualTo(point));Assert.That(nextNormal,Is.EqualTo(normal));Assert.That(nextHeight,Is.EqualTo(height));
                }
            });
        }

        [Test]
        public void ThresholdJitterKeepsExistingSplitCohortAndRoundTripRestoresSameSurface()
        {
            WithTerrain(terrain=>
            {
                terrain.MaximumLevel=8;terrain.MaximumVisiblePatches=128;terrain.LodHysteresis=.25f;
                Double3 direction=new Double3(1,.123,.456).Normalized;
                Double3 ground=TerrainPatchMesh.SurfacePoint(direction,Planet);
                Double3 close=ground+direction*8000;
                Settle(terrain,close);
                var reference=new List<TerrainPatchState>();var actual=new List<TerrainPatchState>();terrain.CopyActivePatchStates(reference);
                terrain.TrySampleRenderedSurface(direction,out Double3 first,out _,out _);
                int switches=0;
                for(int i=0;i<100;i++)
                {
                    terrain.Tick(ground+direction*(8000+(i%2==0?1:-1)),1,Vector3.zero,Vector3.up,1f/30);
                    switches+=terrain.LodSwitchesThisFrame;
                }
                Assert.That(switches,Is.Zero,"One metre of threshold jitter cannot continually rebuild/swap a cohort.");
                Settle(terrain,ground+direction*300000);Settle(terrain,close);
                // Hysteresis may retain additional valid tiles after a return;
                // the maximum-level triangle under the observer is identical.
                Assert.That(terrain.TrySampleRenderedSurface(direction,out Double3 returned,out _,out _),Is.True);
                Assert.That((returned-first).Length,Is.LessThan(.001));
                Settle(terrain,close);terrain.CopyActivePatchStates(actual);
                for(int i=0;i<40;i++)terrain.Tick(close,1,Vector3.zero,Vector3.up,1f/30);
                terrain.CopyActivePatchStates(reference);SameStates(actual,reference);
            });
        }

        [Test]
        public void DescentAndReverseTrajectoryKeepCoverageWithLowBuildBudget()
        {
            WithTerrain(terrain=>
            {
                terrain.BuildsPerFrame=2;terrain.MaximumLevel=12;terrain.MaximumResidentPatches=256;
                Double3 direction=new Double3(1,.123,.456).Normalized;
                Double3 ground=TerrainPatchMesh.SurfacePoint(direction,Planet);
                Settle(terrain,ground+direction*300000);
                for(int frame=0;frame<360;frame++)
                {
                    double phase=frame<180?frame/179.0:(359-frame)/179.0;
                    double altitude=Math.Exp(Math.Log(300000)*(1-phase)+Math.Log(20)*phase);
                    terrain.Tick(ground+direction*altitude,1,Vector3.zero,Vector3.up,1f/30);
                    Assert.That(terrain.TrySampleRenderedSurface(direction,out _,out _,out _),Is.True,$"Coverage lost at frame{frame}");
                    Assert.That(terrain.BuiltThisFrame,Is.LessThanOrEqualTo(2));
                    Assert.That(terrain.TransitioningPatchCount,Is.GreaterThanOrEqualTo(0));
                }
                Settle(terrain,ground+direction*20);
                Assert.That(terrain.MaxActiveLevel,Is.EqualTo(12));
                Assert.That(terrain.IsSettled,Is.True);
            });
        }

        [Test]
        public void IdenticalTimedDescentProducesIdenticalVisibleRepresentations()
        {
            WithTerrain(first=>WithTerrain(second=>
            {
                first.MaximumLevel=second.MaximumLevel=12;
                var a=new List<TerrainPatchState>();var b=new List<TerrainPatchState>();
                Double3 direction=new Double3(1,.123,.456).Normalized;
                Double3 ground=TerrainPatchMesh.SurfacePoint(direction,Planet);
                for(int frame=0;frame<220;frame++)
                {
                    double phase=Math.Min(1,frame/179.0);
                    double altitude=Math.Exp(Math.Log(300000)*(1-phase)+Math.Log(2)*phase);
                    Double3 camera=ground+direction*altitude;
                    first.Tick(camera,1,Vector3.zero,Vector3.up,1f/30);
                    second.Tick(camera,1,Vector3.zero,Vector3.up,1f/30);
                    first.CopyActivePatchStates(a);second.CopyActivePatchStates(b);SameStates(a,b);
                    Assert.That(first.PendingBuildCount,Is.EqualTo(second.PendingBuildCount));
                    Assert.That(first.TransitioningPatchCount,Is.EqualTo(second.TransitioningPatchCount));
                    bool found=first.TrySampleRenderedSurface(direction,out Double3 p,out Vector3 n,out float h);
                    Assert.That(second.TrySampleRenderedSurface(direction,out Double3 q,out Vector3 m,out float k),Is.EqualTo(found));
                    if(found){Assert.That(q,Is.EqualTo(p));Assert.That(m,Is.EqualTo(n));Assert.That(k,Is.EqualTo(h));}
                }
            }));
        }

        static void WithTerrain(Action<PlanetTerrain> action)
        {
            var root=new GameObject("LOD temporal regression");var material=new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                var terrain=root.AddComponent<PlanetTerrain>();terrain.PatchResolution=4;terrain.BuildsPerFrame=32;
                terrain.BuildBudgetMilliseconds=1000;terrain.MaximumVisiblePatches=128;terrain.MaximumResidentPatches=512;
                terrain.MaterialMapResolution=64;terrain.LodTransitionSeconds=.1f;terrain.Configure(Planet,material);action(terrain);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(material);}
        }
        static void Settle(PlanetTerrain terrain,Double3 camera)
        {
            for(int frame=0;frame<500;frame++)
            {terrain.Tick(camera,1,Vector3.zero,Vector3.up,1f/30);if(frame>0&&terrain.IsSettled)return;}
            Assert.Fail("LOD did not converge at a fixed camera.");
        }
        static void SameStates(List<TerrainPatchState> a,List<TerrainPatchState> b)
        {
            Assert.That(b.Count,Is.EqualTo(a.Count));
            for(int i=0;i<a.Count;i++){Assert.That(b[i].Key,Is.EqualTo(a[i].Key));Assert.That(b[i].Morph,Is.EqualTo(a[i].Morph));}
        }
    }
}
