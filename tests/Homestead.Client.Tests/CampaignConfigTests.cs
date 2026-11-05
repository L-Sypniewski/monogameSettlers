using Homestead.Client;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Persistence;

namespace Homestead.Client.Tests;

/// <summary>
/// <see cref="CampaignConfig.Load"/> caches per data directory: the lists of saved games and the system menu read it
/// for every row, every frame. <see cref="Lang.SaveName"/> keeps its two shapes (a mission's name, a map's name).
/// </summary>
public class CampaignConfigTests
{
    [Fact]
    public void Repeat_loads_return_the_same_instance()
    {
        var first = CampaignConfig.Load();
        Assert.Same(first, CampaignConfig.Load());
    }

    [Fact]
    public void Another_data_directory_gets_its_own_entry()
    {
        string directory = Path.Combine(Path.GetTempPath(), "homestead-campaign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(BuildingCatalog.DefaultDataDirectory, "campaign.json"), Path.Combine(directory, "campaign.json"));
            var own = CampaignConfig.Load(directory);
            Assert.Same(own, CampaignConfig.Load(directory));
            Assert.NotSame(CampaignConfig.Load(), own);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveName_names_a_mission_by_its_side_and_number()
    {
        var info = new SaveInfo("Kanalkampf", "Roman", new DateTime(2026, 1, 1), 0, "settlers/Roman/0");
        string name = Lang.SaveName(info);
        Assert.Contains(Lang.T("peoples.Roman"), name);
        Assert.EndsWith("1", name); // "campaign.saveName" ends with the mission number in every language
    }

    [Fact]
    public void SaveName_without_a_mission_returns_the_map_name()
    {
        var info = new SaveInfo("Kanalkampf", "Roman", new DateTime(2026, 1, 1), 0, Mission: null);
        Assert.Equal("Kanalkampf", Lang.SaveName(info));
    }
}
