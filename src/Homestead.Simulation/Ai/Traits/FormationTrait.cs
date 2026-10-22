using System.Text.Json;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Battle formation (<c>formation</c> in docs/KI-TACTICS.md): bowmen first, melee behind. A strike with
/// bowmen and melee soldiers against enemies mostly on foot (their bowmen at most <c>maxEnemyBowShare</c>
/// of their strength), still at least the bowmen's range away, sends the bowmen ahead to shoot from range
/// and parks the melee soldiers <c>parkDistance</c> fields behind them, beyond the enemies' sight; both hold
/// their position („Position halten“: fight only enemies within reach, chase nobody). When an enemy comes within <c>fallBackDistance</c> of a
/// bowman, the bowmen fall back without fighting behind the melee soldiers and shoot from there, and these
/// go in; later too, whenever an enemy gets that close to a bowman again (at most every <c>fallBackSeconds</c>;
/// bowmen without a target move up again in between). After <c>screenSeconds</c> without that the melee
/// soldiers go in anyway.
/// </summary>
internal sealed class FormationTrait(JsonElement settings) : AiTrait
{
    public const string TraitId = "formation";

    private readonly double _maxEnemyBowShare = Setting(settings, "maxEnemyBowShare", 0.34);
    private readonly int _parkDistance = (int)Setting(settings, "parkDistance", 8);
    private readonly int _fallBackDistance = (int)Setting(settings, "fallBackDistance", 6);
    private readonly long _fallBackTicks = (long)(Setting(settings, "fallBackSeconds", 4) * World.TicksPerSecond);
    private readonly long _screenTicks = (long)(Setting(settings, "screenSeconds", 30) * World.TicksPerSecond);

    /// <summary>How far behind the melee soldiers the bowmen fall back (still within their range of the enemies in front of them).</summary>
    private const int BehindTheLine = 4;

    public override string Id => TraitId;

    internal override StrikeLeader? LeadStrike(AiPlayer ai, AiPlayer.Strike strike, AiPlayer.EnemyGroup group)
    {
        var world = ai.World;
        var bowmen = strike.Soldiers.Where(IsBowman).ToList();
        var melee = strike.Soldiers.Except(bowmen).ToList();
        if (bowmen.Count == 0 || melee.Count == 0 || !group.Armed)
            return null;
        // Against bowmen of their own, waiting under their arrows wins nothing.
        double enemyBows = group.Soldiers.Where(IsBowman).Sum(s => ai.Strength(s, group.Center));
        if (enemyBows > group.Strength * _maxEnemyBowShare)
            return null;
        int range = world.Military.Config.Soldiers[SoldierType.Bowman].Range;
        // Already at close quarters: no time to form up.
        if (PathFinder.Distance(AiPlayer.CenterOf(strike.Soldiers), group.Center) < range)
            return null;
        var leader = new Leader(this, world.Tick);
        leader.FormUp(ai, bowmen, melee, group.Center);
        ai.Note($"{TraitId}: sends {bowmen.Count} bowmen ahead against {group.Soldiers.Count} enemies at {group.Center.X},{group.Center.Y}, "
                + $"{melee.Count} soldiers wait behind them");
        return leader;
    }

    private static bool IsBowman(Settler soldier) => soldier.SoldierType == SoldierType.Bowman;

    /// <summary>An enemy that fights at close quarters (bowmen keep their distance themselves).</summary>
    private static bool IsMelee(Settler enemy) => enemy.Profession == Profession.Soldier && !IsBowman(enemy);

    /// <summary>The point <paramref name="distance"/> fields from <paramref name="enemy"/> towards <paramref name="from"/>.</summary>
    private static GridPoint Towards(GridPoint enemy, GridPoint from, int distance, int size)
    {
        int away = Math.Max(1, PathFinder.Distance(from, enemy));
        return new GridPoint(Math.Clamp(enemy.X + (from.X - enemy.X) * distance / away, 0, size - 1),
            Math.Clamp(enemy.Y + (from.Y - enemy.Y) * distance / away, 0, size - 1));
    }

