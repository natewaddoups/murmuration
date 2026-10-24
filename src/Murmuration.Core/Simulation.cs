using System.Numerics;

namespace Murmuration;

public enum ClimbDirection { Neither, Left, Right }

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
}

public sealed class Simulation
{
    public static readonly Vector3 Center = new(0, 0, 25);
    private const float NeighborDistance = 1.0f; // smaller values lead to single-file formations
    private const float CloseNeighborRange = 5.0f;
    private const float NeighborRange = 50.0f;
    private const int NeighborCount = 20;
    private const float CenterRadius = 10.0f;
    private const float MaxRollRate = 360f * MathF.PI / 180f;
    private const float MaxPitchRate = 360f * MathF.PI / 180f;
    private const float Gravity = 9.81f;
    private const float MaxBank = 60f * MathF.PI / 180f;
    private const float MinSpeed = 2.5f;
    private const float SpeedRelaxRate = 1.0f;
    private const float SlopeSpeedGain = 0.5f;
    private const float StallSpeed = 2.75f;
    private const float MaxSpeed = 5.0f;
    private const float SoftFloor = 20f;
    private const float SoftCeiling = 30f;
    private const float ClimbSpreadFraction = 0.1f;
    private static readonly float MaxAscentSin = MathF.Sin(30f * MathF.PI / 180f);
    private static readonly float MaxDescentSin = MathF.Sin(45f * MathF.PI / 180f);
    private static readonly float MinNeighborAlignment = MathF.Cos(90f * MathF.PI / 180f);
    private static readonly float LevelSlope = MathF.Sin(20f * MathF.PI / 180f);

    private Vector3[] _directions = [];
    private float[,] _distances = new float[0, 0]; // squared distances between birds
    private readonly Random _rng;
    private float _timeSinceLastBird;
    private int _lowFrameRateUpdates;

    public Bird[] Birds { get; private set; }

    public Simulation(int count, int seed = 1)
    {
        var rng = _rng = new Random(seed);
        Birds = new Bird[count];

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
        // Temporarily disabled for testing
        AdjustPopulationSize(frameRate, dt);
        Think();
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
    }

    private static Bird CreateBird(Random rng, bool startup)
    {
        Vector3 dir = new Vector3(Range(rng, -1, 1), Range(rng, -1, 1), Range(rng, -1, 1));

        float preferredSpeed = Range(rng, 3, 3.25);
        Vector3 direction = Vector3.Normalize(dir);
        return new Bird
        {
            Orientation = CreateOrientation(direction, Vector3.UnitZ),
            DesiredDirection = direction,
            Position = startup ?
                new Vector3(Range(rng, -50, 50), Range(rng, -50, 50), Range(rng, 40, 50)) :
                new Vector3(Range(rng, 30, 50), Range(rng, 40, 50), 0),
            Speed = preferredSpeed,
            PreferredSpeed = preferredSpeed,
            Mass = Range(rng, 0.25f, 0.5f),
            Climb = ClimbDirection.Neither,
            ClimbStrength = Range(rng, 0.7f, 1.2f),
        };
    }

