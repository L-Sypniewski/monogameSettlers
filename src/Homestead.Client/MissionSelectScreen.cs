using Homestead.Assets.Gfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The campaign's missions of one side once one is accomplished, as a <see cref="ListScreen"/>: first the intro video,
/// then every accomplished mission and the next, with their names from the mission texts, to play again or on, after the
/// last one the outro video (the videos only where the side has them). It opens at the newest mission.
/// A click chooses (<see cref="Chosen"/>, <see cref="ChosenVideo"/>); "Zurück" (Esc) goes back to the choice of side.
/// </summary>
public sealed class MissionSelectScreen : IDisposable
{
    /// <summary>A video of the list.</summary>
    public enum Video { Intro, Outro }

    private readonly ListScreen _list;
    /// <summary>What each row stands for.</summary>
    private readonly (int? Mission, Video? Video)[] _rows;

    /// <param name="names">The names of all the campaign's missions in order.</param>
    /// <param name="accomplished">Missions accomplished so far: those and the next one can be chosen.</param>
    /// <param name="side">The side's name for the heading.</param>
    /// <param name="intro">Whether the side has an intro video, <paramref name="outro"/> an outro.</param>
    public MissionSelectScreen(GraphicsDevice device, GfxDirectory gfx, string side, IReadOnlyList<string> names, int accomplished,
        bool intro = true, bool outro = true)
    {
        var rows = new List<(int?, Video?)>();
        if (intro)
            rows.Add((null, Video.Intro));
        for (int mission = 0; mission < Math.Min(names.Count, accomplished + 1); mission++)
            rows.Add((mission, null));
        if (outro && accomplished >= names.Count)
            rows.Add((null, Video.Outro));
        _rows = rows.ToArray();
        _list = new ListScreen(device, gfx, $"{side} · {Lang.T("campaign.chooseMission")}", _rows.Select(r => r.Mission is { } m
                ? new ListRow($"{m + 1}.  {names[m]}", Lang.T(m < accomplished ? "campaign.missionDone" : "campaign.missionNew"), Lit: m >= accomplished)
                : new ListRow(Lang.T(r.Video == Video.Intro ? "campaign.introVideo" : "campaign.outroVideo"), Lang.T("campaign.video")))
            .ToArray(), shown: _rows.Length - 1);
    }

    /// <summary>The mission (from 0) clicked in the last <see cref="Update"/>, if any.</summary>
    public int? Chosen { get; private set; }

    /// <summary>The video clicked in the last <see cref="Update"/>, if any.</summary>
    public Video? ChosenVideo { get; private set; }

    /// <summary>A mission's name from its text: the first line without its label ("MISSIONSNAME:", "MISSION NAME:").</summary>
    public static string NameOf(string text)
    {
        string line = text.Replace("\r", "").Split('\n')[0];
        int colon = line.IndexOf(':');
        return (colon >= 0 ? line[(colon + 1)..] : line).Trim();
    }

    /// <summary>One frame; true: back to the choice of side. A chosen mission is in <see cref="Chosen"/>.</summary>
    public bool Update(Rectangle view)
    {
        bool back = _list.Update(view);
        (Chosen, ChosenVideo) = _list.Chosen is { } row ? _rows[row] : (null, null);
        return back;
    }

    /// <summary>Back from a mission's description: keys and buttons still held there do not count here.</summary>
    public void Resume() => _list.Resume();

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle view) => _list.Draw(batch, pixel, font, view);

    public void Dispose() => _list.Dispose();
}
