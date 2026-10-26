using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>Finite 3D population kernels. Static galactic coordinates are projected on the GPU for each observer.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GalaxyField : MonoBehaviour
    {
        public GalaxyDefinition Definition { get; private set; }
        public int ParticleCount { get; private set; }
        public int GeneratedSeed {get;private set;}
        public float Exposure = 1;
        public float KernelScale = 1;
        public float DustStrength = 1;
        [Min(1)] public float DustScaleHeightLy = 220;
        [Min(1)] public float DustAttenuationLengthLy = 1200;
        public float LocalFadeLy = 100;
        public bool ShowField = true;
        Material material;
        Mesh mesh;
        MeshRenderer rendererComponent;

        public void Configure(GalaxyDefinition definition,int count,Material source)
        {
            if(definition==null)throw new System.ArgumentNullException(nameof(definition));
            if(source==null)throw new System.ArgumentNullException(nameof(source));
            Release();Definition=definition;GeneratedSeed=definition.Seed;ParticleCount=Mathf.Clamp(count,1000,200000);
            material=new Material(source){name="Galaxy field instance"};
            GalaxyParticle[] particles=GalaxyModel.GenerateRepresentatives(definition,ParticleCount);
            var vertices=new Vector3[ParticleCount*4];var colors=new Color[vertices.Length];
            var uv=new Vector2[vertices.Length];var info=new Vector4[vertices.Length];var indices=new int[ParticleCount*6];
            for(int i=0;i<ParticleCount;i++)
            {
                var p=particles[i];int v=i*4,t=i*6;
                for(int corner=0;corner<4;corner++)
                {
                    vertices[v+corner]=p.PositionLy.ToVector3();colors[v+corner]=p.Color;
                    info[v+corner]=new Vector4(p.SizeLy,p.Luminosity,(float)p.Component,i%997/997f);
                }
                uv[v]=new Vector2(-1,-1);uv[v+1]=new Vector2(1,-1);uv[v+2]=new Vector2(1,1);uv[v+3]=new Vector2(-1,1);
                indices[t]=v;indices[t+1]=v+2;indices[t+2]=v+1;indices[t+3]=v;indices[t+4]=v+3;indices[t+5]=v+2;
            }
            mesh=new Mesh{name="Galaxy spatial population kernels",indexFormat=IndexFormat.UInt32};
            mesh.vertices=vertices;mesh.colors=colors;mesh.uv=uv;mesh.SetUVs(1,info);mesh.triangles=indices;
            // Vertices are galactic ly, while the shader emits camera-relative angular kernels.
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*32000000);
            mesh.UploadMeshData(true);
            var filter=GetComponent<MeshFilter>();if(filter==null)filter=gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            rendererComponent=GetComponent<MeshRenderer>();if(rendererComponent==null)rendererComponent=gameObject.AddComponent<MeshRenderer>();
            rendererComponent.sharedMaterial=material;rendererComponent.shadowCastingMode=ShadowCastingMode.Off;rendererComponent.receiveShadows=false;
        }

        public void Tick(UniversalPosition observer)
        {
            if(material==null||Definition==null)return;
            rendererComponent.enabled=ShowField&&isActiveAndEnabled;
            material.SetVector("_ObserverLy",GalaxyModel.ToGalacticLy(observer,Definition).ToVector3());
            material.SetFloat("_Exposure",Exposure);material.SetFloat("_KernelScale",KernelScale);
            material.SetFloat("_DustStrength",DustStrength);material.SetFloat("_LocalFadeLy",LocalFadeLy);
            material.SetFloat("_DustScaleHeightLy",Mathf.Max(1,DustScaleHeightLy));
            material.SetFloat("_DustAttenuationLengthLy",Mathf.Max(1,DustAttenuationLengthLy));
            material.SetFloat("_DensityCompensation",60000f/ParticleCount);
            material.SetFloat("_GalaxyRadiusLy",(float)Definition.RadiusLy);
            material.SetFloat("_ArmPitch",(float)Definition.ArmPitchRadians);material.SetFloat("_ArmCount",Definition.ArmCount);
            material.SetFloat("_ArmPhase",(float)GalaxyModel.ArmAngle(10000,Definition));
        }
        void Release()
        {
            if(rendererComponent!=null)rendererComponent.enabled=false;
            if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
            if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
            mesh=null;material=null;
        }
        void OnDisable(){if(rendererComponent!=null)rendererComponent.enabled=false;}
        void OnDestroy(){Release();}
    }
}
