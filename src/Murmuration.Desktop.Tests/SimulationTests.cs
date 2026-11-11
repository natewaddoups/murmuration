using System.Numerics;
using Murmuration;
using NUnit.Framework;

namespace Murmuration.Desktop.Tests;

public class SimulationTests
{
    [Test]
    public void SeededSimulationsFollowTheSameTrajectory()
    {
        var first = new Simulation(64, 42);
        var second = new Simulation(64, 42);

        for (int frame = 0; frame < 60; frame++)
        {
            first.Step(1f / 60f, 60);
            second.Step(1f / 60f, 60);
        }

        Assert.That(second.Birds, Is.EqualTo(first.Birds));
    }

    [Test]
    public void MovementKeepsBirdsFiniteAndSpeedWithinLimits()
    {
        var simulation = new Simulation(64, 42);

        for (int frame = 0; frame < 120; frame++)
            simulation.Step(1f / 60f, 60);

        foreach (Bird bird in simulation.Birds)
        {
            Assert.That(float.IsFinite(bird.Position.X) && float.IsFinite(bird.Position.Y) && float.IsFinite(bird.Position.Z), Is.True);
            Assert.That(float.IsFinite(bird.Orientation.X) && float.IsFinite(bird.Orientation.Y) &&
                float.IsFinite(bird.Orientation.Z) && float.IsFinite(bird.Orientation.W), Is.True);
            Assert.That(bird.Speed, Is.InRange(2.5f, 5f));
            Assert.That(bird.Direction.Length(), Is.EqualTo(1f).Within(1e-5f));
        }
    }

