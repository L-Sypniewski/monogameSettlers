namespace Homestead.Simulation;

/// <summary>Victory and defeat of the own player (player 0), and which players are out of the game.</summary>
public interface IGameGoals
{
    MissionOutcome Outcome { get; }

    /// <summary>The tick the outcome was decided; −1 while open.</summary>
    long DecidedAt { get; }

    void Tick(World world);

    /// <summary>Decides the game (also the cheat menu's); a decided one stays as it is.</summary>
    void Decide(MissionOutcome outcome, long tick);

    /// <summary>A defeated player's computer opponent gives up: its settlers and soldiers only go on by themselves.</summary>
    bool IsDefeated(int player);
}
