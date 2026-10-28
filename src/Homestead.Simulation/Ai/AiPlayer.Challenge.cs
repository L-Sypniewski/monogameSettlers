using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Ai;

/// <summary>
/// A challenge (the chat with the computer players, <see cref="AiSituation"/>): the AI starts a campaign against one
/// player at its next military think, whatever the armies, the distance or the character's timing say – only the soldiers
/// at hand have to match the target's defence one to one. Everything after that is an ordinary campaign: it gathers, attacks and retreats as always.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>Share of the other idle fighters going along against a challenger at least (more if the character commits more).</summary>
    private const double ChallengeCommit = 0.5;

    /// <summary>The player whose challenge it takes up at the next military think, or null.</summary>
    public int? ChallengedBy { get; private set; }

    /// <summary>The player its campaign goes against right now, or null.</summary>
    public int? CampaignAgainst => _campaign?.Target is { Exists: true } target ? target.Player : null;

    /// <summary>Whether its campaign has left the gathering point and fights.</summary>
    public bool CampaignAttacking => _campaign?.Attacking == true;

    /// <summary>
    /// Whether a challenge of <paramref name="player"/> could start a campaign now: an enemy, nothing else under way, and one
    /// of its occupied military buildings within the idle soldiers' means (told to the chat before it promises an attack).
    /// </summary>
    public bool CanTakeUpChallenge(int player)
    {
        if (!IsEnemy(player) || _campaign is not null || AssaultOn is not null || _world.PathFinder is null)
            return false;
        var military = _world.Buildings.Where(b => b.Exists && b.Player == Player && IsMilitary(b) && b.State == BuildingState.Finished).ToList();
        var fighters = IdleSoldiers().Where(s => s.SoldierType != SoldierType.Bowman).ToList();
        return military.Count > 0 && fighters.Count > 0 && ChooseCampaign(military, fighters, player) is not null;
    }

    /// <summary>Takes up the challenge of <paramref name="player"/> (no ally of it; not while a campaign or an assault runs).</summary>
    public bool Challenge(int player)
    {
        if (!IsEnemy(player) || _campaign is not null || AssaultOn is not null)
            return false;
        ChallengedBy = player;
        _nextMilitary = _world.Tick;
        return true;
    }
}
