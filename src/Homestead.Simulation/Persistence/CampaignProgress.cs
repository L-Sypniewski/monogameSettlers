using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Persistence;

/// <summary>A campaign mission: the campaign (e.g. <c>settlers</c>), the people and its number from 0.</summary>
public sealed record MissionRef(string Campaign, Civilization People, int Index)
{
    /// <summary>As text, e.g. <c>settlers/Roman/0</c>.</summary>
    public override string ToString() => $"{Campaign}/{People}/{Index}";

    public static MissionRef? Parse(string? text) =>
        text?.Split('/') is [var campaign, var people, var index]
        && Enum.TryParse<Civilization>(people, out var civilization) && int.TryParse(index, out int number) && number >= 0
            ? new MissionRef(campaign, civilization, number)
            : null;
}

/// <summary>
/// The campaigns' progress across games (the global save game): per campaign and people the number of missions
/// accomplished, in <c>campaign.json</c> next to the save slots (<see cref="SaveSlots.Directory"/>).
/// </summary>
public sealed class CampaignProgress
{
    private readonly Dictionary<string, Dictionary<Civilization, int>> _accomplished = new();

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
                        foreach (var (people, count) in entries)
                            if (Enum.TryParse<Civilization>(people, out var civilization) && count is JsonValue value && value.TryGetValue(out int number))
                                progress.Set(campaign, civilization, number);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // No progress yet.
        }
        return progress;
    }

    /// <summary>Missions of <paramref name="people"/>'s campaign accomplished so far (the next one's number).</summary>
    public int Accomplished(string campaign, Civilization people) =>
        _accomplished.TryGetValue(campaign, out var peoples) ? peoples.GetValueOrDefault(people) : 0;

    /// <summary>Notes the mission as accomplished (and every one before it); an earlier one changes nothing.</summary>
    public void Accomplish(MissionRef mission) =>
        Set(mission.Campaign, mission.People, Math.Max(Accomplished(mission.Campaign, mission.People), mission.Index + 1));

    private void Set(string campaign, Civilization people, int count)
    {
        if (!_accomplished.TryGetValue(campaign, out var peoples))
            _accomplished[campaign] = peoples = new();
        peoples[people] = Math.Max(0, count);
    }

    /// <summary>Writes the progress (over a temporary file, so a failure never spoils it).</summary>
    public void Save()
    {
        var root = new JsonObject();
        foreach (var (campaign, peoples) in _accomplished)
        {
            var entries = new JsonObject();
            foreach (var (people, count) in peoples)
                entries[people.ToString()] = count;
            root[campaign] = entries;
        }
        Directory.CreateDirectory(SaveSlots.Directory);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
