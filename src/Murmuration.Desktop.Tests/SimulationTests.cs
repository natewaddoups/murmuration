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
                Assert.That(MathF.Abs(bird.FlapAngle), Is.LessThanOrEqualTo(MathF.PI / 4f + 1e-6f));
            }
        }
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
}