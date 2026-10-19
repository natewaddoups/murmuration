using System.Numerics;

namespace Murmuration;

public static class Camera
{
    private static readonly Vector3 Eye = new(-50, -50, 25);
    private static readonly Vector3 Target = new(0, 0, 25);

    // Horizontal FOV of 100 degrees covers the 90 degrees needed to see the side corners.
    private const float HorizontalFov = 100f * MathF.PI / 180f;

    public static Matrix4x4 ViewProjection(float aspect)
    {
        float verticalFov = 2f * MathF.Atan(MathF.Tan(HorizontalFov / 2f) / aspect);
        var view = Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitZ);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(verticalFov, aspect, 0.1f, 500f);
        return view * proj;
    }
}
