---
description: Port a fork branch's upstream-able commits to a clean branch on origin, ready for an upstream PR
---

Port the upstream-able commits of `$1` (source branch) onto `upstream/main` and push the result as `port/$1` to origin:

Run `bash .opencode/scripts/port-upstream.sh $1` (optional second argument: port branch name; `--force` re-ports by deleting and recreating the remote port branch).

- On a hard fail, follow the script's printed recovery: a mixed commit (shared + fork-only files) must be split first, a cherry-pick conflict must be resolved (`git add` + `git cherry-pick --continue`) and the script re-run with `--force`. Never force-push anything but the port branch to origin, never push to `upstream`.
- On success, report the ported and skipped commits, confirm the script's path and marker asserts passed, and present the compare URL and the printed `gh pr create` command to the human.
- STOP there — never create the upstream PR, never run `gh` against `meue/*`, never push to the upstream remote. Upstream PR creation is a human step by design.