    [Test]
    public void BirdsStartWithOwnFlapRateAndPhase()
    {
        var simulation = new Simulation(64, 42);

        Assert.That(simulation.Birds.All(bird => bird.FlapRate is >= 3f and <= 4f), Is.True);
        Assert.That(simulation.Birds.All(bird => bird.FlapPhase is >= 0f and < MathF.Tau), Is.True);
        Assert.That(simulation.Birds.Select(bird => bird.FlapRate).Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void FlapPhaseAdvancesByRateAndStaysWithinAmplitude()
    {
        var simulation = new Simulation(8, 42);
        Bird[] before = simulation.Birds.ToArray();
        const float dt = 1f / 60f;

        simulation.Step(dt, 60);

        for (int i = 0; i < before.Length; i++)
        {
            float expected = (before[i].FlapPhase + MathF.Tau * before[i].FlapRate * dt) % MathF.Tau;
            Assert.That(simulation.Birds[i].FlapPhase, Is.EqualTo(expected).Within(1e-5f));
        }

        for (int frame = 0; frame < 120; frame++)
        {
            simulation.Step(dt, 60);
            foreach (Bird bird in simulation.Birds)
            {
                Assert.That(bird.FlapPhase, Is.InRange(0f, MathF.Tau));
                Assert.That(MathF.Abs(bird.FlapAngle), Is.LessThanOrEqualTo(MathF.PI / 3f + 1e-6f));
            }
        }
    }

    [Test]
    public void NearbyBirdRampsFlapRateTowardNearestBird()
    {
        const float dt = 1f / 60f;
        Simulation simulation = FlapPair(new Vector3(0, 0, 10), 3f, 3f, 0f, new Vector3(1, 0, 10), 4f, 4f, 0f);

        simulation.Step(dt, 58);

        Assert.That(simulation.Birds[0].FlapRate, Is.EqualTo(3f + 1f * dt).Within(1e-5f));
        Assert.That(simulation.Birds[1].FlapRate, Is.EqualTo(4f));
    }

    [Test]
    public void DistantNeighborLetsFlapRateReturnToPreferred()
    {
        const float dt = 1f / 60f;
        Simulation simulation = FlapPair(new Vector3(0, 0, 10), 3.5f, 3f, 0f, new Vector3(30, 0, 10), 4f, 4f, 0f);

        simulation.Step(dt, 58);

        Assert.That(simulation.Birds[0].FlapRate, Is.EqualTo(3.5f - 1f * dt).Within(1e-5f));
    }

    [Test]
    public void CloseRateHoldsUntilPhasesAlign()
    {
        const float dt = 1f / 60f;
        Simulation apart = FlapPair(new Vector3(0, 0, 10), 3.8f, 3.8f, 0f, new Vector3(1, 0, 10), 4f, 4f, MathF.PI);
        Simulation aligned = FlapPair(new Vector3(0, 0, 10), 3.8f, 3.8f, 0.1f, new Vector3(1, 0, 10), 4f, 4f, 0f);

        apart.Step(dt, 58);
        aligned.Step(dt, 58);

        Assert.That(apart.Birds[0].FlapRate, Is.EqualTo(3.8f));
        Assert.That(aligned.Birds[0].FlapRate, Is.EqualTo(3.8f + 1f * dt).Within(1e-5f));
    }

    private static Simulation FlapPair(Vector3 firstPosition, float firstRate, float firstPreferred, float firstPhase,
        Vector3 secondPosition, float secondRate, float secondPreferred, float secondPhase)
    {
        static Bird Create(Vector3 position, float rate, float preferred, float phase) => new()
        {
            Position = position,
            Orientation = Quaternion.Identity,
            DesiredDirection = Vector3.UnitX,
            Speed = 3f,
            PreferredSpeed = 3f,
            Mass = 0.3f,
            FlapRate = rate,
            PreferredFlapRate = preferred,
            FlapPhase = phase,
        };

        var simulation = new Simulation(2, 1);
        simulation.Birds[0] = Create(firstPosition, firstRate, firstPreferred, firstPhase);
        simulation.Birds[1] = Create(secondPosition, secondRate, secondPreferred, secondPhase);
        return simulation;
    }

    [Test]
    public void FlapAmplitudeInterpolatesWithClimbAngle()
    {
        static float Amplitude(float degrees) =>
            Simulation.FlapAmplitude(MathF.Sin(degrees * MathF.PI / 180f)) * 180f / MathF.PI;

        Assert.That(Amplitude(-30f), Is.EqualTo(0f).Within(1e-3f));
        Assert.That(Amplitude(-10f), Is.EqualTo(0f).Within(1e-3f));
        Assert.That(Amplitude(-5f), Is.EqualTo(5f).Within(1e-3f));
        Assert.That(Amplitude(0f), Is.EqualTo(10f).Within(1e-3f));
        Assert.That(Amplitude(22.5f), Is.EqualTo(35f).Within(1e-3f));
        Assert.That(Amplitude(45f), Is.EqualTo(60f).Within(1e-3f));
        Assert.That(Amplitude(80f), Is.EqualTo(60f).Within(1e-3f));
    }

    [Test]
    public void ZeroDeltaDoesNotAdvanceFlapPhase()
    {
        var simulation = new Simulation(8, 42);
        float[] phases = simulation.Birds.Select(bird => bird.FlapPhase).ToArray();

        simulation.Step(0f, 60);

        Assert.That(simulation.Birds.Select(bird => bird.FlapPhase), Is.EqualTo(phases));
    }

    [Test]
    public void ZeroDeltaDoesNotMoveBirds()
    {
        var simulation = new Simulation(16, 42);
        Vector3[] positions = simulation.Birds.Select(bird => bird.Position).ToArray();

        simulation.Step(0f, 60);

        Assert.That(simulation.Birds.Select(bird => bird.Position), Is.EqualTo(positions));
    }

    [Test]
    public void SustainedLowFrameRateReducesPopulationToMinimum()
    {
        var simulation = new Simulation(16, 42);

        for (int frame = 0; frame < 10; frame++)
            simulation.Step(1f / 30f, 30);

        Assert.That(simulation.Birds, Has.Length.EqualTo(10));
    }

    [Test]
    public void HighFrameRateAddsBirdsAfterOneSecond()
    {
        var simulation = new Simulation(16, 42);

        for (int frame = 0; frame < 3; frame++)
            simulation.Step(0.25f, 60);
        Assert.That(simulation.Birds, Has.Length.EqualTo(16));

        simulation.Step(0.25f, 60);
        Assert.That(simulation.Birds, Has.Length.EqualTo(17));
    }

    [Test]
    public void RecoveredFrameRateResetsLowFrameRateStreak()
    {
        var simulation = new Simulation(16, 42);

        simulation.Step(0.01f, 30);
        simulation.Step(0.01f, 30);
        simulation.Step(0.01f, 55);
        simulation.Step(0.01f, 30);
        simulation.Step(0.01f, 30);
        Assert.That(simulation.Birds, Has.Length.EqualTo(16));

        simulation.Step(0.01f, 30);
        Assert.That(simulation.Birds, Has.Length.EqualTo(10));
    }

    [Test]
    public void HighFrameRateDoesNotGrowPopulationBeyondAutomaticLimit()
    {
        var simulation = new Simulation(999, 42);

        simulation.AdjustPopulationSize(60, 2f);

        Assert.That(simulation.Birds, Has.Length.EqualTo(1000));
    }

    [Test]
    public void PopulationStartedAboveAutomaticLimitOnlyShrinksOnLowFrameRate()
    {
        var simulation = new Simulation(1200, 42);

        simulation.AdjustPopulationSize(60, 2f);
        Assert.That(simulation.Birds, Has.Length.EqualTo(1200));

        simulation.AdjustPopulationSize(30, 0.01f);
        simulation.AdjustPopulationSize(30, 0.01f);
        simulation.AdjustPopulationSize(30, 0.01f);
        Assert.That(simulation.Birds, Has.Length.EqualTo(1175));
    }
}