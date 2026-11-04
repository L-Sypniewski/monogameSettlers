#!/bin/sh
# Compiles the client's .fx shaders to .mgfx (OpenGL) with MonoGame's mgfxc, which runs the DirectX
# shader compiler through Wine. Needs a .NET 8 runtime, Wine and a prefix set up for MonoGame
# (MGFXC_WINE_PATH, default ~/.winemonogame). Inside the VS Code Flatpak it runs itself on the host.
set -e
if [ -f /.flatpak-info ]; then
    exec flatpak-spawn --host "$(realpath "$0")"
fi
cd "$(dirname "$0")/../src/Homestead.Client/Shaders"
# mgfxc ships as its own tool package since 3.8.6; dotnet-mgcb bundled it earlier. Both are looked up where
# dotnet tool installs put them.
mgfxc=$(ls -d "$HOME"/.dotnet/tools/.store/dotnet-mgfxc/*/dotnet-mgfxc/*/tools/net8.0/any/mgfxc.dll \
  "$HOME"/.nuget/packages/dotnet-mgfxc/*/tools/net8.0/any/mgfxc.dll \
  "$HOME"/.nuget/packages/dotnet-mgcb/*/tools/net8.0/any/mgfxc.dll 2>/dev/null | sort -V | tail -1)
if [ -z "$mgfxc" ]; then
    echo "mgfxc.dll not found (dotnet tool install --global dotnet-mgfxc)" >&2
    exit 1
fi
export MGFXC_WINE_PATH="${MGFXC_WINE_PATH:-$HOME/.winemonogame}"
run() {
    # On macOS the Windows dotnet that mgfxc drives through Wine dies on Console.OutputEncoding without a
    # terminal (dotnet/runtime#98441); a pseudo terminal around the call avoids it.
    if [ "$(uname)" = Darwin ] && command -v script >/dev/null; then
        script -q /dev/null "$@"
    else
        "$@"
    fi
}
for fx in *.fx; do
    run dotnet "$mgfxc" "$fx" "${fx%.fx}.mgfx" /Profile:OpenGL
done
