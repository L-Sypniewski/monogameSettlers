# Agents

Project rules, layout and conventions live in [CLAUDE.md](CLAUDE.md) - read it first (it is in German; code and comments are English).

- This repository is a **fork** (upstream: `meue/monogameSettlers`, remote `upstream`). Agents push only to `origin` (`L-Sypniewski/monogameSettlers`). Upstream contributions happen ONLY via `/port-upstream <branch>`: it replays the branch's upstream-able commits onto `upstream/main`, pushes `port/<branch>` to origin and prints the compare URL; the human opens the upstream PR from the fork. Direct pushes to the upstream remote and `gh` commands against `meue/*` remain forbidden.
- Build and test: `scripts/fast-checks.sh` (`dotnet build Homestead.sln -c Release`, then `dotnet test`).
- macOS app bundles: ALWAYS `scripts/build-macos-app.sh` (self-contained publish + ad-hoc `codesign` + strict verify + quarantine strip + headless smoke). Never hand-roll packaging and never ship/download an unsigned or CI-artifact bundle: arm64 refuses to run unsigned binaries, and downloaded bundles get quarantinated by Gatekeeper (recipients strip once with `xattr -dr com.apple.quarantine monoSettlers.app`).
- Agent skills: curated .NET/C# set in `.agents/skills/` (`.claude/skills/` symlinks for Claude Code), pinned by `skills-lock.json` - update with `npx -y skills update`, drift-checked by CI (`bun run scripts/check-skills-lock.ts`).
- The shipflow pipeline (`ship` agent, `/ship-next`, `/fast-checks`, ...) is vendored in `.opencode/`, configured by `opencode.jsonc` and `.shipflow.json`; setup notes in [.devcontainer/README.md](.devcontainer/README.md).
