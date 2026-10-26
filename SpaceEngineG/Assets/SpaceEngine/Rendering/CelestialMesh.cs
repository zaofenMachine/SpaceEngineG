using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>Generated unit cloud-reference surfaces and equatorial annuli. No imported sphere assets.</summary>
    public static class CelestialMesh
    {
        public static Mesh Sphere(int longitudeSegments, int latitudeSegments)
        {
            if (longitudeSegments < 8 || latitudeSegments < 4)
                throw new ArgumentOutOfRangeException(nameof(longitudeSegments));
            int count = 2 + longitudeSegments * (latitudeSegments - 1);
            var vertices = new Vector3[count];
            var indices = new int[6 * longitudeSegments * (latitudeSegments - 1)];
            vertices[0] = Vector3.up;
            vertices[count - 1] = Vector3.down;
            for (int latitude = 1; latitude < latitudeSegments; latitude++)
            {
                double theta = Math.PI * latitude / latitudeSegments;
                for (int longitude = 0; longitude < longitudeSegments; longitude++)
                {
                    double phi = 2 * Math.PI * longitude / longitudeSegments;
                    vertices[1 + (latitude - 1) * longitudeSegments + longitude] = new Vector3(
                        (float)(Math.Sin(theta) * Math.Cos(phi)), (float)Math.Cos(theta),
                        (float)(Math.Sin(theta) * Math.Sin(phi)));
                }
            }
            int cursor = 0;
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
            {
                int next = (longitude + 1) % longitudeSegments;
                indices[cursor++] = 0;
                indices[cursor++] = 1 + next;
                indices[cursor++] = 1 + longitude;
                for (int latitude = 0; latitude < latitudeSegments - 2; latitude++)
                {
                    int a = 1 + latitude * longitudeSegments + longitude;
                    int b = 1 + latitude * longitudeSegments + next;
                    int c = a + longitudeSegments, d = b + longitudeSegments;
                    indices[cursor++] = a; indices[cursor++] = b; indices[cursor++] = c;
                    indices[cursor++] = b; indices[cursor++] = d; indices[cursor++] = c;
                }
                int last = 1 + (latitudeSegments - 2) * longitudeSegments;
                indices[cursor++] = last + longitude;
                indices[cursor++] = last + next;
                indices[cursor++] = count - 1;
            }
            return Build("Generated cloud reference sphere", vertices, vertices, indices);
        }

        /// <summary>Ring mesh in XZ with dimensionless radii relative to the cloud/surface reference radius.</summary>
        public static Mesh Ring(float innerRatio, float outerRatio, int angularSegments, int radialSegments)
        {
            if (!(innerRatio > 1) || !(outerRatio > innerRatio))
                throw new ArgumentOutOfRangeException(nameof(innerRatio));
            if (angularSegments < 8 || radialSegments < 1)
                throw new ArgumentOutOfRangeException(nameof(angularSegments));
            var vertices = new Vector3[angularSegments * (radialSegments + 1)];
            var normals = new Vector3[vertices.Length];
            var indices = new int[angularSegments * radialSegments * 6];
            for (int radial = 0; radial <= radialSegments; radial++)
            for (int angle = 0; angle < angularSegments; angle++)
            {
                float radius = Mathf.Lerp(innerRatio, outerRatio, radial / (float)radialSegments);
                double theta = 2 * Math.PI * angle / angularSegments;
                int index = radial * angularSegments + angle;
                vertices[index] = new Vector3((float)Math.Cos(theta) * radius, 0, (float)Math.Sin(theta) * radius);
                normals[index] = Vector3.up;
            }
            int cursor = 0;
            for (int radial = 0; radial < radialSegments; radial++)
            for (int angle = 0; angle < angularSegments; angle++)
            {
                int a = radial * angularSegments + angle;
                int b = radial * angularSegments + (angle + 1) % angularSegments;
                int c = a + angularSegments, d = b + angularSegments;
                indices[cursor++] = a; indices[cursor++] = b; indices[cursor++] = c;
                indices[cursor++] = b; indices[cursor++] = d; indices[cursor++] = c;
            }
            return Build("Generated equatorial particle annulus", vertices, normals, indices);
        }

        static Mesh Build(string name, Vector3[] vertices, Vector3[] normals, int[] indices)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Length > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