    private sealed class Leader(FormationTrait trait, long since) : StrikeLeader
    {
        /// <summary>The melee soldiers still wait behind the bowmen.</summary>
        private bool _screening = true;

        private GridPoint _enemy;
        private long _nextFallBack;

        /// <summary>Bowmen to shoot from a little inside their range, melee soldiers parked behind them, out of the enemies' sight; all holding their position.</summary>
        public void FormUp(AiPlayer ai, List<Settler> bowmen, List<Settler> melee, GridPoint enemy)
        {
            var world = ai.World;
            var from = AiPlayer.CenterOf(bowmen.Concat(melee).ToList());
            int range = world.Military.Config.Soldiers[SoldierType.Bowman].Range;
            int size = world.Map!.Size;
            var front = Towards(enemy, from, range - 2, size);
            var park = Towards(enemy, from, range - 2 + trait._parkDistance, size);
            var waiting = melee.Where(s => s.Activity != Activity.Fighting).ToList();
            var walking = bowmen.Where(s => s.Activity != Activity.Shooting).ToList();
            if (walking.Count > 0)
                world.Military.Move(walking, front, hold: true);
            if (waiting.Count > 0)
                world.Military.Move(waiting, park, forced: true, hold: true);
            _enemy = enemy;
        }

        public override bool Follow(AiPlayer ai, AiPlayer.Strike strike, AiPlayer.EnemyGroup group)
        {
            var world = ai.World;
            var fighting = strike.Fighting.ToList();
            var bowmen = fighting.Where(IsBowman).ToList();
            var melee = fighting.Except(bowmen).ToList();
            // Any enemy on foot, not just the group: one charging the bowmen soon runs out of it.
            var enemies = ai.EnemyGroups.SelectMany(g => g.Soldiers).Where(IsMelee).ToList();
            bool close = bowmen.Any(b => enemies.Any(e => PathFinder.Distance(b.Position, e.Position) <= trait._fallBackDistance));
            if (!_screening)
            {
                // The melee soldiers are in it: bowmen an enemy gets near fall back behind them again; once it is
                // calm there, those with nobody to shoot at move up again.
                if (world.Tick < _nextFallBack)
                    return false;
                if (close && melee.Count > 0)
                    FallBack(ai, bowmen, melee, group.Center);
                else
                {
                    var idle = bowmen.Where(b => world.Military.Holds(b) && b.Activity != Activity.Shooting).ToList();
                    if (idle.Count > 0)
                        world.Military.Move(idle, group.Center);
                }
                return false;
            }
            if (bowmen.Count == 0 || melee.Count == 0)
            {
                _screening = false;
                return false;
            }
            bool reached = melee.Any(s => s.Activity == Activity.Fighting);
            if (close || reached || world.Tick - since >= trait._screenTicks)
            {
                _screening = false;
                if (close)
                    FallBack(ai, bowmen, melee, group.Center);
                var going = melee.Where(s => s.Activity != Activity.Fighting).ToList();
                world.Military.Move(going, group.Center);
                ai.Note($"{TraitId}: {melee.Count} soldiers go in at {group.Center.X},{group.Center.Y}"
                        + (close ? ", the bowmen fall back behind them" : ""));
                return true;
            }
            // The enemies moved: the formation keeps in front of them.
            if (PathFinder.Distance(group.Center, _enemy) > 3)
                FormUp(ai, bowmen, melee, group.Center);
            return true;
        }

        /// <summary>The bowmen walk (without fighting) to a spot behind the melee soldiers, as seen from the enemies, and shoot from there.</summary>
        private void FallBack(AiPlayer ai, List<Settler> bowmen, List<Settler> melee, GridPoint enemy)
        {
            var world = ai.World;
            var line = AiPlayer.CenterOf(melee);
            // In the middle of the enemies: behind is where the bowmen came from.
            var from = PathFinder.Distance(line, enemy) > 0 ? line : AiPlayer.CenterOf(bowmen);
            var behind = Towards(enemy, from, PathFinder.Distance(line, enemy) + BehindTheLine, world.Map!.Size);
            world.Military.Move(bowmen, behind, forced: true, hold: true);
            _nextFallBack = world.Tick + trait._fallBackTicks;
        }
    }
}
