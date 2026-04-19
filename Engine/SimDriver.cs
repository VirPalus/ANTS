namespace ANTS;
using System.Diagnostics;

/// <summary>
/// Owns simulation-time state and advances the world at fixed SimHz.
/// Pure sim-domain; UI side-effects (dirty flags, layout) live in Engine wrappers.
/// </summary>
public sealed class SimDriver
{
    public static readonly double[] SpeedChoices = new double[] { 1.0, 2.0, 5.0, 10.0 };

    private World _world;
    private readonly long _ticksPerSimStep;
    private long _simAccumulatorTicks;
    private long _lastSimTimestamp;
    private bool _paused;
    private double _speedMultiplier = 1.0;

    public SimDriver(World world)
    {
        _world = world;
        _ticksPerSimStep = Stopwatch.Frequency / World.SimHz;
        _lastSimTimestamp = Stopwatch.GetTimestamp();
    }

    public void SetWorld(World world)
    {
        _world = world;
    }

    public bool IsPaused => _paused;
    public double Speed => _speedMultiplier;

    public void Advance()
    {
        long simStartTicks = Stopwatch.GetTimestamp();
        long simDeltaTicks = simStartTicks - _lastSimTimestamp;
        _lastSimTimestamp = simStartTicks;

        if (_paused)
        {
            _simAccumulatorTicks = 0;
        }
        else
        {
            long scaledDelta = (long)(simDeltaTicks * _speedMultiplier);
            _simAccumulatorTicks += scaledDelta;
            while (_simAccumulatorTicks >= _ticksPerSimStep)
            {
                _world.Update();
                _simAccumulatorTicks -= _ticksPerSimStep;
            }
        }
    }

    public void TogglePause()
    {
        _paused = !_paused;
    }

    public void SetSpeed(double speed)
    {
        if (Math.Abs(_speedMultiplier - speed) < 0.001) return;
        _speedMultiplier = speed;
        _simAccumulatorTicks = 0;
    }
}
