using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Client;

// Options: --capture <dir>  render offscreen, save frames as PNG and quit (for checks without looking)
string? captureDirectory = null;
int captureIndex = Array.IndexOf(args, "--capture");
if (captureIndex >= 0 && captureIndex + 1 < args.Length)
    captureDirectory = args[captureIndex + 1];

var installation = GameInstallation.Locate();
using var game = new WalkPocGame(new GfxDirectory(installation.GfxDirectory), captureDirectory);
game.Run();
