using System.Numerics;

namespace Murmuration;

internal static class Utility
{
    internal static Vector3 SteeringDelta(Vector3 direction, Vector3 desired)
    {
        if (desired.LengthSquared() < 1e-12f) return Vector3.Zero;
        return Vector3.Normalize(desired) - direction;
    }

    internal static Quaternion CreateOrientation(Vector3 direction, Vector3 up)
    {
        Vector3 forward = Vector3.Normalize(direction);
        up -= forward * Vector3.Dot(up, forward);
        if (up.LengthSquared() < 1e-12f)
        {
            up = Math.Abs(forward.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
            up -= forward * Vector3.Dot(up, forward);
        }
        up = Vector3.Normalize(up);
        Vector3 left = Vector3.Normalize(Vector3.Cross(up, forward));
        up = Vector3.Cross(forward, left);

        var matrix = new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0f,
            left.X, left.Y, left.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.CreateFromRotationMatrix(matrix);
    }

    internal static float Range(Random rng, double min, double max) =>
        (float)(min + rng.NextDouble() * (max - min));
}