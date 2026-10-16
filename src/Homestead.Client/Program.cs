using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Client;
using Homestead.Simulation.Buildings;

// Options:
//   --scene map|walk   map: terrain of an original map (default), walk: walking PoC on plain grass
//   --map <name>       map to open (file name without extension; random[:players[:none|mirror|repeat[:seed]]] for a
//                      random map); without it (and --capture) the start screen
//                      offers map, peoples and colors
//   --capture <dir>    render offscreen, save frames as PNG and quit (for checks without looking)
//   --peoples <list>   peoples for players the map leaves open, e.g. amazon,roman (player i: entry i mod count)
//   --load <slot>      load the saved game of this slot (1-8) instead of a new game
//   --characters <list> AI characters per player (ai.json characters), e.g. ,general,standard: entry i for
//                      player i+1, empty or missing: drawn by the people's weights
string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var installation = GameInstallation.Locate();
var gfx = new GfxDirectory(installation.GfxDirectory);
string? capture = Option("--capture");
if (capture is not null)
    Directory.CreateDirectory(capture);

using Microsoft.Xna.Framework.Game game = Option("--scene") switch
{
    "walk" => new WalkPocGame(gfx, capture),
    _ => new MapGame(installation, gfx, Option("--map"), capture,
        Option("--peoples")?.Split(',').Select(p => Enum.Parse<Civilization>(p.Trim(), ignoreCase: true)).ToArray(),
        Option("--characters")?.Split(',').Select(c => c.Trim()).ToArray(),
        startScreen: (capture is null || Environment.GetEnvironmentVariable("HOMESTEAD_CAPTURE_START") is not null) && Option("--map") is null
                     && Option("--load") is null,
        loadSlot: int.TryParse(Option("--load"), out int slot) ? slot - 1 : null),
};
game.Run();
