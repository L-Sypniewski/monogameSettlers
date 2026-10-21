using System.Text.Json;
using Homestead.Simulation.Buildings;

namespace Homestead.Client;

/// <summary>A finished picture of the original for the mission's preview: an image of a GFX file's settler sequence.</summary>
public sealed record PreviewImage(int File, int Sequence, int Frame);

/// <summary>
/// One mission: its map, its description and hints (entries of the text table in GFX file 07), the spoken briefing,
/// the players' short names on the rendered preview (by map player; empty: none) or a finished picture instead.
/// Paths below the installation; the Mission CD has no spoken briefings (null).
/// </summary>
public sealed record CampaignMission(string Map, int Text, int Hint, string? Voice, IReadOnlyList<string> Labels, PreviewImage? Preview);

/// <summary>
/// One side of a campaign (a people in the settlers' campaign; the Amazons or the three peoples in theirs): its name
/// (a text key), its intro video, its missions in order and the video after the last one (the Mission CD has none: null).
/// </summary>
public sealed record CampaignSide(string Name, string? Intro, IReadOnlyList<CampaignMission> Missions, string? Outro);

/// <summary>The campaigns of the original installation (<c>data/campaign.json</c>).</summary>
public sealed class CampaignConfig
{
    private CampaignConfig(IReadOnlyDictionary<string, IReadOnlyDictionary<string, CampaignSide>> campaigns, IReadOnlyDictionary<string, string> names)
    {
        Campaigns = campaigns;
        Names = names;
    }

    /// <summary>Per campaign (<c>settlers</c>, <c>missionCd</c>, <c>amazons</c>) its sides by id (e.g. <c>Roman</c>, <c>amazons</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, CampaignSide>> Campaigns { get; }

    /// <summary>Per campaign its name (a text key).</summary>
    public IReadOnlyDictionary<string, string> Names { get; }

    public static CampaignConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "campaign.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var campaigns = document.RootElement.GetProperty("campaigns").EnumerateObject().ToArray();
        return new CampaignConfig(campaigns.ToDictionary(
            c => c.Name,
            c => (IReadOnlyDictionary<string, CampaignSide>)c.Value.GetProperty("sides").EnumerateObject().ToDictionary(
                s => s.Name,
                s => new CampaignSide(s.Value.GetProperty("name").GetString()!, Optional(s.Value, "intro"),
                    s.Value.GetProperty("missions").EnumerateArray().Select(Mission).ToArray(),
                    Optional(s.Value, "outro")))),
            campaigns.ToDictionary(c => c.Name, c => c.Value.GetProperty("name").GetString()!));
    }

    private static string? Optional(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static CampaignMission Mission(JsonElement m) => new(
        m.GetProperty("map").GetString()!, m.GetProperty("text").GetInt32(), m.GetProperty("hint").GetInt32(),
        Optional(m, "voice"),
        m.TryGetProperty("labels", out var labels) ? labels.EnumerateArray().Select(l => l.GetString()!).ToArray() : [],
        m.TryGetProperty("preview", out var preview) ? new PreviewImage(preview[0].GetInt32(), preview[1].GetInt32(), preview[2].GetInt32()) : null);
}