    private void Think()
    {
        var birds = Birds;
        FillDistances();

        for (int i = 0; i < birds.Length; i++)
        {
            ref Bird bird = ref birds[i];
            Vector3 direction = _directions[i];

            Vector3 toCenter = Vector3.Zero;
            Vector3 offset = Center - bird.Position;
            Vector3 toNeighbor = Vector3.Zero;
            Vector3 alignment = Vector3.Zero;
            Vector3 climbYaw = Vector3.Zero;
            Vector3 toUp = Vector3.Zero;
            Vector3 toDown = Vector3.Zero;
            Vector3 toLevel = Vector3.Zero;

            bool stalled = bird.Speed < StallSpeed;
            if (stalled)
            {
                var level = new Vector3(direction.X, direction.Y, 0f);
                if (level.LengthSquared() < 1e-6f) level = Vector3.UnitX;
                toLevel = SteeringDelta(direction, level);
            }
            else if (bird.Position.Z < SoftFloor)
            {
                toUp = SteeringDelta(direction, Vector3.UnitZ);
            }
            else if (bird.Position.Z > SoftCeiling)
            {
                toDown = SteeringDelta(direction, -Vector3.UnitZ);
            }

            if (stalled)
            {
                // Slow birds only steer sideways for cohesion.
            }
            else if (direction.Z > 0f && bird.Climb != ClimbDirection.Neither)
            {
                // Spread out when climbing. Positive rotation about the up-vector turns left.
                float turn = bird.Climb == ClimbDirection.Left ? 1f : -1f;
                climbYaw = Vector3.Cross(bird.Up, direction) * (turn * bird.ClimbStrength);
            }
            else
            {
                // Aim toward the center when descending and not close to the center.
                float manhattan = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
                if (manhattan > CenterRadius)
                    toCenter = SteeringDelta(direction, offset) * 0.5f;
            }

            if (CheckNeighbors(i, out Vector3 targetDirection, out Vector3 targetPosition))
            {
                toNeighbor = SteeringDelta(direction, targetPosition - bird.Position);
                alignment = SteeringDelta(direction, targetDirection);
                if (stalled)
                {
                    toNeighbor.Z = 0f;
                    alignment.Z = 0f;
                }
            }

            Vector3 sum = direction * 0.5f + toCenter + toNeighbor + alignment + climbYaw + toUp + toDown + toLevel;
            bird.DesiredDirection = ClampSlope(sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : direction, direction);
        }
    }

    private void FillDistances()
    {
        var birds = Birds;
        int count = birds.Length;
        if (_distances.GetLength(0) != count)
        {
            _distances = new float[count, count];
            _directions = new Vector3[count];
        }

        for (int i = 0; i < count; i++)
        {
            _directions[i] = birds[i].Direction;
            Vector3 position = birds[i].Position;
            for (int j = i + 1; j < count; j++)
            {
                float distance = (birds[j].Position - position).LengthSquared();
                _distances[i, j] = distance;
                _distances[j, i] = distance;
            }
        }
    }

    private void Move(float dt)
    {
        var birds = Birds;
        for (int i = 0; i < birds.Length; i++)
        {
            ref Bird bird = ref birds[i];
            if (dt <= 0f) continue;

            Vector3 oldDirection = bird.Direction;
            UpdateOrientation(ref bird, dt);
            Vector3 newDirection = bird.Direction;

            if (MathF.Abs(newDirection.Z) <= LevelSlope)
            {
                bird.Speed += (bird.PreferredSpeed - bird.Speed) * (1f - MathF.Exp(-SpeedRelaxRate * dt));
            }
            else
            {
                bird.Speed -= SlopeSpeedGain * bird.Speed * newDirection.Z * dt; // climbing slows, diving speeds up
            }

            bird.Speed = Math.Clamp(bird.Speed, MinSpeed, MaxSpeed);
            bird.Position += newDirection * bird.Speed * dt;

            if (oldDirection.Z <= 0f && newDirection.Z > 0f)
            {
                double roll = _rng.NextDouble();
                double subset = ClimbSpreadFraction;
                bird.Climb = roll < subset ? ClimbDirection.Left : roll < (subset * 2) ? ClimbDirection.Right : ClimbDirection.Neither;
            }
        }
    }

