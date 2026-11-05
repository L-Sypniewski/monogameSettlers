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

    public Portraits(GraphicsDevice device, GfxDirectory gfx, Civilization people, UiLayout ui)
    {
        if (!ui.Portraits.Peoples.TryGetValue(people.ToString(), out var own))
            return;
        var file = gfx.Open(own.File);
        void Load(string type, int index, GfxFile? from = null)
        {
            var bitmap = (from ?? file).ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            _images[type] = texture;
        }
        foreach (var (type, index) in own.Types)
            Load(type, index);
        if (ui.Portraits.Common is { } common)
        {
            var commonFile = gfx.Open(common.File);
            foreach (var (type, image) in common.Entries)
                Load(type, image.Index, image.File == common.File ? commonFile : gfx.Open(image.File));
        }
        int first = own.Soldiers;
        for (int level = 1; level <= 3; level++)
        for (int t = 0; t < SoldierTypes.Length; t++)
            Load($"{SoldierTypes[t]}_L{level}", first + 9 * (level - 1) + 3 * t);
    }

    /// <summary>The size of a portrait (all the same but the war machine's).</summary>
    public static readonly Point Size = new(40, 60);

    /// <summary>The size of the type's portrait image (<see cref="Size"/> for those drawn in the empty frame).</summary>
    public Point SizeOf(string type) => _images.TryGetValue(type, out var image) ? new Point(image.Width, image.Height) : Size;

    /// <summary>
    /// The type's portrait centered in the box (<paramref name="scale"/> times its size), or its standing figure
    /// (in an empty frame, if there is one) if it has none.
    /// </summary>
    public void Draw(SpriteBatch batch, string type, Rectangle box, SettlerRenderer settlers, Civilization people, Color playerColor,
        float scale = 1)
    {
        if (_images.TryGetValue(type, out var image))
        {
            batch.Draw(image, new Vector2(box.Center.X - image.Width * scale / 2, box.Center.Y - image.Height * scale / 2), null, Color.White,
                0, Vector2.Zero, scale, SpriteEffects.None, 0);
            return;
        }
        if (_images.TryGetValue("FRAME", out var frame))
        {
            var at = new Vector2(box.Center.X - frame.Width * scale / 2, box.Center.Y - frame.Height * scale / 2);
            batch.Draw(frame, at, null, Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
            int border = (int)MathF.Round(4 * scale);
            box = new Rectangle((int)at.X + border, (int)at.Y + border, (int)(frame.Width * scale) - 2 * border, (int)(frame.Height * scale) - 2 * border);
        }
        settlers.DrawIcon(batch, people, type, box, 2f * scale, playerColor);
    }

    public void Dispose()
    {
        foreach (var texture in _images.Values)
            texture.Dispose();
    }
}
