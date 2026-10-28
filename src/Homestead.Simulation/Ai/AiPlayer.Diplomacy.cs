using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// What the chat with the computer players (<see cref="AiSituation"/>) may agree on besides a challenge:
/// <see cref="Truce"/> – no campaigns, assault or guerilla against one player for a while (a campaign against it under way
/// goes home); defence on the own land goes on, and enemies of that player standing on the own land break it.
/// <see cref="Help"/> – a share of the idle soldiers goes to an ally's land for a while and fights the enemies there.
/// Both saved with the game (lazily created: games saved before there were any).
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>Share of the idle soldiers sent to help an ally (at least one).</summary>
    private const double HelpShare = 0.5;

    private Dictionary<int, long>? _truces;
    private Dictionary<int, long> Truces => _truces ??= new();

    internal sealed class Helpers(int ally, long until, List<Settler> soldiers)
    {
        public int Ally { get; } = ally;
        public long Until { get; } = until;
        public List<Settler> Soldiers { get; } = soldiers;
    }

    private Helpers? _help;

    /// <summary>Ticks of the truce with <paramref name="player"/> left, or null without one.</summary>
    public long? TruceLeft(int player) =>
        Truces.TryGetValue(player, out long until) && until > _world.Tick ? until - _world.Tick : null;

    /// <summary>The ally it helps right now, or null.</summary>
    public int? Helping => _help?.Ally;

    /// <summary>Soldiers on their way to or fighting on the ally's land.</summary>
    public int HelperCount => _help?.Soldiers.Count ?? 0;

    /// <summary>
    /// A truce with an enemy for <paramref name="ticks"/>: a campaign, an assault or a challenge against it ends (the
    /// soldiers go home), guerilla strikes against it are called off, and none of these start before it runs out.
    /// </summary>
    public bool Truce(int player, long ticks)
    {
        if (player == Player || !IsEnemy(player) || ticks <= 0)
            return false;
        Truces[player] = _world.Tick + ticks;
        if (ChallengedBy == player)
            ChallengedBy = null;
        if (AssaultOn == player)
            AssaultOn = null;
        if (_campaign is { } c && c.Target.Player == player)
        {
            _world.Military.Move(c.Soldiers, HomeDoor());
            _campaign = null;
        }
        foreach (var strike in _strikes.Where(k => k.Guerilla && k.Targets.All(t => t.Player == player)).ToList())
        {
            _world.Military.Move(strike.Soldiers, HomeDoor());
            _strikes.Remove(strike);
        }
        _nextMilitary = _world.Tick;
        Note($"truce with player {player + 1} for {ticks / World.TicksPerSecond} s");
        return true;
    }

    /// <summary>Ends the truce with <paramref name="player"/> (attacking it again, or it broke the truce).</summary>
    private void EndTruce(int player, string why)
    {
        if (Truces.Remove(player))
            Note($"truce with player {player + 1} over: {why}");
    }

    /// <summary>Whether a truce with the player holds (an expired one is dropped).</summary>
    internal bool InTruce(int player)
    {
        if (!Truces.TryGetValue(player, out long until))
            return false;
        if (until > _world.Tick)
            return true;
        EndTruce(player, "time is up");
        return false;
    }

    /// <summary>Whether the group is made up of players it has a truce with only.</summary>
    private bool InTruce(EnemyGroup group) => group.Soldiers.All(s => InTruce(s.Player));

    /// <summary>Sends <see cref="HelpShare"/> of the idle soldiers to <paramref name="ally"/>'s land for <paramref name="ticks"/>.</summary>
    public bool Help(int ally, long ticks)
    {
        if (ally == Player || IsEnemy(ally) || _help is not null || ticks <= 0 || !CanHelp(ally))
            return false;
        var idle = IdleSoldiers();
        var soldiers = idle.Take(Math.Max(1, (int)Math.Ceiling(idle.Count * HelpShare))).ToList();
        _help = new Helpers(ally, _world.Tick + ticks, soldiers);
        Note($"helps player {ally + 1} with {soldiers.Count} soldiers for {ticks / World.TicksPerSecond} s");
        FollowHelp();
        return true;
    }

    /// <summary>Whether it could help <paramref name="ally"/> now: an ally with buildings, idle soldiers, no help under way.</summary>
    public bool CanHelp(int ally) =>
        ally != Player && !IsEnemy(ally) && _help is null && _world.Buildings.Any(b => b.Exists && b.Player == ally)
        && IdleSoldiers().Count > 0;

    /// <summary>
    /// Every military think: the helpers go for the strongest enemies on the ally's land, without any to the ally's military
    /// building nearest an enemy building; once the time is up or the ally has no building left they go home.
    /// </summary>
    private void FollowHelp()
    {
        if (_help is not { } help)
            return;
        help.Soldiers.RemoveAll(s => s.IsDead || s.IsInside || s.Player != Player);
        var allyBuildings = _world.Buildings.Where(b => b.Exists && b.Player == help.Ally).ToList();
        if (_world.Tick >= help.Until || help.Soldiers.Count == 0 || allyBuildings.Count == 0 || IsEnemy(help.Ally))
        {
            Note(help.Soldiers.Count == 0 ? $"lost all helpers on the land of player {help.Ally + 1}"
                : $"helpers come home from player {help.Ally + 1}");
            _world.Military.Move(help.Soldiers, HomeDoor());
            _help = null;
            return;
        }
        var map = _world.Map!;
        GridPoint? goal = FindEnemyGroups(_config.Military.GroupRadius)
            .Where(g => g.Armed && map.OwnerAt(g.Center) == help.Ally).MaxBy(g => g.Strength)?.Center;
        if (goal is null)
        {
            var enemies = _world.Buildings.Where(b => b.Exists && IsEnemy(b.Player)).Select(b => b.Position).ToList();
            var front = allyBuildings.Where(IsMilitary).DefaultIfEmpty(allyBuildings[0])
                .MinBy(b => enemies.Count == 0 ? 0 : enemies.Min(e => PathFinder.Distance(e, b.Position)))!;
            goal = front.Door;
        }
        // Those fighting go on; the others (idle, or walking to an old goal) take the new one.
        var free = help.Soldiers.Where(s => !s.HasTask || _world.Military.MoveTarget(s) is { } to && PathFinder.Distance(to, goal.Value) > 3)
            .Where(s => PathFinder.Distance(s.Position, goal.Value) > _config.Military.GuardDistance).ToList();
        if (free.Count > 0)
            _world.Military.Move(free, goal.Value);
    }
}
