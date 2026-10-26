using System;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Terrain
{
    /// <summary>A CPU mesh payload with an astronomical double precision origin.</summary>
    public sealed class TerrainPatchMesh
    {
        public Double3 Anchor;
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Color[] Colors;
        public Vector2[] UV;
        public int[] Triangles;

        public static double SurfaceHeight(Double3 direction, PlanetDefinition definition)
        {
            double height = TerrainNoise.Height(direction, definition);
            return definition.HasOcean ? Math.Max(0, height) : height;
        }

        public static Double3 SurfacePoint(Double3 direction, PlanetDefinition definition) => direction * (definition.Radius + SurfaceHeight(direction, definition));

        /// <summary>Fixed physical sample interval keeps normals equal across LOD boundaries.</summary>
        public static Double3 SurfaceNormal(Double3 direction, PlanetDefinition definition)
        {
            double angle = Math.Max(.5, definition.Radius * 2e-7) / definition.Radius;
            Double3 reference = Math.Abs(direction.y) < .95 ? new Double3(0, 1, 0) : new Double3(1, 0, 0);
            Double3 tangent = CubeSphere.Cross(reference, direction).Normalized;
            Double3 bitangent = CubeSphere.Cross(direction, tangent).Normalized;
            Double3 dx = SurfacePoint((direction + tangent * angle).Normalized, definition) - SurfacePoint((direction - tangent * angle).Normalized, definition);
            Double3 dy = SurfacePoint((direction + bitangent * angle).Normalized, definition) - SurfacePoint((direction - bitangent * angle).Normalized, definition);
            return CubeSphere.Cross(dx, dy).Normalized;
        }

        public static TerrainPatchMesh Build(TerrainPatchKey key, PlanetDefinition definition, int resolution, double skirtDepthFactor = .035)
        {
            resolution = Math.Max(4, Math.Min(32, resolution));
            CubeSphere.Bounds(key, out double u0, out double v0, out double span);
            int row = resolution + 1;
            int surfaceVertexCount = row * row;
            int boundaryCount = resolution * 4;
            int vertexCount = surfaceVertexCount + boundaryCount;
            var data = new TerrainPatchMesh
            {
                Anchor = SurfacePoint(CubeSphere.CenterDirection(key), definition),
                Vertices = new Vector3[vertexCount], Normals = new Vector3[vertexCount],
                Colors = new Color[vertexCount], UV = new Vector2[vertexCount],
                Triangles = new int[resolution * resolution * 6 + boundaryCount * 6]
            };
            for (int y = 0; y < row; y++)
            for (int x = 0; x < row; x++)
            {
                int i = y * row + x;
                Double3 direction = CubeSphere.Direction(key.Face, u0 + span * x / resolution, v0 + span * y / resolution);
                double rawHeight = TerrainNoise.Height(direction, definition);
                double height = definition.HasOcean ? Math.Max(0, rawHeight) : rawHeight;
                Double3 normal = SurfaceNormal(direction, definition);
                // Do the cancellation in DOUBLE, then submit a small local float to Unity.
                data.Vertices[i] = (direction * (definition.Radius + height) - data.Anchor).ToVector3();
                data.Normals[i] = normal.ToVector3();
                data.Colors[i] = Biome(direction, normal, rawHeight, definition);
                data.UV[i] = new Vector2((float)(u0 + span * x / resolution), (float)(v0 + span * y / resolution));
            }
            int triangle = 0;
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int i = y * row + x;
                AddTriangle(data, ref triangle, i, i + 1, i + row);
                AddTriangle(data, ref triangle, i + 1, i + row + 1, i + row);
            }
            // Counterclockwise perimeter viewed from outside. Independent lower ring
            // closes T junctions against a coarser neighbor, including cube edges.
            var boundary = new int[boundaryCount];
            int k = 0;
            for (int x = 0; x < resolution; x++) boundary[k++] = x;
            for (int y = 0; y < resolution; y++) boundary[k++] = y * row + resolution;
            for (int x = resolution; x > 0; x--) boundary[k++] = resolution * row + x;
            for (int y = resolution; y > 0; y--) boundary[k++] = y * row;
            double tileWidth = definition.Radius * span;
            double skirtDepth = Math.Max(.25, Math.Min(tileWidth * skirtDepthFactor, definition.Radius * .12));
            for (int edge = 0; edge < boundaryCount; edge++)
            {
                int source = boundary[edge];
                int skirt = surfaceVertexCount + edge;
                Double3 absolute = data.Anchor + new Double3(data.Vertices[source].x, data.Vertices[source].y, data.Vertices[source].z);
                data.Vertices[skirt] = (absolute - absolute.Normalized * skirtDepth - data.Anchor).ToVector3();
                data.Normals[skirt] = data.Normals[source]; data.Colors[skirt] = data.Colors[source]; data.UV[skirt] = data.UV[source];
                int next = (edge + 1) % boundaryCount;
                AddTriangle(data, ref triangle, source, surfaceVertexCount + next, skirt);
                AddTriangle(data, ref triangle, source, boundary[next], surfaceVertexCount + next);
            }
            return data;
        }

        static void AddTriangle(TerrainPatchMesh data, ref int cursor, int a, int b, int c)
        {
            data.Triangles[cursor++] = a; data.Triangles[cursor++] = b; data.Triangles[cursor++] = c;
        }

        static Color Biome(Double3 direction, Double3 normal, double height, PlanetDefinition planet)
        {
            if (planet.HasOcean && height < 0)
                return Color.Lerp(new Color(.012f, .045f, .13f), new Color(.018f, .18f, .30f), Mathf.Clamp01((float)(1 + height / Math.Max(1, planet.TerrainHeight * .22))));
            float normalizedHeight = (float)(height / Math.Max(1, planet.TerrainHeight));
            float slope = (float)Math.Max(0, 1 - CubeSphere.Dot(direction, normal));
            Color rock = Color.Lerp(new Color(.23f, .20f, .17f), planet.SurfaceTint, .45f);
            Color color;
            if (planet.HasOcean)
            {
                float latitude = (float)Math.Abs(direction.y);
                Color vegetation = Color.Lerp(new Color(.09f, .22f, .08f), new Color(.31f, .29f, .12f), Mathf.Clamp01((latitude - .15f) * 1.7f));
                color = Color.Lerp(new Color(.48f, .40f, .24f), vegetation, Mathf.Clamp01(normalizedHeight * 35));
                color = Color.Lerp(color, rock, Mathf.Clamp01(normalizedHeight * 1.6f + slope * 4));
                float snow = Mathf.Clamp01((normalizedHeight - .55f) * 4 + (latitude - .72f) * 5);
                color = Color.Lerp(color, new Color(.88f, .92f, .96f), snow);
            }
            else color = Color.Lerp(planet.SurfaceTint * .65f, planet.SurfaceTint * 1.3f, Mathf.Clamp01(normalizedHeight * .6f + .5f));
            return color;
        }
    }
}
