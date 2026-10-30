# Devcontainer

Ubuntu 24.04 with [mise](https://mise.jdx.dev) (versions in `.mise.toml`: .NET 9, Node 24, bun), `gh`, Claude Code and opencode (installed on create).
It builds and tests the simulation headless; running the game needs a desktop and an installation of The Settlers III, so do that on the host.
On the host, `mise install` gives the same toolchain.

## Prerequisites

Export these in the host shell **before** opening the container (they are forwarded through `remoteEnv`):

| Variable | Used for |
|---|---|
| `GH_TOKEN` | `gh` and git over HTTPS (`gh auth token` on the host) |
| `CLAUDE_CODE_OAUTH_TOKEN` | Claude Code login inside the container (`claude setup-token` on the host) |

`GH_REPO` is pinned to `L-Sypniewski/monogameSettlers` in `containerEnv`: this repo is a fork, and `gh` would otherwise
default PRs and issues to the upstream project. Nothing in the pipeline may push to or open anything in upstream.
On the host, run `gh repo set-default L-Sypniewski/monogameSettlers` once per clone for the same effect.

## What the container covers

- `dotnet build Homestead.sln` and `dotnet test`: the test project references only `Homestead.Simulation` and `Homestead.Assets`, so no display or native game libraries are needed. MinVer reads the git history, so keep the full clone.
- Shaders: the compiled `.mgfx` files are committed, so Wine and `tools/compile-shaders.sh` are not needed unless you edit a `.fx` file (do that on the host).
- Original game installation: set `HOMESTEAD_GAME_DIR` once on the host (e.g. `export HOMESTEAD_GAME_DIR=~/Games/Settlers3` in `~/.zshrc`).
  The game reads the same variable, so a built game on the host and the container share one path. On start the container mounts that folder
  read-only at `/mnt/settlers3` (`initializeCommand` links it to `.devcontainer/game-dir`) and `link-game-dir.sh` writes it as `gameDirectory` into the container's settings.
  Without the variable the mount is an empty folder and the tests that need the original files pass unchecked; with it they run for real.
  The variable has to be visible to whatever starts the container (a terminal, or an IDE launched from one). A game started from the Dock or Finder does not see shell variables:
  there the installation dialog stores the path in `~/.config/homestead/settings.json` instead.
- Running the game (DesktopGL) needs a desktop: use the host.

## Shipflow

The agent pipeline is vendored in the repo: the plugin (pipeline tools, guards, worktree strategy) in `.opencode/shipflow/`, its options in `opencode.jsonc`,
agents/commands/rules in `.opencode/`, skills in `.agents/skills/`, Claude Code wrappers in `.claude/scripts/`, repo policy in `.shipflow.json`.
Its only dependency is installed by `bash .opencode/scripts/ensure-shipflow-deps.sh` (run by `post-create.sh`).
`scripts/fast-checks.sh` (build + tests) is the drift gate shipflow runs between edits.
