using System.Text.Json;
using Homestead.Client;
using Homestead.Simulation.Buildings;

namespace Homestead.Client.Tests;

/// <summary>
/// <see cref="UiLayout"/> against the repository's <c>data/ui.json</c>: it reads every key the file has (the load
/// itself fails on a key it requires but the file lacks), and the file has no key it ignores — the same drift gate
/// <c>LanguageTests</c> is for the texts.
/// </summary>
public class UiLayoutTests
{
    [Fact]
    public void Load_reads_every_key_of_ui_json()
    {
        var layout = UiLayout.Load();
        Assert.NotEmpty(layout.PanelTabs);
        Assert.NotEmpty(layout.Status);
        Assert.Equal(4, layout.Portraits.Peoples.Count);
    }

    [Fact]
    public void ui_json_has_no_key_ui_layout_ignores()
    {
        UiLayout.Load();
        var read = UiLayout.ReadPaths.Select(p => p.Split('.')[0]).Distinct().ToHashSet();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var present = document.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('_')).Select(p => p.Name).ToHashSet();
        Assert.Empty(present.Except(read).Select(k => $"ui.json has a key UiLayout never reads: {k}"));
        Assert.Empty(read.Except(present).Select(k => $"UiLayout reads a key ui.json does not have: {k}"));
    }
}
