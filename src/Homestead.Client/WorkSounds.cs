using Homestead.Simulation;

namespace Homestead.Client;

/// <summary>
/// The sounds of work: settlers working outside sound once a round of their work animation, at the moment
/// <c>work</c> in <c>sound.json</c> names (chop, dig, hammer …), so the sound keeps time with the picture; workshops
/// (<c>workshops</c>) sound now and then while their worker works inside. Only what can be heard is looked at.
/// </summary>
public sealed class WorkSounds
{
    private Dictionary<int, long> _rounds = new(), _seen = new();
    private readonly Dictionary<int, double> _workshopNext = new();
    private readonly Random _random = new();

    /// <param name="now">Game time in seconds (stands still with the game).</param>
    public void Update(World world, SettlerRenderer renderer, GameAudio audio, double now)
    {
        var config = audio.Config;
        float volume = config.VolumeOf("work");
        _seen.Clear();
        foreach (var settler in world.Settlers)
        {
            if (settler.IsInside || settler.State == SettlerState.Stepping || !config.Work.TryGetValue(settler.Activity, out var sound)
                || !audio.Hears(settler.Position) || renderer.WorkRoundFrames(settler) is not { } frames || frames <= 0)
                continue;
            // Rounds of the animation that passed the moment of the sound.
            float work = settler.ActivityTicks * SettlerRenderer.WorkFramesPerTick;
            float moment = sound.At * frames;
            long round = work < moment ? 0 : (long)((work - moment) / frames) + 1;
            if (_rounds.TryGetValue(settler.Id, out long last) && round > last)
                audio.Play(sound.Sound, settler.Position, volume);
            _seen[settler.Id] = round;
        }
        (_rounds, _seen) = (_seen, _rounds);

        foreach (var building in world.Buildings)
        {
            if (!config.Workshops.TryGetValue(building.Definition.Name, out var workshop))
                continue;
            if (!world.Production.IsProducing(building) || !audio.Hears(building.Position))
            {
                _workshopNext.Remove(building.Id);
                continue;
            }
            if (!_workshopNext.TryGetValue(building.Id, out double next))
                next = now + _random.NextDouble() * workshop.EverySeconds * 0.5;
            if (now >= next)
            {
                audio.Play(workshop.Sounds[_random.Next(workshop.Sounds.Length)], building.Position, volume);
                next = now + workshop.EverySeconds * (0.85 + 0.3 * _random.NextDouble());
            }
            _workshopNext[building.Id] = next;
        }
    }
}
