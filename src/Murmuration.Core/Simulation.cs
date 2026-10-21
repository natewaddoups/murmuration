using System.Numerics;

namespace Murmuration;

public enum ClimbDirection { Neither, Left, Right }

public struct Bird
{
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Up;
    public float Speed;
    public float PreferredSpeed;
    public float Mass;
    public ClimbDirection Climb;
    public float ClimbStrength;
}

public sealed class Simulation
{
    public static readonly Vector3 Center = new(0, 0, 25);
    private const float Gravity = 9.81f;
    private const float NeighborDistance = 1.0f;
    private const float NeighborRange = 20.0f;
    private const float CenterRadius = 10.0f;
    private const float BankTimeConstant = 0.2f;
    private const float SpeedRelaxRate = 1.0f;
    private const float SlopeSpeedGain = 0.5f;
    private const float StallSpeed = 2.1f;
    private const float MaxSpeed = 5.0f;
    private static readonly float LevelSlope = MathF.Sin(20f * MathF.PI / 180f);

    private Vector3[] _newDirections;
    private readonly Random _rng;
    private float _timeSinceLastBird;
    private int _lowFrameRateUpdates;

    public Bird[] Birds { get; private set; }

    public Simulation(int count, int seed = 1)
    {
        var rng = _rng = new Random(seed);
        Birds = new Bird[count];
        _newDirections = new Vector3[count];

        for (int i = 0; i < count; i++)
            Birds[i] = CreateBird(rng, true);
    }

    // 180 deg/s at 15 m/s falling linearly to 135 deg/s at 20 m/s.
    public static float MaxTurnRate(float speed)
    {
        float degrees = 180f - 9f * (speed - 15f);
        return Math.Max(0f, degrees) * (MathF.PI / 180f);
    }

    public void Step(float dt, int frameRate)
    {
        AdjustPopulationSize(frameRate, dt);
        Think(dt);
        Move(dt);
    }

    private void AdjustPopulationSize(int frameRate, float dt)
    {
        const int minFrameRate = 55;
        if (frameRate < minFrameRate)
        {
            _lowFrameRateUpdates++;
            if (_lowFrameRateUpdates >= 3 && Birds.Length > 10)
                ResizePopulation(Math.Max(10, Birds.Length - (minFrameRate - frameRate)));
        }
        else
        {
            _lowFrameRateUpdates = 0;
        }

        if (frameRate > 59 && dt > 0f)
        {
            _timeSinceLastBird += dt;
            int birdsToAdd = (int)_timeSinceLastBird;
            if (birdsToAdd > 0)
            {
                _timeSinceLastBird -= birdsToAdd;
                int firstNewBird = Birds.Length;
                ResizePopulation(firstNewBird + birdsToAdd);
                for (int i = firstNewBird; i < Birds.Length; i++)
                    Birds[i] = CreateBird(_rng, false);
            }
        }
    }

    private void ResizePopulation(int newSize)
    {
        Bird[] birds = Birds;
        Array.Resize(ref birds, newSize);
        Birds = birds;
        Array.Resize(ref _newDirections, newSize);
    }

    private static Bird CreateBird(Random rng, bool startup)
    {
        Vector3 dir;
        do
        {
            dir = new Vector3(Range(rng, -1, 1), Range(rng, -1, 1), Range(rng, -1, 1));
        } while (dir.LengthSquared() < 1e-4f);

        float preferredSpeed = Range(rng, 3, 4);
        return new Bird
        {
            Direction = Vector3.Normalize(dir),
            Up = Vector3.UnitZ,
            Position = startup ? 
                new Vector3(Range(rng, -50, 50), Range(rng, -50, 50), Range(rng, 0, 50)) :
                new Vector3(Range(rng, 30, 50), Range(rng, 40, 50), 0),
            Speed = preferredSpeed,
            PreferredSpeed = preferredSpeed,
            Mass = Range(rng, 0.25f, 0.5f),
            Climb = ClimbDirection.Neither,
            ClimbStrength = Range(rng, 0.7f, 1.2f),
        };
    }

    private void Think(float dt)
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
            Vector3 toUp = Vector3.Zero;
            Vector3 toDown = Vector3.Zero;

            bool stalled = b.Speed < StallSpeed;
            Vector3 toLevel = Vector3.Zero;

