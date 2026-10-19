using System.Numerics;
using System.Runtime.InteropServices;

namespace Murmuration;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Color;

    public Vertex(Vector3 position, Vector3 color)
    {
        Position = position;
        Color = color;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Up;
}

public static class Geometry
{
    public const float GroundHalfSize = 50f;

    private static readonly Vector3 WingColor = new(0.92f, 0.92f, 0.92f);
    private static readonly Vector3 HeadColor = new(0.95f, 0.55f, 0.1f);
    private static readonly Vector3 TailColor = new(0.55f, 0.55f, 0.6f);
    private static readonly Vector3 GroundColor = new(0.15f, 0.55f, 0.2f);

    // Bird local axes: x forward, y left, z up; units are meters.
    public static Vertex[] CreateBird()
    {
        var v = new List<Vertex>();

        const float wingHalfWidth = 0.025f;
        const float wingHalfHeight = 0.005f;
        const float wingLength = 0.30f;
        foreach (float side in new[] { 1f, -1f })
        {
            AddTetrahedron(v,
                new Vector3(-wingHalfWidth, 0, -wingHalfHeight),
                new Vector3(wingHalfWidth, 0, -wingHalfHeight),
                new Vector3(0, 0, wingHalfHeight),
                new Vector3(0, side * wingLength, 0),
                WingColor);
        }

        AddTetrahedron(v,
            new Vector3(0, -0.015f, -0.01f),
            new Vector3(0, 0.015f, -0.01f),
            new Vector3(0, 0, 0.01f),
            new Vector3(0.10f, 0, 0),
            HeadColor);

        AddTetrahedron(v,
            new Vector3(-0.10f, -0.025f, -0.0025f),
            new Vector3(-0.10f, 0.025f, -0.0025f),
            new Vector3(-0.10f, 0, 0.0025f),
            Vector3.Zero,
            TailColor);

        return v.ToArray();
    }

    // World-space square at Z=0.
    public static Vertex[] CreateGround()
    {
        float h = GroundHalfSize;
        var a = new Vertex(new Vector3(-h, -h, 0), GroundColor);
        var b = new Vertex(new Vector3(h, -h, 0), GroundColor);
        var c = new Vertex(new Vector3(h, h, 0), GroundColor);
        var d = new Vertex(new Vector3(-h, h, 0), GroundColor);
        return [a, b, c, a, c, d];
    }

    private static void AddTetrahedron(List<Vertex> v, Vector3 a, Vector3 b, Vector3 c, Vector3 apex, Vector3 color)
    {
        AddTriangle(v, a, b, c, color);
        AddTriangle(v, a, b, apex, color);
        AddTriangle(v, b, c, apex, color);
        AddTriangle(v, c, a, apex, color);
    }

    private static void AddTriangle(List<Vertex> v, Vector3 a, Vector3 b, Vector3 c, Vector3 color)
    {
        v.Add(new Vertex(a, color));
        v.Add(new Vertex(b, color));
        v.Add(new Vertex(c, color));
    }
}
