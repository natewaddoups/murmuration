using System.Numerics;

namespace Murmuration;

public enum CameraMode { Original, TopDown, Bird }

public static class Camera
{
    private static readonly Vector3 Eye = new(-10, -10, 0);
    private static readonly Vector3 Target = Simulation.Center;

    // Same convention as Bird: X forward, Z up.
    private static readonly Quaternion EyeOrientation = LookOrientation(Target - Eye, Vector3.UnitZ);

    private static readonly Vector3 TopPosition = new(0, 0, 50);
    private static readonly Quaternion TopOrientation = LookOrientation(-Vector3.UnitZ, Vector3.UnitX);

    // Horizontal FOV in radians
    private const float HorizontalFov = 60f * MathF.PI / 180f;

    // Bird mode disabled because it's too shaky and chaotic
    // TopDown mode disabled until there's a background to make it more understandable.
    private const int ModeCount = 1; 
    private const double HoldDuration = 30.0;
    private const double TransitionDuration = 2.0;
    private const double ModePeriod = HoldDuration + TransitionDuration;
    private const double OrientationTimeConstant = 1;

    // Bird frame: X forward, Z up.
    private static readonly Vector3 BirdCameraOffset = new(-1.0f, 0, 0.02f);

    private static Quaternion s_filteredBirdOrientation;
    private static double s_lastTime = double.NaN;

    public static Matrix4x4 ViewProjection(float aspect, double time, in Bird bird)
    {
        (CameraMode from, CameraMode to, float blend) = SelectMode(time);

        bool birdActive = (from == CameraMode.Bird && blend < 1f) || (to == CameraMode.Bird && blend > 0f);
        Quaternion birdOrientation = FilterOrientation(bird.Orientation, time, birdActive);
        Vector3 birdEye = bird.Position + Vector3.Transform(BirdCameraOffset, bird.Orientation);

        (Vector3 fromPos, Quaternion fromRot) = Pose(from, birdEye, birdOrientation);
        (Vector3 toPos, Quaternion toRot) = Pose(to, birdEye, birdOrientation);
        Vector3 position = Vector3.Lerp(fromPos, toPos, blend);
        Quaternion orientation = Quaternion.Slerp(fromRot, toRot, blend);
        Vector3 forward = Vector3.Transform(Vector3.UnitX, orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitZ, orientation);

        float verticalFov = 2f * MathF.Atan(MathF.Tan(HorizontalFov / 2f) / aspect);
        var view = Matrix4x4.CreateLookAt(position, position + forward, up);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(verticalFov, aspect, 0.1f, 500f);
        return view * proj;
    }

    // blend is 0 while holding `from` and eases to 1 during the transition into `to`.
    private static (CameraMode From, CameraMode To, float Blend) SelectMode(double time)
    {
        double cycle = time % (ModeCount * ModePeriod);
        int index = (int)(cycle / ModePeriod);
        double within = cycle - index * ModePeriod;
        var from = (CameraMode)index;
        var to = (CameraMode)((index + 1) % ModeCount);
        return (from, to, Smooth((within - HoldDuration) / TransitionDuration));
    }

    private static (Vector3 Position, Quaternion Orientation) Pose(CameraMode mode, Vector3 birdEye, Quaternion birdOrientation) =>
        mode switch
        {
            CameraMode.Bird => (birdEye, birdOrientation),
            CameraMode.TopDown => (TopPosition, TopOrientation),
            _ => (Eye, EyeOrientation),
        };

    // Exponential smoothing; snaps to the bird while the bird camera is inactive.
    private static Quaternion FilterOrientation(Quaternion target, double time, bool active)
    {
        double dt = double.IsNaN(s_lastTime) ? 0.0 : Math.Max(0.0, time - s_lastTime);
        s_lastTime = time;

        if (!active)
        {
            s_filteredBirdOrientation = target;
            return target;
        }

        float alpha = (float)(1.0 - Math.Exp(-dt / OrientationTimeConstant));
        s_filteredBirdOrientation = Quaternion.Slerp(s_filteredBirdOrientation, target, alpha);
        return s_filteredBirdOrientation;
    }

    private static float Smooth(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return (float)(t * t * (3.0 - 2.0 * t));
    }

    private static Quaternion LookOrientation(Vector3 forward, Vector3 up)
    {
        Vector3 x = Vector3.Normalize(forward);
        Vector3 y = Vector3.Normalize(Vector3.Cross(up, x));
        Vector3 z = Vector3.Cross(x, y);
        var m = new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(m);
    }
}
