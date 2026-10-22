namespace Homestead.Simulation;

/// <summary>
/// Victory and defeat of a free game, checked once a second: a player who has lost every finished military building
/// (tower, big tower, castle – lost, destroyed or torn down) once having had one is defeated for good; the own player
/// (player 0) defeated loses, every other player defeated wins. Decided once, then it stays; the game goes on, and the
/// other players' defeats are still noted.
/// </summary>
public sealed class ConquestGoals : IGameGoals
{
    public const int Player = 0;

    public MissionOutcome Outcome { get; private set; }

    public long DecidedAt { get; private set; } = -1;

    /// <summary>The players who have had a military building (only they can lose them all).</summary>
    private readonly HashSet<int> _hadGuard = new();

    private readonly HashSet<int> _defeated = new();

    public bool IsDefeated(int player) => _defeated.Contains(player);

    public void Tick(World world)
    {
        if (world.Tick % World.TicksPerSecond != 0)
            return;
        var guarded = world.Buildings
            .Where(b => b.Exists && b.State == Buildings.BuildingState.Finished && b.Definition.Occupiers.Count > 0)
            .Select(b => b.Player)
            .ToHashSet();
        _hadGuard.UnionWith(guarded);
        foreach (int player in _hadGuard)
            if (!guarded.Contains(player))
                _defeated.Add(player);
        if (_defeated.Contains(Player))
            Decide(MissionOutcome.Lost, world.Tick);
        else if (_hadGuard.Any(p => p != Player) && _hadGuard.Where(p => p != Player).All(_defeated.Contains))
            Decide(MissionOutcome.Won, world.Tick);
    }

    public void Decide(MissionOutcome outcome, long tick)
    {
        if (Outcome != MissionOutcome.Open || outcome == MissionOutcome.Open)
            return;
        Outcome = outcome;
        DecidedAt = tick;
    }
}
