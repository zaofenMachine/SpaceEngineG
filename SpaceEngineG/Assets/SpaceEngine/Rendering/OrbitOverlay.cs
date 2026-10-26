using System;
using System.Collections.Generic;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>Kepler paths in SI, reprojected from the moving observer, with pixel-width ribbons.</summary>
    public sealed class OrbitOverlay:MonoBehaviour
    {
        public bool ShowOrbits=true;
        public float PixelWidth=1.2f;
        public int SelectedBody;
        public int VisibleSegmentCount {get;private set;}
        public int Segments=160;
        StarSystemDefinition system;Double3[][] paths;
        Mesh mesh;Material material;MeshRenderer view;
        readonly List<Vector3> vertices=new List<Vector3>(),others=new List<Vector3>();
        readonly List<Vector2> sides=new List<Vector2>();readonly List<Color> colors=new List<Color>();readonly List<int> indices=new List<int>();
        public void Configure(StarSystemDefinition value,Material source)
        {
            system=value;paths=new Double3[value.Planets.Length][];
            for(int i=0;i<paths.Length;i++)paths[i]=OrbitPath.Sample(value.Planets[i],value.Planets[i].ParentIndex<0?value.StarMass:value.Planets[value.Planets[i].ParentIndex].Mass,Math.Clamp(Segments,16,2048));
            if(mesh==null)
            {
                mesh=new Mesh{name="Observer-relative physical orbital paths",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
                gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;view=gameObject.AddComponent<MeshRenderer>();
                view.shadowCastingMode=ShadowCastingMode.Off;view.receiveShadows=false;
            }
            if(material!=null)Destroy(material);
            material=source==null?new Material(Shader.Find("SpaceEngine/OrbitOverlay")):new Material(source);view.sharedMaterial=material;
        }
        Double3 Offset(int index,double time)
        {
            Double3 sum=Double3.Zero;
            for(int n=0;n<system.Planets.Length;n++)
            {
                var p=system.Planets[index];sum+=Orbit.Evaluate(p,p.ParentIndex<0?system.StarMass:system.Planets[p.ParentIndex].Mass,time);
                if(p.ParentIndex<0)return sum;index=p.ParentIndex;
            }
            throw new InvalidOperationException("Cyclic orbit");
        }
        public static Vector3 Project(UniversalPosition position,UniversalPosition observer,double scale)
        {
            Double3 p=position.RelativeTo(observer);double distance=p.Length*scale;
            if(distance>8000000)scale*=(8000000+4000000*(1-8000000/distance))/distance;
            return (p*scale).ToVector3();
        }
        public void Tick(UniversalPosition observer,double time,double scale,Camera camera)
        {
            if(view==null)return;
            view.enabled=isActiveAndEnabled&&ShowOrbits&&observer.RelativeTo(system.Position).Length<PhysicalConstants.LightYear*.3;
            VisibleSegmentCount=0;if(!view.enabled)return;
            material.SetFloat("_PixelWidth",Mathf.Clamp(PixelWidth,.5f,4));
            vertices.Clear();others.Clear();sides.Clear();colors.Clear();indices.Clear();
            for(int body=0;body<paths.Length;body++)
            {
                int parent=system.Planets[body].ParentIndex;
                UniversalPosition origin=parent<0?system.Position:system.Position.Add(Offset(parent,time));
                Color tint=body==SelectedBody?new Color(.42f,.95f,1,.9f):parent<0?new Color(.38f,.62f,.76f,.42f):new Color(.96f,.73f,.38f,.6f);
                var path=paths[body];
                for(int j=1;j<path.Length;j++)
                {
                    Vector3 a=Project(origin.Add(path[j-1]),observer,scale),b=Project(origin.Add(path[j]),observer,scale);
                    if(camera.transform.InverseTransformPoint(a).z<camera.nearClipPlane||camera.transform.InverseTransformPoint(b).z<camera.nearClipPlane)continue;
                    int v=vertices.Count;vertices.Add(a);vertices.Add(a);vertices.Add(b);vertices.Add(b);
                    others.Add(b);others.Add(b);others.Add(a);others.Add(a);
                    sides.Add(new Vector2(-1,0));sides.Add(new Vector2(1,0));sides.Add(new Vector2(1,0));sides.Add(new Vector2(-1,0));
                    for(int k=0;k<4;k++)colors.Add(tint);
                    indices.Add(v);indices.Add(v+2);indices.Add(v+1);indices.Add(v+1);indices.Add(v+2);indices.Add(v+3);VisibleSegmentCount++;
                }
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,sides);mesh.SetUVs(1,others);mesh.SetColors(colors);mesh.SetTriangles(indices,0);
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*32000000);
        }
        void OnDisable(){if(view!=null)view.enabled=false;}
        void OnDestroy(){if(mesh!=null)Destroy(mesh);if(material!=null)Destroy(material);}
    }
}
