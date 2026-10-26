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
        // xyz is the displacement from this vertex to the actual parent
        // triangle; w is that triangle's interpolated signed height.
        public Vector4[] ParentData;
        public Vector3[] ParentNormals;
        public Vector4[] ParentColors;
        public int Resolution;
        public Bounds MorphBounds;
        public int[] Triangles;

        public static int CompatibleResolution(int resolution) => Math.Max(4, Math.Min(32, (resolution + 1) & ~1));

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
            // An even grid preserves every parent grid line/diagonal when it
            // is subdivided into four children. Odd grids cannot reproduce the
            // parent triangulation exactly at the morph endpoint.
            resolution = CompatibleResolution(resolution);
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
                ParentData = new Vector4[vertexCount], ParentNormals = new Vector3[vertexCount],
                ParentColors = new Vector4[vertexCount], Resolution = resolution,
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
            for (int i = 0; i < vertexCount; i++)
            {
                data.ParentData[i] = new Vector4(0, 0, 0, data.SurfaceData[i].x);
                data.ParentNormals[i] = data.Normals[i];
                data.ParentColors[i] = data.Colors[i];
            }
            data.RecalculateMorphBounds();
            return data;
        }

        /// <summary>
        /// Uses the parent's submitted float vertices, rather than re-sampling
        /// Height or using bilinear interpolation. At t=0 the child's surface
        /// is a subdivision of exactly the same two parent triangles.
        /// </summary>
        public void SetParent(TerrainPatchKey childKey, TerrainPatchMesh parent)
        {
            if (parent == null || childKey.Level == 0) return;
            if (parent.Resolution != Resolution) throw new ArgumentException("Parent and child grid resolutions must agree.");
            int row = Resolution + 1, surfaceCount = row * row;
            for (int y = 0; y <= Resolution; y++)
            for (int x = 0; x <= Resolution; x++)
            {
                int i = y * row + x;
                double px = ((childKey.X & 1) * Resolution + x) * .5;
                double py = ((childKey.Y & 1) * Resolution + y) * .5;
                parent.Sample(px, py, 1, out Double3 position, out Vector3 normal, out Vector4 color, out float height);
                SetParentVertex(i, position, normal, color, height);
            }
            // Internal child edges begin collapsed into the parent surface.
            // Outer edges follow the parent's actual lower skirt ring.
            int[] boundary = Boundary(Resolution);
            for (int edge = 0; edge < boundary.Length; edge++)
            {
                int source = boundary[edge], x = source % row, y = source / row;
                double px = ((childKey.X & 1) * Resolution + x) * .5;
                double py = ((childKey.Y & 1) * Resolution + y) * .5;
                Double3 position = Anchor + Double3.FromVector3(Vertices[source]) + Double3.FromVector3((Vector3)ParentData[source]);
                if (px == 0 || py == 0 || px == Resolution || py == Resolution)
                    position = parent.SampleSkirt(px, py);
                SetParentVertex(surfaceCount + edge, position, ParentNormals[source], ParentColors[source], ParentData[source].w);
            }
            RecalculateMorphBounds();
        }

        void SetParentVertex(int index, Double3 absolute, Vector3 normal, Vector4 color, float height)
        {
            Vector3 local = (absolute - Anchor).ToVector3();
            Vector3 delta = local - Vertices[index];
            ParentData[index] = new Vector4(delta.x, delta.y, delta.z, height);
            ParentNormals[index] = normal; ParentColors[index] = color;
        }

        void RecalculateMorphBounds()
        {
            Vector3 minimum=Vertices[0],maximum=Vertices[0];
            for(int i=0;i<Vertices.Length;i++)
            {
                Vector3 endpoint=Vertices[i]+(Vector3)ParentData[i];
                minimum=Vector3.Min(minimum,Vector3.Min(Vertices[i],endpoint));
                maximum=Vector3.Max(maximum,Vector3.Max(Vertices[i],endpoint));
            }
            // Bounds stores float centre/extents. Repeated Encapsulate can
            // round centre +/- extents inward and exclude an earlier vertex.
            // Compute extrema once and pad the culling box by a few float ULPs;
            // this does not alter a geometric vertex or morph tolerance.
            Vector3 magnitude=Vector3.Max(new Vector3(Mathf.Abs(minimum.x),Mathf.Abs(minimum.y),Mathf.Abs(minimum.z)),
                new Vector3(Mathf.Abs(maximum.x),Mathf.Abs(maximum.y),Mathf.Abs(maximum.z)));
            Vector3 padding=new Vector3(Mathf.Max(.00001f,magnitude.x*1e-6f),Mathf.Max(.00001f,magnitude.y*1e-6f),Mathf.Max(.00001f,magnitude.z*1e-6f));
            MorphBounds=new Bounds((minimum+maximum)*.5f,maximum-minimum+padding*2);
        }

        public void Sample(double x, double y, float morph, out Double3 position, out Vector3 normal, out Vector4 color, out float height)
        {
            int ix = Math.Max(0, Math.Min(Resolution - 1, (int)Math.Floor(x)));
            int iy = Math.Max(0, Math.Min(Resolution - 1, (int)Math.Floor(y)));
            float a = (float)Math.Max(0, Math.Min(1, x - ix)), b = (float)Math.Max(0, Math.Min(1, y - iy));
            int row = Resolution + 1, v = iy * row + ix;
            int i0, i1, i2; float w0, w1, w2;
            if (a + b <= 1) { i0 = v; i1 = v + 1; i2 = v + row; w0 = 1 - a - b; w1 = a; w2 = b; }
            else { i0 = v + row + 1; i1 = v + row; i2 = v + 1; w0 = a + b - 1; w1 = 1 - a; w2 = 1 - b; }
            float coarse = 1 - Mathf.Clamp01(morph);
            Vector3 p0 = Vertices[i0] + (Vector3)ParentData[i0] * coarse;
            Vector3 p1 = Vertices[i1] + (Vector3)ParentData[i1] * coarse;
            Vector3 p2 = Vertices[i2] + (Vector3)ParentData[i2] * coarse;
            // Sum in double so tests/readbacks retain the same small chart precision.
            position = Anchor + Double3.FromVector3(p0) * w0 + Double3.FromVector3(p1) * w1 + Double3.FromVector3(p2) * w2;
            normal = Vector3.LerpUnclamped(Normals[i0], ParentNormals[i0], coarse) * w0
                + Vector3.LerpUnclamped(Normals[i1], ParentNormals[i1], coarse) * w1
                + Vector3.LerpUnclamped(Normals[i2], ParentNormals[i2], coarse) * w2;
            color = Vector4.LerpUnclamped(Colors[i0], ParentColors[i0], coarse) * w0
                + Vector4.LerpUnclamped(Colors[i1], ParentColors[i1], coarse) * w1
                + Vector4.LerpUnclamped(Colors[i2], ParentColors[i2], coarse) * w2;
            height = Mathf.LerpUnclamped(SurfaceData[i0].x, ParentData[i0].w, coarse) * w0
                + Mathf.LerpUnclamped(SurfaceData[i1].x, ParentData[i1].w, coarse) * w1
                + Mathf.LerpUnclamped(SurfaceData[i2].x, ParentData[i2].w, coarse) * w2;
        }

        Double3 SampleSkirt(double x, double y)
        {
            double perimeter = y == 0 ? x : x == Resolution ? Resolution + y
                : y == Resolution ? 3 * Resolution - x : 4 * Resolution - y;
            int lower = (int)Math.Floor(perimeter), length = 4 * Resolution, start = (Resolution + 1) * (Resolution + 1);
            double fraction = perimeter - lower;
            return Anchor + Double3.FromVector3(Vertices[start + lower % length]) * (1 - fraction)
                + Double3.FromVector3(Vertices[start + (lower + 1) % length]) * fraction;
        }

        static int[] Boundary(int resolution)
        {
            int row = resolution + 1, k = 0; var result = new int[resolution * 4];
            for (int x = 0; x < resolution; x++) result[k++] = x;
            for (int y = 0; y < resolution; y++) result[k++] = y * row + resolution;
            for (int x = resolution; x > 0; x--) result[k++] = resolution * row + x;
            for (int y = resolution; y > 0; y--) result[k++] = y * row;
            return result;
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
