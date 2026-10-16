#!/bin/sh
# Compiles the client's .fx shaders to .mgfx (OpenGL) with MonoGame's mgfxc, which runs the DirectX
# shader compiler through Wine. Needs a .NET 8 runtime, Wine and a prefix set up for MonoGame
# (MGFXC_WINE_PATH, default ~/.winemonogame). Inside the VS Code Flatpak it runs itself on the host.
set -e
if [ -f /.flatpak-info ]; then
    exec flatpak-spawn --host "$(realpath "$0")"
fi
cd "$(dirname "$0")/../src/Homestead.Client/Shaders"
mgfxc=$(ls -d "$HOME"/.nuget/packages/dotnet-mgcb/*/tools/net8.0/any/mgfxc.dll | sort -V | tail -1)
export MGFXC_WINE_PATH="${MGFXC_WINE_PATH:-$HOME/.winemonogame}"
for fx in *.fx; do
    dotnet "$mgfxc" "$fx" "${fx%.fx}.mgfx" /Profile:OpenGL
done
