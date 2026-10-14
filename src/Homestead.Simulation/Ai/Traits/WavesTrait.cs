using System.Text.Json;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Waves (<c>waves</c> in docs/KI-TACTICS.md): calm phases of <c>calmSeconds</c> without campaigns, in
/// which the army grows, alternate with waves of <c>waveSeconds</c> in which campaigns may start one
/// after another. Each phase lasts up to <c>jitter</c> (share) longer or shorter, so the rhythm cannot be
/// counted on. The first wave comes after the first calm phase. Defence and guerilla are not held back.
/// </summary>
internal sealed class WavesTrait(JsonElement settings) : AiTrait
{
    public const string TraitId = "waves";

    private readonly double _calmSeconds = Setting(settings, "calmSeconds", 480);
    private readonly double _waveSeconds = Setting(settings, "waveSeconds", 180);
    private readonly double _jitter = Setting(settings, "jitter", 0.25);
    private Random? _random;
    private long _phaseEnds = -1;
    private bool _wave;

    public override string Id => TraitId;

    internal override bool AllowCampaign(AiPlayer ai)
    {
        long now = ai.World.Tick;
        _random ??= new Random(5151 + ai.World.Seed);
        if (_phaseEnds < 0)
            _phaseEnds = now + Ticks(_calmSeconds);
        else if (now >= _phaseEnds)
        {
            _wave = !_wave;
            _phaseEnds = now + Ticks(_wave ? _waveSeconds : _calmSeconds);
            ai.Note(_wave ? $"waves: a wave starts for {(_phaseEnds - now) / World.TicksPerSecond} s"
                : $"waves: calm for {(_phaseEnds - now) / World.TicksPerSecond} s");
        }
        return _wave;
    }

    private long Ticks(double seconds) =>
        (long)(seconds * (1 + _jitter * (2 * _random.NextDouble() - 1)) * World.TicksPerSecond);
}
