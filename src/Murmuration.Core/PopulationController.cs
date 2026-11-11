namespace Murmuration;

internal sealed class PopulationController
{
    private float _timeSinceLastBird;
    private int _lowFrameRateUpdates;

    public int Advance(int count, int frameRate, float dt)
    {
        const int minFrameRate = 55;
        if (frameRate < minFrameRate)
        {
            _lowFrameRateUpdates++;
            if (_lowFrameRateUpdates >= 3 && count > 10)
                count = Math.Max(10, count - (minFrameRate - frameRate));
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
                count += birdsToAdd;
            }
        }

        return count;
    }
}
