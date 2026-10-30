# Agents

Project rules, layout and conventions live in [CLAUDE.md](CLAUDE.md) - read it first (it is in German; code and comments are English).

- This repository is a **fork**. Push and open PRs/issues only against `L-Sypniewski/monogameSettlers`, never the upstream project.
- Build and test: `scripts/fast-checks.sh` (`dotnet build Homestead.sln -c Release`, then `dotnet test`).
- The shipflow pipeline (`ship` agent, `/ship-next`, `/fast-checks`, ...) is vendored in `.opencode/`, configured by `opencode.jsonc` and `.shipflow.json`; setup notes in [.devcontainer/README.md](.devcontainer/README.md).
