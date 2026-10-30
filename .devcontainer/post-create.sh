#!/usr/bin/env bash
# Hoisted from devcontainer.json postCreateCommand: a script keeps shell quoting native.

export PATH="/mnt/mise-data/shims:$PATH"
mise trust "$(pwd)/.mise.toml" >/dev/null 2>&1 || true
sudo chown -R vscode:vscode /home/vscode/.local

# The game itself needs a desktop and the original assets; the container builds and tests the simulation headless.
{ dotnet restore Homestead.sln || echo 'WARN: dotnet restore failed - rerun it in the container'; }

# Vendored shipflow plugin dependencies (.opencode/shipflow).
{ bash .opencode/scripts/ensure-shipflow-deps.sh || echo 'WARN: shipflow dependency install failed - rerun .opencode/scripts/ensure-shipflow-deps.sh'; }

echo 'eval "$(mise activate zsh)"' >> /home/vscode/.zshrc
echo 'eval "$(mise activate bash)"' >> /home/vscode/.bashrc
