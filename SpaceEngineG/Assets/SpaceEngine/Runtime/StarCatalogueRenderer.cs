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
        Vector2[] shape;
        int[] triangles;
        /// <summary>Daylight and similar adaptation dim the point sources without changing the catalogue.</summary>
        public float Visibility = 1;
        const double NominalSolarLuminosity=3.828e26;
        public int Count => Entries.Count;
        public long CenterX {get;private set;}public long CenterY {get;private set;}public long CenterZ {get;private set;}

        public void Generate(int seed, long cx, long cy, long cz, int radius, Material material,GalaxyDefinition galaxy=null,int generationVersion=UniverseGenerator.GenerationVersion)
        {
            CenterX=cx;CenterY=cy;CenterZ=cz;
            galaxy=galaxy??GalaxyModel.Default(seed);
            Entries.Clear();
            for (int x=-radius; x<=radius; x++)
            for (int y=-radius; y<=radius; y++)
            for (int z=-radius; z<=radius; z++)
                if(UniverseGenerator.IsCellOccupied(seed,cx+x,cy+y,cz+z,galaxy))
                    Entries.Add(new Entry { X=cx+x, Y=cy+y, Z=cz+z, System=UniverseGenerator.GenerateSystem(seed,cx+x,cy+y,cz+z,galaxy,generationVersion) });
            if (mesh != null) Destroy(mesh);
            mesh = new Mesh { name="Seeded spatial star catalogue", indexFormat=IndexFormat.UInt32 };
            mesh.MarkDynamic();
            int count=Entries.Count;
            vertices=new Vector3[count*4]; colors=new Color[count*4]; uv=new Vector2[count*4]; shape=new Vector2[count*4]; triangles=new int[count*6];
            for (int i=0;i<count;i++)
            {
                int v=i*4,t=i*6;
                uv[v]=new Vector2(0,0); uv[v+1]=new Vector2(1,0); uv[v+2]=new Vector2(1,1); uv[v+3]=new Vector2(0,1);
                triangles[t]=v; triangles[t+1]=v+2; triangles[t+2]=v+1;
                triangles[t+3]=v; triangles[t+4]=v+3; triangles[t+5]=v+2;
            }
            mesh.vertices=vertices; mesh.uv=uv; mesh.SetUVs(1,shape); mesh.colors=colors; mesh.triangles=triangles;
            var filter=GetComponent<MeshFilter>();if(filter==null)filter=gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            meshRenderer=GetComponent<MeshRenderer>();if(meshRenderer==null)meshRenderer=gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial=material; meshRenderer.shadowCastingMode=ShadowCastingMode.Off; meshRenderer.receiveShadows=false;
        }

        public void Tick(UniversalPosition observer, Camera camera, float brightness,float minimumAngularRadius=.00065f)
        {
            if(mesh==null) return;
            const float shell=15000000;
            Vector3 right=camera.transform.right, up=camera.transform.up;
            float pixel=2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)/Mathf.Max(1,camera.pixelHeight);
            for(int i=0;i<Entries.Count;i++)
            {
                var star=Entries[i].System;
                Double3 delta=star.Position.RelativeTo(observer);
                double distance=delta.Length;
                Vector3 center=delta.Normalized.ToVector3()*shell;
                double ly=Math.Max(.05,distance/PhysicalConstants.LightYear);
                // Apparent flux relative to a Sun-like star at five light years, compressed for display:
                // a factor of 100 in flux spans roughly a factor of 8 in drawn brightness.
                double flux=star.StarLuminosity/NominalSolarLuminosity/(ly*ly);
                float intensity=brightness*Visibility*(float)Math.Clamp(Math.Pow(Math.Max(0,flux)/.04,.45),0,14);
                float halfPixels=Mathf.Min(14,3+3.5f*Mathf.Sqrt(Mathf.Min(intensity,9)));
                float size=Mathf.Max(shell*pixel*halfPixels,shell*minimumAngularRadius);
                Color color=TemperatureColor(star.StarTemperature).linear;
                if(star.Kind==StellarKind.BlackHole||intensity<.004f)color=Color.black;
                color.a=distance<star.StarRadius*3 ? 0:1;
                int v=i*4;
                vertices[v]=center-right*size-up*size; vertices[v+1]=center+right*size-up*size;
                vertices[v+2]=center+right*size+up*size; vertices[v+3]=center-right*size+up*size;
                colors[v]=colors[v+1]=colors[v+2]=colors[v+3]=color;
                shape[v]=shape[v+1]=shape[v+2]=shape[v+3]=new Vector2(size/(shell*pixel),intensity);
            }
            mesh.vertices=vertices; mesh.colors=colors; mesh.SetUVs(1,shape);
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*shell*3);
        }

        /// <summary>Blackbody chromaticity (fit to the Planck locus, 1000-40000 K) with its brightest channel at one.</summary>
        public static Color TemperatureColor(double kelvin)
        {
            double t=Math.Clamp(kelvin,1000,40000)/100;
            double r=t<=66?1:Math.Clamp(1.29293618*Math.Pow(t-60,-.1332047592),0,1);
            double g=t<=66?Math.Clamp(.39008157*Math.Log(t)-.63184144,0,1):Math.Clamp(1.12989086*Math.Pow(t-60,-.0755148492),0,1);
            double b=t>=66?1:t<=19?0:Math.Clamp(.54320679*Math.Log(t-10)-1.19625409,0,1);
            // sRGB-encoded like other material colours; vertex-colour callers convert with .linear.
            return new Color((float)r,(float)g,(float)b);
        }
        void OnDestroy(){if(mesh!=null)Destroy(mesh);}
    }
}
