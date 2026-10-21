using Homestead.Simulation.Buildings;

namespace Homestead.Simulation;

/// <summary>What made a sound (the client picks the recording).</summary>
public enum SoundCueKind
{
    /// <summary>A soldier struck or shot (also at a door): <see cref="SoundCue.Soldier"/> says with what.</summary>
    Strike,

    /// <summary>A settler died: <see cref="SoundCue.People"/> says whose voice.</summary>
    Death,
}

/// <summary>Something audible happened at a point in this tick.</summary>
public sealed record SoundCue(SoundCueKind Kind, GridPoint Position, int Player, SoldierType? Soldier = null, Civilization? People = null);

public sealed partial class World
{
    /// <summary>
    /// Where the world puts what can be heard, while not null: the client sets a list and empties it every frame;
    /// without one (arena, tests) nothing is collected. Not saved.
    /// </summary>
    [field: NonSerialized]
    public List<SoundCue>? SoundCues { get; set; }

    internal void Cue(SoundCue cue) => SoundCues?.Add(cue);
}
