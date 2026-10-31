#!/usr/bin/env bash
# Points the game's settings at the mounted installation (/mnt/settlers3) when the host provided one.
# Not a containerEnv variable: a configured but invalid path fails the installation tests, while no path passes them unchecked.
set -u

settings="/home/vscode/.config/homestead/settings.json"
if [ -n "$(ls -A /mnt/settlers3 2>/dev/null)" ]; then
  mkdir -p "$(dirname "$settings")"
  printf '{ "gameDirectory": "/mnt/settlers3" }\n' > "$settings"
else
  rm -f "$settings"
fi
