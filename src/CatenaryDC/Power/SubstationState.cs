using System;
namespace CatenaryDC.Power;

/// <summary>Main-thread, game-time equivalent of the upstream 10/30 s restoration sequence.
/// No Tasks survive unloading. This is still the upstream simplified load model, not a network solver.</summary>
internal sealed class SubstationState
{
    private const float GraceSeconds = 5;
    private readonly float _supply, _maximum, _regulation;
    private readonly bool _inverter;
    private float _accumulated, _grace = GraceSeconds, _cooldown, _repeatWindow;
    internal float Voltage { get; private set; }
    internal float Load { get; private set; }
    internal bool IsTripped => _cooldown > 0;

    internal SubstationState(float supply, float maximum, bool inverter)
    {
        if (!ElectricalMath.Finite(supply) || supply < 0 || !ElectricalMath.Finite(maximum) || maximum <= 0)
            throw new ArgumentOutOfRangeException("Substation voltage/limit must be finite; limit must be positive.");
        _supply = Voltage = supply; _maximum = maximum; _inverter = inverter;
        _regulation = inverter ? Math.Max(0, (supply - 1300) / maximum) : 0;
    }
    internal void AddLoad(float current)
    {
        if (!ElectricalMath.Finite(current)) throw new ArgumentOutOfRangeException(nameof(current));
        _accumulated += current;
    }
    // Returns true ONLY on the tick that trips the breaker. Injected sample makes tests deterministic.
    internal bool Tick(float dt, float randomSample)
    {
        if (!ElectricalMath.Finite(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        Load = _accumulated; _accumulated = 0;
        if (_cooldown > 0)
        {
            _cooldown = Math.Max(0, _cooldown - dt);
            if (_cooldown == 0) { Voltage = _supply; _repeatWindow = 300; }
            return false;
        }
        _repeatWindow = Math.Max(0, _repeatWindow - dt);
        if (Math.Abs(Load) <= _maximum) _grace = Math.Min(GraceSeconds, _grace + dt);
        else
        {
            _grace -= dt;
            if (_grace < 0)
            {
                _grace += 1;
                // Retains upstream stochastic overload behavior for positive traction current.
                if (randomSample <= (Load / _maximum - 1) * 2)
                {
                    _cooldown = _repeatWindow > 0 ? 30 : 10;
                    Voltage = 0;
                    return true;
                }
            }
        }
        float target = _supply;
        if (Load < -100 && _inverter) target += Load * _regulation;
        Voltage = target * 0.01f + Voltage * 0.99f;
        return false;
    }
}
