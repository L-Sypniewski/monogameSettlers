using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Magic;

namespace Homestead.Simulation.Tests;

public class GameSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "homestead-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsFile => Path.Combine(_directory, "settings.json");
    private static readonly MagicConfig Magic = MagicConfig.Load();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Without_a_file_the_settings_are_those_of_production_json()
    {
        var production = ProductionConfig.Load();
        var settings = GameSettings.Load(production, Magic, SettingsFile);
        Assert.Equal(production.Depletion, settings.Depletion);
        Assert.Equal(Magic.CostIncrease, settings.SpellCostIncrease);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void Saved_settings_come_back_and_keep_the_game_directory()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "gameDirectory": "/somewhere/Settlers" }""");
        var production = ProductionConfig.Load();
        var settings = GameSettings.Load(production, Magic, SettingsFile) with { Depletion = new Depletion(50, 0, 25) };
        settings.Save(SettingsFile);

        Assert.Equal(new Depletion(50, 0, 25), GameSettings.Load(production, Magic, SettingsFile).Depletion);
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        Assert.Equal("/somewhere/Settlers", document.RootElement.GetProperty("gameDirectory").GetString());
        Assert.Equal(new Depletion(50, 0, 25), settings.Apply(production).Depletion);
    }

    [Fact]
    public void The_spell_cost_increase_is_saved_and_applied_to_magic()
    {
        var production = ProductionConfig.Load();
        (GameSettings.Defaults(production, Magic) with { SpellCostIncrease = 10 }).Save(SettingsFile);

        var settings = GameSettings.Load(production, Magic, SettingsFile);
        Assert.Equal(10, settings.SpellCostIncrease);
        Assert.Equal(10, settings.Apply(Magic).CostIncrease);
    }

    [Fact]
    public void Sections_of_the_file_leave_each_other_alone()
    {
        var production = ProductionConfig.Load();
        Homestead.Assets.SettingsFile.WriteSection("display", new JsonObject { ["fullScreen"] = true }, SettingsFile);
        (GameSettings.Defaults(production, Magic) with { Depletion = new Depletion(10, 10, 10) }).Save(SettingsFile);
        Homestead.Assets.SettingsFile.WriteSection("display", new JsonObject { ["fullScreen"] = false }, SettingsFile);

        Assert.Equal(new Depletion(10, 10, 10), GameSettings.Load(production, Magic, SettingsFile).Depletion);
        Assert.False(Homestead.Assets.SettingsFile.ReadSection("display", SettingsFile)!.Value.GetProperty("fullScreen").GetBoolean());
    }

    [Fact]
    public void Missing_values_keep_the_default_and_a_broken_or_empty_file_is_replaced()
    {
        Directory.CreateDirectory(_directory);
        var production = ProductionConfig.Load() with { Depletion = new Depletion(100, 100, 100) };
        File.WriteAllText(SettingsFile, """{ "game": { "depletion": { "fish": 10 } } }""");
        Assert.Equal(new Depletion(100, 100, 10), GameSettings.Load(production, Magic, SettingsFile).Depletion);

        File.WriteAllText(SettingsFile, "{ broken");
        Assert.Equal(production.Depletion, GameSettings.Load(production, Magic, SettingsFile).Depletion);
        GameSettings.Defaults(production, Magic).Save(SettingsFile);
        Assert.Equal("{ broken", File.ReadAllText(SettingsFile + ".broken"));
        Assert.Equal(production.Depletion, GameSettings.Load(production, Magic, SettingsFile).Depletion);

        File.WriteAllText(SettingsFile, "");
        (GameSettings.Defaults(production, Magic) with { Depletion = new Depletion(25, 25, 25) }).Save(SettingsFile);
        Assert.Equal(new Depletion(25, 25, 25), GameSettings.Load(production, Magic, SettingsFile).Depletion);
    }
}
