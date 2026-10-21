using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.Assets;

namespace Homestead.Client;

/// <summary>The language of the texts (<see cref="Lang"/>).</summary>
public enum Language { German, English }

/// <summary>
/// How the game shows itself: the <c>display</c> section of <c>settings.json</c> (<see cref="SettingsFile"/>), written
/// by the settings screen, applied at the start and on "Ok". F11 switches full screen for the session only.
/// </summary>
public sealed record DisplaySettings
{
    private const string Section = "display";

    /// <summary>The system's mouse pointer; otherwise the game draws its own (<see cref="SoftwareCursor"/>).</summary>
    public bool HardwareCursor { get; init; } = true;

    public bool FullScreen { get; init; }

    /// <summary>Until chosen: German on a German system, English otherwise.</summary>
    public Language Language { get; init; } =
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? Language.German : Language.English;

    /// <summary>Loudness of the background music in percent (0 = off).</summary>
    public int MusicVolume { get; init; } = 75;

    /// <summary>Music of the own people only; otherwise all pieces.</summary>
    public bool PeopleMusic { get; init; } = true;

    /// <summary>Loudness of the sound effects in percent (0 = off).</summary>
    public int EffectsVolume { get; init; } = 100;

    /// <summary>The settings in the file (default: the user's); missing or unreadable values keep their default.</summary>
    public static DisplaySettings Load(string? path = null)
    {
        var settings = new DisplaySettings();
        if (SettingsFile.ReadSection(Section, path) is not { ValueKind: JsonValueKind.Object } display)
            return settings;
        bool Flag(string name, bool fallback) =>
            display.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
        int Percent(string name, int fallback) =>
            display.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int percent)
                ? Math.Clamp(percent, 0, 100)
                : fallback;
        return settings with
        {
            MusicVolume = Percent("musicVolume", settings.MusicVolume),
            PeopleMusic = Flag("peopleMusic", settings.PeopleMusic),
            EffectsVolume = Percent("effectsVolume", settings.EffectsVolume),
            HardwareCursor = Flag("hardwareCursor", settings.HardwareCursor),
            FullScreen = Flag("fullScreen", settings.FullScreen),
            Language = display.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String
                ? language.GetString() == "en" ? Language.English : Language.German
                : settings.Language,
        };
    }

    /// <summary>Writes the <c>display</c> section into the file (default: the user's), see <see cref="SettingsFile.WriteSection"/>.</summary>
    public void Save(string? path = null) =>
        SettingsFile.WriteSection(Section, new JsonObject
        {
            ["hardwareCursor"] = HardwareCursor,
            ["fullScreen"] = FullScreen,
            ["language"] = Language == Language.English ? "en" : "de",
            ["musicVolume"] = MusicVolume,
            ["peopleMusic"] = PeopleMusic,
            ["effectsVolume"] = EffectsVolume,
        }, path);
}
