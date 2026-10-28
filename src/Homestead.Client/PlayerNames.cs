using Homestead.Simulation;

namespace Homestead.Client;

/// <summary>How players are named in the game: number and people („Spieler 2 (Römer)“), never the AI's character.</summary>
public static class PlayerNames
{
    public static string Of(World world, int player) =>
        Lang.T("start.playerPeople", player + 1, Lang.T("peoples." + world.PeopleOf(player)));
}
