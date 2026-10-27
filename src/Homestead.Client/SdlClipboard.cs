using System.Runtime.InteropServices;

namespace Homestead.Client;

/// <summary>The system clipboard: MonoGame has none; DesktopGL runs on SDL2, which has one.</summary>
public static class SdlClipboard
{
    [DllImport("SDL2", EntryPoint = "SDL_GetClipboardText")]
    private static extern IntPtr SdlGetClipboardText();

    [DllImport("SDL2", EntryPoint = "SDL_free")]
    private static extern void SdlFree(IntPtr memory);

    static SdlClipboard()
    {
        NativeLibrary.SetDllImportResolver(typeof(SdlClipboard).Assembly, (name, assembly, path) =>
        {
            if (name != "SDL2")
                return IntPtr.Zero;
            foreach (string candidate in new[] { "libSDL2-2.0.so.0", "SDL2.dll", "libSDL2-2.0.0.dylib", "libSDL2.so" })
                if (NativeLibrary.TryLoad(candidate, assembly, path, out var handle))
                    return handle;
            return IntPtr.Zero;
        });
    }

    /// <summary>The text on the clipboard; null when there is none or SDL is not at hand.</summary>
    public static string? Text()
    {
        try
        {
            var text = SdlGetClipboardText();
            if (text == IntPtr.Zero)
                return null;
            string? value = Marshal.PtrToStringUTF8(text);
            SdlFree(text);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
