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
        // Exact raw signed height at each geometric vertex. Close-range surface
        // material must agree with the unmodified collision/landing field.
        public Vector2[] SurfaceData;
        public int[] Triangles;

        public static double SurfaceHeight(Double3 direction, PlanetDefinition definition)
        {
            double height = TerrainNoise.Height(direction, definition);
            return definition.HasOcean ? Math.Max(0, height) : height;
        }

        public static Double3 SurfacePoint(Double3 direction, PlanetDefinition definition) => direction * (definition.Radius + SurfaceHeight(direction, definition));

        /// <summary>Fixed physical sample interval keeps normals equal across LOD boundaries.</summary>
        public static Double3 SurfaceNormal(Double3 direction, PlanetDefinition definition, double physicalSampleDistance = .25)
        {
            // A physical interval is essential once the height field includes
            // metre-scale geology. Tying this interval to planet radius erases
            // fine normals on larger planets even when mesh LOD is sufficient.
            double angle = Math.Max(.01, physicalSampleDistance) / definition.Radius;
            Double3 reference = Math.Abs(direction.y) < .95 ? new Double3(0, 1, 0) : new Double3(1, 0, 0);
            Double3 tangent = CubeSphere.Cross(reference, direction).Normalized;
            Double3 bitangent = CubeSphere.Cross(direction, tangent).Normalized;
            Double3 dx = SurfacePoint((direction + tangent * angle).Normalized, definition) - SurfacePoint((direction - tangent * angle).Normalized, definition);
            Double3 dy = SurfacePoint((direction + bitangent * angle).Normalized, definition) - SurfacePoint((direction - bitangent * angle).Normalized, definition);
            return CubeSphere.Cross(dx, dy).Normalized;
        }

        public static TerrainPatchMesh Build(TerrainPatchKey key, PlanetDefinition definition, int resolution, double skirtDepthFactor = .08, double normalSampleDistance = .25, double minimumSkirtDepth = 4)
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
                SurfaceData = new Vector2[vertexCount],
                Triangles = new int[resolution * resolution * 6 + boundaryCount * 12]
            };
            for (int y = 0; y < row; y++)
            for (int x = 0; x < row; x++)
            {
                int i = y * row + x;
                Double3 direction = CubeSphere.Direction(key.Face, u0 + span * x / resolution, v0 + span * y / resolution);
                double rawHeight = TerrainNoise.Height(direction, definition);
                double height = definition.HasOcean ? Math.Max(0, rawHeight) : rawHeight;
                Double3 normal = SurfaceNormal(direction, definition, normalSampleDistance);
                // Do the cancellation in DOUBLE, then submit a small local float to Unity.
                data.Vertices[i] = (direction * (definition.Radius + height) - data.Anchor).ToVector3();
                data.Normals[i] = normal.ToVector3();
                data.Colors[i] = Biome(direction, normal, rawHeight, definition);
                data.UV[i] = new Vector2((float)(u0 + span * x / resolution), (float)(v0 + span * y / resolution));
                data.SurfaceData[i] = new Vector2((float)rawHeight, 0);
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
            double skirtDepth = Math.Max(minimumSkirtDepth, Math.Min(tileWidth * skirtDepthFactor, definition.Radius * .12));
            for (int edge = 0; edge < boundaryCount; edge++)
            {
                int source = boundary[edge];
                int skirt = surfaceVertexCount + edge;
                Double3 absolute = data.Anchor + new Double3(data.Vertices[source].x, data.Vertices[source].y, data.Vertices[source].z);
                data.Vertices[skirt] = (absolute - absolute.Normalized * skirtDepth - data.Anchor).ToVector3();
                data.Normals[skirt] = data.Normals[source]; data.Colors[skirt] = data.Colors[source]; data.UV[skirt] = data.UV[source];
                data.SurfaceData[skirt] = data.SurfaceData[source];
                int next = (edge + 1) % boundaryCount;
                AddTriangle(data, ref triangle, source, surfaceVertexCount + next, skirt);
                AddTriangle(data, ref triangle, source, boundary[next], surfaceVertexCount + next);
                // A taller neighbor is viewed from outside its tile; a lower
                // neighbor is viewed from inside. Both sides must write depth,
                // otherwise single-sided terrain materials reveal black cracks.
                AddTriangle(data, ref triangle, skirt, surfaceVertexCount + next, source);
                AddTriangle(data, ref triangle, surfaceVertexCount + next, boundary[next], source);
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
            {
                Color sea = Color.Lerp(new Color(.012f, .045f, .13f), new Color(.018f, .18f, .30f), Mathf.Clamp01((float)(1 + height / Math.Max(1, planet.TerrainHeight * .22))));
                sea.a = 0;
                return sea;
            }
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
            else
            {
                color = TerrainNoise.SurfaceColor(direction, height, planet);
                float emission = color.a;
                color *= 1 - Mathf.Clamp01(slope * 2.5f) * .18f;
                color.a = emission;
            }
            // Seeded physical material variation follows the same 3D charts as
            // the geometric relief. It adds a sense of scale without a texture
            // or UV seam, while the actual shape remains defined by Height().
            Double3 metres = direction * planet.Radius;
            float stone = (float)TerrainNoise.Noise(metres / 9.0, unchecked(planet.Seed + 32452843));
            float soil = (float)TerrainNoise.Noise(metres / 120.0, unchecked(planet.Seed + 49979687));
            float fissure = planet.Kind == PlanetKind.Lava && !planet.HasOcean ? color.a : 0;
            color *= Mathf.Clamp(.96f + stone * .28f + soil * .15f, .7f, 1.2f);
            color.a = fissure;
            return color;
        }
    }
}
