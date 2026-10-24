using System.Diagnostics;
using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Assets.Maps;
using Homestead.AssetTool;

// Usage:
//   AssetTool scan                                   decode every image of every GFX file, report errors
//   AssetTool export <file> <kind> <index> <outDir>  write PNGs (kind: settler|torso|shadow|landscape|gui)
//   AssetTool sheet <file> <kind> <out.png>          contact sheet of the first frame of every entry
//   AssetTool frames <file> <kind> <first> <last> <out.png>  every frame of those sequences, a row each
//   AssetTool maps                                   parse every map, report sizes and unknown landscape values
//   AssetTool resources                              resource byte statistics (type nibble per landscape group)
//   AssetTool minimap <map name> <out.png>           landscape overview of one map

var installation = GameInstallation.Locate();
var gfx = new GfxDirectory(installation.GfxDirectory);

switch (args.FirstOrDefault())
{
    case "scan":
        Scan(gfx);
        break;
    case "export" when args.Length == 5:
        Export(gfx.Open(int.Parse(args[1])), args[2], int.Parse(args[3]), args[4]);
        break;
    case "sheet" when args.Length == 4:
        ContactSheet.Write(gfx.Open(int.Parse(args[1])), args[2], args[3]);
        break;
    case "frames" when args.Length == 6:
        ContactSheet.WriteFrames(gfx.Open(int.Parse(args[1])), args[2], int.Parse(args[3]), int.Parse(args[4]), args[5]);
        break;
    case "maps":
        MapReport.Scan(installation);
        break;
    case "resources":
        MapReport.Resources(installation);
        break;
    case "minimap" when args.Length == 3:
        MapReport.Minimap(installation, args[1], args[2]);
        break;
    default:
        Console.Error.WriteLine("Usage: scan | export <file> <kind> <index> <outDir> | sheet <file> <kind> <out.png> | maps | minimap <map> <out.png>");
        return 1;
}
return 0;

static void Scan(GfxDirectory gfx)
{
    var watch = Stopwatch.StartNew();
    int totalImages = 0, totalErrors = 0;
    foreach (int number in gfx.FileNumbers)
    {
        var file = gfx.Open(number);
        int images = 0;
        var errors = new List<string>();

        void Try(string what, Func<GfxBitmap> read)
        {
            try { read(); images++; }
            catch (Exception e) { errors.Add($"{what}: {e.Message}"); }
        }

        for (int s = 0; s < file.SettlerCount; s++)
        for (int f = 0; f < file.GetSettlerFrameCount(s); f++)
            Try($"settler {s}/{f}", () => file.ReadSettler(s, f));
        for (int s = 0; s < file.TorsoCount; s++)
        for (int f = 0; f < file.GetTorsoFrameCount(s); f++)
            Try($"torso {s}/{f}", () => file.ReadTorso(s, f));
        for (int s = 0; s < file.ShadowCount; s++)
        for (int f = 0; f < file.GetShadowFrameCount(s); f++)
            Try($"shadow {s}/{f}", () => file.ReadShadow(s, f));
        for (int i = 0; i < file.LandscapeCount; i++)
            Try($"landscape {i}", () => file.ReadLandscape(i));
        for (int i = 0; i < file.GuiCount; i++)
            Try($"gui {i}", () => file.ReadGui(i));

        string other = string.Join(" ", file.Indices
            .Where(kv => kv.Key is GfxIndexType.Animation or GfxIndexType.Palette || !Enum.IsDefined(kv.Key))
            .Select(kv => $"{kv.Key}={kv.Value.Length}"));
        Console.WriteLine($"{number,2}: {file.ColorFormat} settler={file.SettlerCount} torso={file.TorsoCount} " +
                          $"shadow={file.ShadowCount} landscape={file.LandscapeCount} gui={file.GuiCount} {other} " +
                          $"-> {images} images, {errors.Count} errors");
        foreach (string e in errors.Take(3))
            Console.WriteLine($"      {e}");

        totalImages += images;
        totalErrors += errors.Count;
    }
    Console.WriteLine($"Total: {totalImages} images, {totalErrors} errors, {watch.ElapsedMilliseconds} ms");
}

static void Export(GfxFile file, string kind, int index, string outDir)
{
    Directory.CreateDirectory(outDir);
    foreach (var (name, bitmap) in ContactSheet.Frames(file, kind, index))
        Png.Write(Path.Combine(outDir, $"{kind}_{index}_{name}.png"), bitmap);
}
