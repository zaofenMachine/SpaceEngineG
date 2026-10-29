using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// A clip-space quad for sky layers. Its shaders place it at the far plane, so every
    /// opaque body nearer than the far plane occludes the layer through the depth test.
    /// </summary>
    public static class FarPlaneQuad
    {
        public static MeshRenderer Attach(GameObject owner, Material material, out Mesh mesh)
        {
            mesh = new Mesh { name = owner.name + " far-depth quad" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // Vertices are emitted in clip space; huge bounds keep culling from removing them.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 32000000);
            owner.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = owner.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }
    }
}
