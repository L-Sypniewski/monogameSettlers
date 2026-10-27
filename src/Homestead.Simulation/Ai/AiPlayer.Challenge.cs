namespace Homestead.Simulation.Ai;

/// <summary>
/// A challenge (the chat with the computer players, <see cref="AiSituation"/>): the AI starts a campaign against one
/// player at its next military think, whatever the armies say – only the target building has to be within its means
/// (<c>attack.ratio</c>). Everything after that is an ordinary campaign: it gathers, attacks and retreats as always.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>The player whose challenge it takes up at the next military think, or null.</summary>
    public int? ChallengedBy { get; private set; }

    /// <summary>The player its campaign goes against right now, or null.</summary>
    public int? CampaignAgainst => _campaign?.Target is { Exists: true } target ? target.Player : null;

    /// <summary>Whether its campaign has left the gathering point and fights.</summary>
    public bool CampaignAttacking => _campaign?.Attacking == true;

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
