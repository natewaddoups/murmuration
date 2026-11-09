using System.Numerics;

namespace Murmuration;

public struct Bird
{
    public Vector3 Position;
    public Quaternion Orientation;
    public Vector3 DesiredDirection;
    public Vector3 Direction => Vector3.Transform(Vector3.UnitX, Orientation);
    public Vector3 Up => Vector3.Transform(Vector3.UnitZ, Orientation);
    public float Speed;
    public float PreferredSpeed;
    public float Mass;
    public ClimbDirection Climb;
    public float ClimbStrength;
    public float FlapRate; // Hz
    public float PreferredFlapRate; // Hz; FlapRate relaxes back to this when no neighbor is close
    public float FlapPhase; // radians in [0, 2π)
    public float FlapAngle => Simulation.FlapAmplitude(Direction.Z) * MathF.Sin(FlapPhase);
}
