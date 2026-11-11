using System.Numerics;
using System.Runtime.InteropServices;

namespace Murmuration;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Color;
    public float WingSide; // +1 left wing, -1 right wing, 0 rigid

    public Vertex(Vector3 position, Vector3 color, float wingSide = 0f)
    {
        Position = position;
        Color = color;
        WingSide = wingSide;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Up;
    public float FlapAngle; // radians, positive raises wing tips
}

/// <summary>
/// Provides geometry data for birds and the ground.
/// </summary>
/// <remarks>
/// This code is shared by the CPU and GPU paths.
/// </remarks>
public static class Geometry
{
    public const float GroundHalfSize = 50f;

    private static readonly Vector3 Blue = new(0.2f, 0.4f, 0.95f);
    private static readonly Vector3 Green = new(0.2f, 0.5f, 0.2f);
    private static readonly Vector3 BrightBlue = new(0f, 0.3f, 1f);
    private static readonly Vector3 Red = new(1f, 0.1f, 0.1f);
    private static readonly Vector3 White = new(1f, 1f, 1f);
    private static readonly Vector3 MediumGray = new(0.5f, 0.5f, 0.5f);
    
    // Set ground color to black, to hide it for now.
    private static readonly Vector3 GroundColor = new(0.0f, 0.0f, 0.0f);
    //private static readonly Vector3 GroundColor = new(0.15f, 0.55f, 0.2f);

    // Bird local axes: x forward, y left, z up; units are meters.
    public static Vertex[] CreateBird()
    {
        var v = new List<Vertex>();

        const float wingHalfWidth = 0.025f;
        const float wingHalfHeight = 0.010f;
        const float wingLength = 0.30f;
        foreach (float side in new[] { 1f, -1f })
        {
            AddTetrahedron(v,
                new Vector3(-wingHalfWidth, 0, -wingHalfHeight),
                new Vector3(wingHalfWidth, 0, -wingHalfHeight),
                new Vector3(0, 0, wingHalfHeight),
                new Vector3(0, side * wingLength, 0),
                Blue, Green, side);
        }

        // Head
        AddTetrahedron(v,
            new Vector3(0, -0.015f, -0.01f),
            new Vector3(0, 0.015f, -0.01f),
            new Vector3(0, 0, 0.01f),
            new Vector3(0.10f, 0, 0),
            White, White);

        // Tail
        AddTetrahedron(v,
            new Vector3(-0.10f, -0.050f, -0.0025f),
            new Vector3(-0.10f, 0.050f, -0.0025f),
            new Vector3(-0.10f, 0, 0.0025f),
            Vector3.Zero,
            BrightBlue, Red);

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

    // A face is "top" when its outward normal points up (+Z), otherwise "bottom".
    private static void AddTetrahedron(List<Vertex> v, Vector3 a, Vector3 b, Vector3 c, Vector3 apex, Vector3 topColor, Vector3 bottomColor, float wingSide = 0f)
    {
        Vector3 centroid = (a + b + c + apex) / 4f;
        AddTriangle(v, a, b, c, centroid, topColor, bottomColor, wingSide);
        AddTriangle(v, a, b, apex, centroid, topColor, bottomColor, wingSide);
        AddTriangle(v, b, c, apex, centroid, topColor, bottomColor, wingSide);
        AddTriangle(v, c, a, apex, centroid, topColor, bottomColor, wingSide);
    }

    private static void AddTriangle(List<Vertex> v, Vector3 a, Vector3 b, Vector3 c, Vector3 solidCentroid, Vector3 topColor, Vector3 bottomColor, float wingSide)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(normal, (a + b + c) / 3f - solidCentroid) < 0) normal = -normal;
        Vector3 color = normal.Z >= 0 ? topColor : bottomColor;

        v.Add(new Vertex(a, color, wingSide));
        v.Add(new Vertex(b, color, wingSide));
        v.Add(new Vertex(c, color, wingSide));
    }
}
