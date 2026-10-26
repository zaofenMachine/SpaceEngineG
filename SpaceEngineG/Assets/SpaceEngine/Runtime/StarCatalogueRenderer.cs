using System;
using System.Collections.Generic;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Runtime
{
    /// <summary>Point source proxies are projected from persistent 3D star positions, never a sky texture.</summary>
    public sealed class StarCatalogueRenderer : MonoBehaviour
    {
        public struct Entry { public long X,Y,Z; public StarSystemDefinition System; }
        public readonly List<Entry> Entries = new List<Entry>();
        Mesh mesh;
        MeshRenderer meshRenderer;
        Vector3[] vertices;
        Color[] colors;
        Vector2[] uv;
        int[] triangles;
        public int Count => Entries.Count;
        public long CenterX {get;private set;}public long CenterY {get;private set;}public long CenterZ {get;private set;}

        public void Generate(int seed, long cx, long cy, long cz, int radius, Material material,GalaxyDefinition galaxy=null)
        {
            CenterX=cx;CenterY=cy;CenterZ=cz;
            galaxy=galaxy??GalaxyModel.Default(seed);
            Entries.Clear();
            for (int x=-radius; x<=radius; x++)
            for (int y=-radius; y<=radius; y++)
            for (int z=-radius; z<=radius; z++)
                if(UniverseGenerator.IsCellOccupied(seed,cx+x,cy+y,cz+z,galaxy))
                    Entries.Add(new Entry { X=cx+x, Y=cy+y, Z=cz+z, System=UniverseGenerator.GenerateSystem(seed,cx+x,cy+y,cz+z,galaxy) });
            if (mesh != null) Destroy(mesh);
            mesh = new Mesh { name="Seeded spatial star catalogue", indexFormat=IndexFormat.UInt32 };
            mesh.MarkDynamic();
            int count=Entries.Count;
            vertices=new Vector3[count*4]; colors=new Color[count*4]; uv=new Vector2[count*4]; triangles=new int[count*6];
            for (int i=0;i<count;i++)
            {
                int v=i*4,t=i*6;
                uv[v]=new Vector2(0,0); uv[v+1]=new Vector2(1,0); uv[v+2]=new Vector2(1,1); uv[v+3]=new Vector2(0,1);
                triangles[t]=v; triangles[t+1]=v+2; triangles[t+2]=v+1;
                triangles[t+3]=v; triangles[t+4]=v+3; triangles[t+5]=v+2;
            }
            mesh.vertices=vertices; mesh.uv=uv; mesh.colors=colors; mesh.triangles=triangles;
            var filter=GetComponent<MeshFilter>();if(filter==null)filter=gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            meshRenderer=GetComponent<MeshRenderer>();if(meshRenderer==null)meshRenderer=gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial=material; meshRenderer.shadowCastingMode=ShadowCastingMode.Off; meshRenderer.receiveShadows=false;
        }

        public void Tick(UniversalPosition observer, Camera camera, float brightness,float minimumAngularRadius=.00065f)
        {
            if(mesh==null) return;
            const float shell=15000000;
            Vector3 right=camera.transform.right, up=camera.transform.up;
            for(int i=0;i<Entries.Count;i++)
            {
                var star=Entries[i].System;
                Double3 delta=star.Position.RelativeTo(observer);
                double distance=delta.Length;
                Vector3 center=delta.Normalized.ToVector3()*shell;
                double ly=distance/PhysicalConstants.LightYear;
                // Brightness uses a compressed exposure so the limited streamed catalogue stays navigable.
                float intensity=brightness*(float)Math.Clamp(5.5/Math.Max(.5,ly),.12,2.5);
                float angularRadius=(float)Math.Clamp(star.StarRadius/Math.Max(1,distance),minimumAngularRadius,.06);
                float size=shell*angularRadius*1.8f;
                Color color=TemperatureColor(star.StarTemperature)*intensity;
                if(star.Kind==StellarKind.BlackHole)color=Color.black;
                color.a=distance<star.StarRadius*3 ? 0:1;
                int v=i*4;
                vertices[v]=center-right*size-up*size; vertices[v+1]=center+right*size-up*size;
                vertices[v+2]=center+right*size+up*size; vertices[v+3]=center-right*size+up*size;
                colors[v]=colors[v+1]=colors[v+2]=colors[v+3]=color;
            }
            mesh.vertices=vertices; mesh.colors=colors;
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*shell*3);
        }

        public static Color TemperatureColor(double kelvin)
        {
            float t=(float)Math.Clamp((kelvin-3000)/7000,0,1);
            return Color.Lerp(new Color(1,.57f,.28f),new Color(.58f,.76f,1),t);
        }
        void OnDestroy(){if(mesh!=null)Destroy(mesh);}
    }
}