    // Close neighbors use the nearest point on their perpendicular plane; otherwise target the average position of the closest few eligible birds.
    private bool CheckNeighbors(int index, out Vector3 targetDirection, out Vector3 targetPosition)
    {
        targetPosition = default;
        targetDirection = default;
        Vector3 position = Birds[index].Position;
        Vector3 forward = _directions[index];

        Span<int> best = stackalloc int[NeighborCount];
        Span<float> bestDistance = stackalloc float[NeighborCount];
        int found = 0;

        for (int j = 0; j < Birds.Length; j++)
        {
            if (j == index) continue;

            float distance = _distances[index, j];
            if (distance > NeighborRange * NeighborRange) continue;
            if (found == NeighborCount && distance >= bestDistance[found - 1]) continue;

            // Only look forward. Adding this condition made the flock much more cohesive.
            Vector3 delta = Birds[j].Position - position;
            if (Vector3.Dot(delta, forward) <= 0f) continue;
            if (Vector3.Dot(_directions[j], forward) < MinNeighborAlignment) continue;

            int slot = found < NeighborCount ? found++ : NeighborCount - 1;
            while (slot > 0 && bestDistance[slot - 1] > distance)
            {
                best[slot] = best[slot - 1];
                bestDistance[slot] = bestDistance[slot - 1];
                slot--;
            }
            best[slot] = j;
            bestDistance[slot] = distance;
        }

        if (found == 0 || bestDistance[0] < 1e-12f) return false;

        Vector3 sum = Vector3.Zero;
        Vector3 directionSum = Vector3.Zero;
        for (int k = 0; k < found; k++)
        {
            sum += Birds[best[k]].Position;
            directionSum += _directions[best[k]];
        }

        targetDirection = directionSum.LengthSquared() > 1e-12f ? Vector3.Normalize(directionSum) : _directions[index];
        if (bestDistance[0] <= CloseNeighborRange * CloseNeighborRange)
        {
            Vector3 closestPosition = Birds[best[0]].Position;
            Vector3 closestDirection = _directions[best[0]];
            Vector3 offset = position - closestPosition;
            Vector3 inPlane = offset - Vector3.Dot(offset, closestDirection) * closestDirection;
            if (inPlane.LengthSquared() < 1e-12f)
            {
                Vector3 axis = MathF.Abs(closestDirection.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
                inPlane = Vector3.Cross(closestDirection, axis);
            }

            targetPosition = closestPosition + Vector3.Normalize(inPlane) * NeighborDistance;
            return true;
        }

        Vector3 average = sum / found;
        Vector3 toAverage = average - position;
        if (toAverage.LengthSquared() < 1e-12f) return false;

        // Move back along the line from this bird to the average position by NeighborDistance.
        // Without the distance offset, birds tend to line up single-file.
        targetPosition = average - Vector3.Normalize(toAverage) * NeighborDistance;
        return true;
    }

    private static Vector3 ClampSlope(Vector3 desired, Vector3 current)
    {
        float maxSin = desired.Z > 0f ? MaxAscentSin : MaxDescentSin;
        if (MathF.Abs(desired.Z) <= maxSin) return desired;

        // A near-vertical desired has a noisy azimuth; weighting in the current heading avoids spurious reversals.
        var horizontal = new Vector2(desired.X, desired.Y) + new Vector2(current.X, current.Y);
        if (horizontal.LengthSquared() < 1e-12f) horizontal = Vector2.UnitX;
        horizontal = Vector2.Normalize(horizontal) * MathF.Sqrt(1f - maxSin * maxSin);
        return new Vector3(horizontal.X, horizontal.Y, MathF.CopySign(maxSin, desired.Z));
    }

    private static Vector3 SteeringDelta(Vector3 direction, Vector3 desired)
    {
        if (desired.LengthSquared() < 1e-12f) return Vector3.Zero;
        return Vector3.Normalize(desired) - direction;
    }

    private static void UpdateOrientation(ref Bird bird, float dt)
    {
        Vector3 forward = bird.Direction;
        Vector3 desired = bird.DesiredDirection;
        if (desired.LengthSquared() < 1e-12f) return;
        desired = Vector3.Normalize(desired);

        Vector3 left = Vector3.Transform(Vector3.UnitY, bird.Orientation);

        // Coordinated turn rate at max bank: g * tan(bank) / v.
        float maxTurnRate = MathF.Min(MaxPitchRate, Gravity * MathF.Tan(MaxBank) / MathF.Max(bird.Speed, 0.1f));

        Vector3 turnDir = desired - forward * Vector3.Dot(desired, forward);
        float turnAngle = MathF.Atan2(turnDir.Length(), Vector3.Dot(desired, forward));
        float turnRate = MathF.Min(turnAngle / dt, maxTurnRate);

        // Lift direction is gravity plus centripetal acceleration (coordinated turn).
        Vector3 targetUp = Vector3.UnitZ * Gravity;
        if (turnDir.LengthSquared() > 1e-12f)
            targetUp += Vector3.Normalize(turnDir) * (bird.Speed * turnRate);
        targetUp.Z = MathF.Max(targetUp.Z, 0f); // never roll inverted
        targetUp -= forward * Vector3.Dot(targetUp, forward);
        Vector3 targetLeft = targetUp.LengthSquared() > 1e-12f
            ? Vector3.Normalize(Vector3.Cross(targetUp, forward))
            : GetLevelLeft(forward, left);

        Vector3 levelLeft = GetLevelLeft(forward, left);
        float bank = MathF.Atan2(
            Vector3.Dot(forward, Vector3.Cross(levelLeft, targetLeft)),
            Vector3.Dot(levelLeft, targetLeft));
        float clampedBank = Math.Clamp(bank, -MaxBank, MaxBank);
        if (clampedBank != bank)
            targetLeft = Vector3.Transform(levelLeft, Quaternion.CreateFromAxisAngle(forward, clampedBank));

        Vector3 turnAxis;

        float rollAngle = MathF.Atan2(
            Vector3.Dot(forward, Vector3.Cross(left, targetLeft)),
            Vector3.Dot(left, targetLeft));
        float rollDelta = Math.Clamp(rollAngle, -MaxRollRate * dt, MaxRollRate * dt);
        if (MathF.Abs(rollDelta) > 1e-7f)
        {
            Quaternion roll = Quaternion.CreateFromAxisAngle(forward, rollDelta);
            bird.Orientation = Quaternion.Normalize(roll * bird.Orientation);
        }

        forward = bird.Direction;
        left = Vector3.Transform(Vector3.UnitY, bird.Orientation);

        // Lift sized to cancel gravity's vertical pull leaves only the horizontal turn from banking.
        Vector3 perpZ = Vector3.UnitZ - forward * Vector3.Dot(Vector3.UnitZ, forward);
        float cosTheta = perpZ.Length();
        if (cosTheta > 1e-3f)
        {
            Vector3 perpZHat = perpZ / cosTheta;
            Vector3 up = Vector3.Cross(forward, left);
            float cosBank = MathF.Max(Vector3.Dot(up, perpZHat), MathF.Cos(MaxBank));
            Vector3 net = Gravity * cosTheta * (up / cosBank - perpZHat);
            float netLength = net.Length();
            if (netLength > 1e-6f)
            {
                float rate = MathF.Min(netLength / MathF.Max(bird.Speed, MinSpeed), maxTurnRate);
                Vector3 axis = Vector3.Normalize(Vector3.Cross(forward, net));
                Quaternion natural = Quaternion.CreateFromAxisAngle(axis, rate * dt);
                bird.Orientation = Quaternion.Normalize(natural * bird.Orientation);
            }
        }

        forward = bird.Direction;
        left = Vector3.Transform(Vector3.UnitY, bird.Orientation);
        turnAxis = Vector3.Cross(forward, desired);
        float pitchAngle = MathF.Atan2(
            Vector3.Dot(left, turnAxis),
            Vector3.Dot(forward, desired));
        float pitchDelta = Math.Clamp(pitchAngle, -maxTurnRate * dt, maxTurnRate * dt);
        if (MathF.Abs(pitchDelta) > 1e-7f)
        {
            Quaternion pitch = Quaternion.CreateFromAxisAngle(left, pitchDelta);
            bird.Orientation = Quaternion.Normalize(pitch * bird.Orientation);
        }

        // Steep flight breaks the bank reference, so birds can get stuck; enforce the slope limit on the real pose.
        forward = bird.Direction;
        Vector3 limited = ClampSlope(forward, forward);
        Vector3 fix = Vector3.Cross(forward, limited);
        if (fix.LengthSquared() > 1e-12f)
        {
            float fixAngle = MathF.Atan2(fix.Length(), Vector3.Dot(forward, limited));
            Quaternion correction = Quaternion.CreateFromAxisAngle(Vector3.Normalize(fix), fixAngle);
            bird.Orientation = Quaternion.Normalize(correction * bird.Orientation);
        }
    }

    private static Vector3 GetLevelLeft(Vector3 forward, Vector3 fallbackLeft)
    {
        Vector3 levelUp = Vector3.UnitZ - forward * Vector3.Dot(Vector3.UnitZ, forward);
        if (levelUp.LengthSquared() < 1e-12f)
            return fallbackLeft;

        levelUp = Vector3.Normalize(levelUp);
        return Vector3.Normalize(Vector3.Cross(levelUp, forward));
    }

    private static Quaternion CreateOrientation(Vector3 direction, Vector3 up)
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

    private static float Range(Random rng, double min, double max) =>
        (float)(min + rng.NextDouble() * (max - min));
}
