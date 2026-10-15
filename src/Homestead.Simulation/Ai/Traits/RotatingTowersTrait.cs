using System.Text.Json;
using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Rotating towers (<c>rotatingTowers</c> in docs/KI-TACTICS.md): without a threat, the idle soldiers are
/// shared out evenly among a random <c>share</c> of the own military buildings, drawn anew every
/// <c>rotateSeconds</c> – the enemy cannot tell where they are. With a threat it leaves them to the next
/// trait (guardByThreat).
/// </summary>
internal sealed class RotatingTowersTrait(JsonElement settings) : AiTrait
{
    public const string TraitId = "rotatingTowers";

    private readonly double _share = Setting(settings, "share", 0.3);
    private readonly int _rotateTicks = Math.Max(1, (int)(Setting(settings, "rotateSeconds", 60) * World.TicksPerSecond));
    private long _round = -1;
    private int _count;
    private List<Building> _posts = [];

    public override string Id => TraitId;

    internal override bool Guard(AiPlayer ai, List<Building> military, List<Settler> idle)
    {
        if (ai.Threatened(military))
            return false;
        long round = ai.World.Tick / _rotateTicks;
        int count = Math.Max(1, (int)Math.Round(military.Count * _share));
        if (round != _round || count != _count || _posts.Any(b => !b.Exists || b.Player != ai.Player))
        {
            // The same seed plays the same game.
            var random = new Random(unchecked((int)(ai.World.Seed * 7919L + ai.Player * 104729L + round * 31L)));
            var posts = military.OrderBy(b => b.Id).OrderBy(_ => random.Next()).Take(count).ToList();
            if (!posts.ToHashSet().SetEquals(_posts))
                ai.Note($"{TraitId}: guards {string.Join(" ", posts.Select(b => $"{b.Position.X},{b.Position.Y}"))}");
            _posts = posts;
            _round = round;
            _count = count;
        }
        ai.Distribute(_posts, idle);
        return true;
    }
}
