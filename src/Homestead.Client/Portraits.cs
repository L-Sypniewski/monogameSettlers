using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The people's unit portraits in their stone frames (GUI images of its file, indices in <c>data/ui.json</c>
/// <c>portraits</c>), by animation type (<see cref="SettlerRenderer.TypeOf"/>); types without one fall back
/// to the standing figure.
/// </summary>
public sealed class Portraits : IDisposable
{
    private static readonly string[] SoldierTypes = ["SWORDSMAN", "PIKEMAN", "BOWMAN"];

    private readonly Dictionary<string, Texture2D> _images = new();

    public Portraits(GraphicsDevice device, GfxDirectory gfx, Civilization people)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        if (!document.RootElement.TryGetProperty("portraits", out var all) || !all.TryGetProperty(people.ToString(), out var own))
            return;
        var file = gfx.Open(own.GetProperty("file").GetInt32());
        void Load(string type, int index)
        {
            var bitmap = file.ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            _images[type] = texture;
        }
        foreach (var entry in own.EnumerateObject())
            if (entry.Name.ToUpperInvariant() == entry.Name)
                Load(entry.Name, entry.Value.GetInt32());
        int first = own.GetProperty("soldiers").GetInt32();
        for (int level = 1; level <= 3; level++)
        for (int t = 0; t < SoldierTypes.Length; t++)
            Load($"{SoldierTypes[t]}_L{level}", first + 9 * (level - 1) + 3 * t);
    }

    /// <summary>The size of a portrait (all the same).</summary>
    public static readonly Point Size = new(40, 60);

    /// <summary>The type's portrait centered in the box, or its standing figure if it has none.</summary>
    public void Draw(SpriteBatch batch, string type, Rectangle box, SettlerRenderer settlers, Civilization people, Color playerColor)
    {
        if (_images.TryGetValue(type, out var image))
            batch.Draw(image, new Vector2(box.Center.X - image.Width / 2, box.Center.Y - image.Height / 2), Color.White);
        else
            settlers.DrawIcon(batch, people, type, box, 2f, playerColor);
    }

    public void Dispose()
    {
        foreach (var texture in _images.Values)
            texture.Dispose();
    }
}
