using System.Text.Json;
using Homestead.Simulation.Buildings;

namespace Homestead.Client;

/// <summary>
/// One mission: its map, its description and hints (entries of the text table in GFX file 07), the spoken briefing,
/// the players' short names on the preview (by map player; empty: none). Paths below the installation.
/// </summary>
public sealed record CampaignMission(string Map, int Text, int Hint, string Voice, IReadOnlyList<string> Labels);

/// <summary>One people's campaign: its intro video, its missions in order and the video after the last one.</summary>
public sealed record CampaignPeople(string Intro, IReadOnlyList<CampaignMission> Missions, string Outro);

/// <summary>The campaigns of the original installation (<c>data/campaign.json</c>).</summary>
public sealed class CampaignConfig
{
    private CampaignConfig(IReadOnlyDictionary<string, IReadOnlyDictionary<Civilization, CampaignPeople>> campaigns) => Campaigns = campaigns;

    /// <summary>Per campaign (e.g. <c>settlers</c>) its peoples.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<Civilization, CampaignPeople>> Campaigns { get; }

    public static CampaignConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "campaign.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new CampaignConfig(document.RootElement.GetProperty("campaigns").EnumerateObject().ToDictionary(
            c => c.Name,
            c => (IReadOnlyDictionary<Civilization, CampaignPeople>)c.Value.GetProperty("peoples").EnumerateObject().ToDictionary(
                p => Enum.Parse<Civilization>(p.Name),
                p => new CampaignPeople(p.Value.GetProperty("intro").GetString()!,
                    p.Value.GetProperty("missions").EnumerateArray().Select(Mission).ToArray(),
                    p.Value.GetProperty("outro").GetString()!))));
    }

    private static CampaignMission Mission(JsonElement m) => new(
        m.GetProperty("map").GetString()!, m.GetProperty("text").GetInt32(), m.GetProperty("hint").GetInt32(),
        m.GetProperty("voice").GetString()!,
        m.TryGetProperty("labels", out var labels) ? labels.EnumerateArray().Select(l => l.GetString()!).ToArray() : []);
}