            if (stalled)
            {
                var level = new Vector3(b.Direction.X, b.Direction.Y, 0f);
                if (level.LengthSquared() < 1e-6f) level = Vector3.UnitX;
                toLevel = Steer(b.Direction, level, maxAngle) - b.Direction;
            }
            else if (b.Position.Z < 10f)
            {
                toUp = Steer(b.Direction, Vector3.UnitZ, maxAngle) - b.Direction;
            }
            else if (b.Position.Z > 40f)
            {
                toDown = Steer(b.Direction, -Vector3.UnitZ, maxAngle) - b.Direction;
            }

            if (stalled)
            {
                // Slow birds only steer sideways for cohesion.
            }
            else if (b.Direction.Z > 0f && b.Climb != ClimbDirection.Neither)
            {
                // Spread out when climbing. Positive rotation about the up-vector turns left.
                float yaw = maxAngle * b.ClimbStrength * (b.Climb == ClimbDirection.Left ? 1f : -1f);
                var rotation = Quaternion.CreateFromAxisAngle(b.Up, yaw);
                climbYaw = Vector3.Transform(b.Direction, rotation) - b.Direction;
            } 
            else
            {
                // Aim the center when descending and not close to the center.
                float manhattan = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
                if (manhattan > CenterRadius)
                    toCenter = Steer(b.Direction, offset, maxAngle/2) - b.Direction;
            }

            // Converge towards the nearest neighbor and align with its direction.
            if (TryGetNeighbor(i, out int neighbor, out Vector3 target))
            {
                toNeighbor = Steer(b.Direction, target - b.Position, maxAngle) - b.Direction;
                alignment = Steer(b.Direction, birds[neighbor].Direction, maxAngle) - b.Direction;
                if (stalled)
                {
                    toNeighbor.Z = 0f;
                    alignment.Z = 0f;
                }
            }

            // Combine all steering influences.
            Vector3 sum = b.Direction + toCenter + toNeighbor + alignment + climbYaw + toUp + toDown + toLevel;
            _newDirections[i] = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : b.Direction;
        }
    }

    private void Move(float dt)
    {
        var birds = Birds;
        for (int i = 0; i < birds.Length; i++)
        {
            ref Bird b = ref birds[i];
            Vector3 oldDir = b.Direction;
            Vector3 newDir = _newDirections[i];

            if (MathF.Abs(newDir.Z) <= LevelSlope)
            {
                b.Speed += (b.PreferredSpeed - b.Speed) * (1f - MathF.Exp(-SpeedRelaxRate * dt));
            }
            else
            {
                b.Speed -= SlopeSpeedGain * b.Speed * newDir.Z * dt; // climbing slows, diving speeds up
            }

            b.Speed = Math.Clamp(b.Speed, 0f, MaxSpeed);

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

    // Target is 1 m from the nearest bird ahead of this one, on the line from this bird to that one.
    private bool TryGetNeighbor(int index, out int nearest, out Vector3 target)
    {
        target = default;
        nearest = -1;
        float best = float.MaxValue;
        Vector3 p = Birds[index].Position;
        Vector3 forward = Birds[index].Direction;

        for (int j = 0; j < Birds.Length; j++)
        {
            if (j == index) continue;
            
            Vector3 delta = Birds[j].Position - p;

            // Ignore birds that are too far away. (I can't decide whether this gives better or worse results.)
            float d = delta.LengthSquared();
            // if (d > NeighborRange * NeighborRange) continue;

            // Only look forward. Adding this condition made the flock much more cohesive.
            if (Vector3.Dot(delta, forward) <= 0f) continue;

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
        if (up.LengthSquared() < 1e-12f) return previousUp;
        up = Vector3.Normalize(up);

        // Exponential smoothing so the bank angle can't flip between frames.
        float alpha = 1f - MathF.Exp(-dt / BankTimeConstant);
        Vector3 smoothed = Vector3.Lerp(previousUp, up, alpha);
        smoothed -= newDir * Vector3.Dot(smoothed, newDir);
        return smoothed.LengthSquared() > 1e-12f ? Vector3.Normalize(smoothed) : up;
    }

    private static float Range(Random rng, float min, float max) =>
        min + (float)rng.NextDouble() * (max - min);
}
