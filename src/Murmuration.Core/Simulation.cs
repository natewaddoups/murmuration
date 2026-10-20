using System.Numerics;

namespace Murmuration;

public enum ClimbDirection { Neither, Left, Right }

public struct Bird
{
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Up;
    public float Speed;
    public float Mass;
    public ClimbDirection Climb;
    public float ClimbStrength;
}

public sealed class Simulation
{
    public static readonly Vector3 Center = new(0, 0, 25);
    private const float Gravity = 9.81f;
    private const float NeighborDistance = 1.0f;
    private const float CenterRadius = 10.0f;

    private readonly Vector3[] _newDirections;
    private readonly Random _rng;

    public Bird[] Birds { get; }

    public Simulation(int count, int seed = 1)
    {
        var rng = _rng = new Random(seed);
        Birds = new Bird[count];
        _newDirections = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 dir;
            do
            {
                dir = new Vector3(Range(rng, -1, 1), Range(rng, -1, 1), Range(rng, -1, 1));
            } while (dir.LengthSquared() < 1e-4f);

            Birds[i] = new Bird
            {
                Direction = Vector3.Normalize(dir),
                Up = Vector3.UnitZ,
                Position = new Vector3(Range(rng, -50, 50), Range(rng, -50, 50), Range(rng, 0, 50)),
                Speed = Range(rng, 15, 18),
                Mass = Range(rng, 0.25f, 0.5f),
                Climb = ClimbDirection.Neither,
                ClimbStrength = Range(rng, 0.7f, 1.2f),
            };
        }
    }

    // 180 deg/s at 15 m/s falling linearly to 135 deg/s at 20 m/s.
    public static float MaxTurnRate(float speed)
    {
        float degrees = 180f - 9f * (speed - 15f);
        return Math.Max(0f, degrees) * (MathF.PI / 180f);
    }

    public void Step(float dt)
    {
        var birds = Birds;

        // Directions are computed from the previous positions before anything moves.
        for (int i = 0; i < birds.Length; i++)
        {
            ref readonly Bird b = ref birds[i];
            float maxAngle = MaxTurnRate(b.Speed) * dt;

            Vector3 toCenter = Vector3.Zero;
            Vector3 offset = Center - b.Position;

            Vector3 toNeighbor = Vector3.Zero;
            Vector3 alignment = Vector3.Zero;
            Vector3 climbYaw = Vector3.Zero;
            if (b.Direction.Z > 0f && b.Climb != ClimbDirection.Neither)
            {
                // Positive rotation about up turns left.
                float yaw = maxAngle * b.ClimbStrength * (b.Climb == ClimbDirection.Left ? 1f : -1f);
                var rotation = Quaternion.CreateFromAxisAngle(b.Up, yaw);
                climbYaw = Vector3.Transform(b.Direction, rotation) - b.Direction;
            } 
            else
            {
                float manhattan = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
                if (manhattan > CenterRadius)
                    toCenter = Steer(b.Direction, offset, maxAngle) - b.Direction;
            }

            if (TryGetNeighbor(i, out int neighbor, out Vector3 target))
            {
                toNeighbor = Steer(b.Direction, target - b.Position, maxAngle) - b.Direction;
                alignment = Steer(b.Direction, birds[neighbor].Direction, maxAngle) - b.Direction;
            }

            Vector3 sum = b.Direction + toCenter + toNeighbor + alignment + climbYaw;
            _newDirections[i] = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : b.Direction;
        }

        for (int i = 0; i < birds.Length; i++)
        {
            ref Bird b = ref birds[i];
            Vector3 oldDir = b.Direction;
            Vector3 newDir = _newDirections[i];

            b.Up = ComputeUp(oldDir, newDir, b.Up, b.Speed, dt);
            b.Direction = newDir;
            b.Position += newDir * b.Speed * dt;

            if (oldDir.Z <= 0f && newDir.Z > 0f)
            {
                double roll = _rng.NextDouble();
                double subset = 0.2;
                b.Climb = roll < subset ? ClimbDirection.Left : roll < (subset * 2) ? ClimbDirection.Right : ClimbDirection.Neither;
            }
        }
    }

    // Target is 1 m from the nearest bird, on the line from this bird to that one.
    private bool TryGetNeighbor(int index, out int nearest, out Vector3 target)
    {
        target = default;
        nearest = -1;
        float best = float.MaxValue;
        Vector3 p = Birds[index].Position;

        for (int j = 0; j < Birds.Length; j++)
        {
            if (j == index) continue;
            float d = Vector3.DistanceSquared(p, Birds[j].Position);
            if (d < best)
            {
                best = d;
                nearest = j;
            }
        }

        if (nearest < 0 || best < 1e-12f) return false;

        Vector3 other = Birds[nearest].Position;
        Vector3 toOther = Vector3.Normalize(other - p);
        target = other - toOther * NeighborDistance;
        return true;
    }

    // Rotates dir toward desired by at most maxAngle radians.
    private static Vector3 Steer(Vector3 dir, Vector3 desired, float maxAngle)
    {
        if (desired.LengthSquared() < 1e-12f) return dir;
        desired = Vector3.Normalize(desired);

        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(dir, desired), -1f, 1f));
        if (angle < 1e-6f) return dir;

        Vector3 axis = Vector3.Cross(dir, desired);
        if (axis.LengthSquared() < 1e-12f)
        {
            // Opposite directions: any perpendicular axis works.
            axis = Vector3.Cross(dir, Math.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
        }
        axis = Vector3.Normalize(axis);

        var rotation = Quaternion.CreateFromAxisAngle(axis, Math.Min(angle, maxAngle));
        return Vector3.Normalize(Vector3.Transform(dir, rotation));
    }

    // Tilts "up" so the net of gravity and centripetal acceleration lies along it.
    private static Vector3 ComputeUp(Vector3 oldDir, Vector3 newDir, Vector3 previousUp, float speed, float dt)
    {
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(oldDir, newDir), -1f, 1f));
        float turnRate = angle / dt;
        float centripetal = speed * turnRate; // v^2 / r with r = v / turnRate

        Vector3 delta = newDir - oldDir;
        Vector3 inward = delta - newDir * Vector3.Dot(delta, newDir);

        Vector3 up = Vector3.UnitZ * Gravity;
        if (inward.LengthSquared() > 1e-12f)
            up += Vector3.Normalize(inward) * centripetal;

        up -= newDir * Vector3.Dot(up, newDir);
        return up.LengthSquared() > 1e-12f ? Vector3.Normalize(up) : previousUp;
    }

    private static float Range(Random rng, float min, float max) =>
        min + (float)rng.NextDouble() * (max - min);
}
