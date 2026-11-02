# Agents

Project rules, layout and conventions live in [CLAUDE.md](CLAUDE.md) - read it first (it is in German; code and comments are English).

- This repository is a **fork** (upstream: `meue/monogameSettlers`, remote `upstream`). Agents push only to `origin` (`L-Sypniewski/monogameSettlers`). Upstream contributions happen ONLY via `/port-upstream <branch>`: it replays the branch's upstream-able commits onto `upstream/main`, pushes `port/<branch>` to origin and prints the compare URL; the human opens the upstream PR from the fork. Direct pushes to the upstream remote and `gh` commands against `meue/*` remain forbidden.
- Build and test: `scripts/fast-checks.sh` (`dotnet build Homestead.sln -c Release`, then `dotnet test`).
- Agent skills: curated .NET/C# set in `.agents/skills/` (`.claude/skills/` symlinks for Claude Code), pinned by `skills-lock.json` - update with `npx -y skills update`, drift-checked by CI (`bun run scripts/check-skills-lock.ts`).
- The shipflow pipeline (`ship` agent, `/ship-next`, `/fast-checks`, ...) is vendored in `.opencode/`, configured by `opencode.jsonc` and `.shipflow.json`; setup notes in [.devcontainer/README.md](.devcontainer/README.md).
