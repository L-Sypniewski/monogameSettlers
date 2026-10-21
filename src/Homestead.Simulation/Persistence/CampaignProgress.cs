using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homestead.Simulation.Persistence;

/// <summary>
/// A campaign mission: the campaign (e.g. <c>settlers</c>, <c>amazons</c>), the side played (a people in the settlers'
/// campaign, e.g. <c>Roman</c>; <c>amazons</c> or <c>peoples</c> in the Amazons') and its number from 0.
/// </summary>
public sealed record MissionRef(string Campaign, string Side, int Index)
{
    /// <summary>As text, e.g. <c>settlers/Roman/0</c>.</summary>
    public override string ToString() => $"{Campaign}/{Side}/{Index}";

    public static MissionRef? Parse(string? text) =>
        text?.Split('/') is [{ Length: > 0 } campaign, { Length: > 0 } side, var index] && int.TryParse(index, out int number) && number >= 0
            ? new MissionRef(campaign, side, number)
            : null;
}

/// <summary>
/// The campaigns' progress across games (the global save game): per campaign and side the number of missions
/// accomplished, in <c>campaign.json</c> next to the save slots (<see cref="SaveSlots.Directory"/>).
/// </summary>
public sealed class CampaignProgress
{
    private readonly Dictionary<string, Dictionary<string, int>> _accomplished = new();

    public static string FilePath => Path.Combine(SaveSlots.Directory, "campaign.json");

    /// <summary>The stored progress; none if the file is missing or unreadable.</summary>
    public static CampaignProgress Load()
    {
        var progress = new CampaignProgress();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject campaigns)
                foreach (var (campaign, peoples) in campaigns)
                    if (peoples is JsonObject entries)
                        foreach (var (side, count) in entries)
                            if (count is JsonValue value && value.TryGetValue(out int number))
                                progress.Set(campaign, side, number);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // No progress yet.
        }
        return progress;
    }

    /// <summary>Missions of the campaign's <paramref name="side"/> accomplished so far (the next one's number).</summary>
    public int Accomplished(string campaign, string side) =>
        _accomplished.TryGetValue(campaign, out var sides) ? sides.GetValueOrDefault(side) : 0;

    /// <summary>Notes the mission as accomplished (and every one before it); an earlier one changes nothing.</summary>
    public void Accomplish(MissionRef mission) =>
        Set(mission.Campaign, mission.Side, Math.Max(Accomplished(mission.Campaign, mission.Side), mission.Index + 1));

    private void Set(string campaign, string side, int count)
    {
        if (!_accomplished.TryGetValue(campaign, out var sides))
            _accomplished[campaign] = sides = new();
        sides[side] = Math.Max(0, count);
    }

    /// <summary>Writes the progress (over a temporary file, so a failure never spoils it).</summary>
    public void Save()
    {
        var root = new JsonObject();
        foreach (var (campaign, sides) in _accomplished)
        {
            var entries = new JsonObject();
            foreach (var (side, count) in sides)
                entries[side] = count;
            root[campaign] = entries;
        }
        Directory.CreateDirectory(SaveSlots.Directory);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
