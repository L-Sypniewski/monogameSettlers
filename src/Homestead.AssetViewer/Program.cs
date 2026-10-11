using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.AssetViewer;

// Optional first argument: game directory (overrides environment variable and settings file).
var installation = GameInstallation.Locate(args.FirstOrDefault());
using var game = new ViewerGame(new GfxDirectory(installation.GfxDirectory));
game.Run();
