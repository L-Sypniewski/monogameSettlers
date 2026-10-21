namespace Homestead.Client;

/// <summary>
/// The panel's answers to clicks (<c>ui</c> in <c>sound.json</c>): menus call these where something changes, the game
/// sets <see cref="Audio"/> (none: silent, e.g. in capture runs).
/// </summary>
public static class UiSounds
{
    public static GameAudio? Audio { get; set; }

    /// <summary>An order or share one more (<paramref name="more"/>) or fewer.</summary>
    public static void Step(bool more) => Play(more ? "up" : "down");

    /// <summary>An on/off switch flipped.</summary>
    public static void Toggle() => Play("toggle");

    public static void WorkArea() => Play("workArea");

    private static void Play(string name)
    {
        if (Audio is { } audio && audio.Config.Ui.TryGetValue(name, out int sound))
            audio.Play(sound, audio.Config.VolumeOf("ui"));
    }
}
