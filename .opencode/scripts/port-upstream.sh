#!/usr/bin/env bash
# Port the upstream-able commits of a fork branch: replay them onto upstream/main, verify the
# result contains no fork-only paths or content, push it as port/<branch> to origin, and print
# the compare URL + human gh command for the upstream PR. Re-runs RESUME an existing port
# branch (already-applied commits are detected by patch-id and kept; --force re-ports fresh).
# Recovery from a hard fail is manual by design - a contaminated port branch is worse than a
# stopped script.
set -euo pipefail

EXCLUDE="$(cd "$(dirname "$0")" && pwd)/port-upstream.exclude"
UPSTREAM_MAIN=upstream/main
FORK_REMOTE=origin
PORT_PREFIX=L-Sypniewski

die() { echo "port-upstream: FAIL: $*" >&2; exit 1; }
log() { echo "port-upstream: $*"; }

# --- args --------------------------------------------------------------------
SOURCE=""
PORT=""
FORCE=false
for arg in "$@"; do
  case "$arg" in
    --force) FORCE=true ;;
    -h|--help) echo "usage: $0 <source-branch> [port-branch-name] [--force]"; exit 0 ;;
    *) if [ -z "$SOURCE" ]; then SOURCE="$arg"
       elif [ -z "$PORT" ]; then PORT="$arg"
       else die "unexpected argument: $arg"; fi ;;
  esac
done
[ -n "$SOURCE" ] || { echo "usage: $0 <source-branch> [port-branch-name] [--force]" >&2; exit 1; }
[ -n "$PORT" ] || PORT="port/$(basename "$SOURCE")"
[ "$PORT" != "$SOURCE" ] || die "port branch name must differ from the source branch"

# the port gate builds Homestead.sln, which is resolved relative to the repo root
cd "$(git rev-parse --show-toplevel)"

# --- fork-only exclusion data: paths (skip commits, reject mixes), owned shared files
# (their entire fork delta is fork-only content) + content markers
[ -f "$EXCLUDE" ] || die "missing exclusion config: $EXCLUDE"
FORK_PATHS=()
OWNED=()
MARKERS=()
while read -r line; do
  case "$line" in
    ''|'#'*) continue ;;
    marker\ *)
      read -r -a words <<< "$line"; mfile="${words[1]}"
      MARKERS+=("$mfile"$'\t'"${line#marker "$mfile" }")
      ;;
    owned\ *)
      read -r -a words <<< "$line"; OWNED+=("${words[1]}")
      ;;
    *) FORK_PATHS+=("$line") ;;
  esac
done < "$EXCLUDE"

# Directory entries (trailing /) match by prefix, file entries exactly.
is_fork_only() {
  local f="$1" entry
  for entry in "${FORK_PATHS[@]}"; do
    case "$entry" in
      */) [[ "$f" == "$entry"* ]] && return 0 ;;
      *)  [[ "$f" == "$entry" ]] && return 0 ;;
    esac
  done
  return 1
}

is_owned_shared() {
  local f="$1" entry
  for entry in "${OWNED[@]}"; do [[ "$f" == "$entry" ]] && return 0; done
  return 1
}

# --- 1. fetch both remotes (tags included: MinVer reads them) ------------------
log "fetching upstream (with tags) and origin"
git fetch upstream --tags
git fetch origin
git rev-parse --verify -q "$UPSTREAM_MAIN" >/dev/null || die "$UPSTREAM_MAIN not found after fetch"
git rev-parse --verify -q "$SOURCE" >/dev/null || die "source branch not found: $SOURCE"
if ! { git diff --quiet && git diff --cached --quiet; }; then
  if [ -f "$(git rev-parse --git-dir)/CHERRY_PICK_HEAD" ]; then
    die "working tree not clean - an interrupted cherry-pick is in progress.
Finish it (resolve, 'git add' the resolutions, 'git cherry-pick --continue') or
'git cherry-pick --abort' it, then re-run WITHOUT --force: picks already on the
port branch are kept and the run resumes at the first unapplied commit."
  fi
  die "working tree not clean - commit or stash first"
fi

# --- 2. port-branch state: resume by default, --force = fresh re-port ---------------
if [ "$FORCE" = true ] && git ls-remote --heads "$FORK_REMOTE" "refs/heads/$PORT" | grep -q .; then
  log "--force: deleting existing $FORK_REMOTE/$PORT and re-porting fresh"
  git push "$FORK_REMOTE" --delete "$PORT"
fi
if ! git show-ref --verify --quiet "refs/heads/$PORT" &&
   git ls-remote --heads "$FORK_REMOTE" "refs/heads/$PORT" | grep -q .; then
  log "no local $PORT - seeding it from $FORK_REMOTE/$PORT"
  git branch "$PORT" "$FORK_REMOTE/$PORT"
fi
RESUME=false
if [ "$FORCE" != true ] && git show-ref --verify --quiet "refs/heads/$PORT"; then
  if [ "$(git rev-parse "$PORT")" != "$(git rev-parse "$UPSTREAM_MAIN")" ]; then
    if git merge-base --is-ancestor "$UPSTREAM_MAIN" "$PORT"; then
      RESUME=true
      log "resuming $PORT: commits already on it are kept"
    else
      die "$PORT does not descend from the current $UPSTREAM_MAIN (upstream moved
since $PORT was created). Re-run with --force for a fresh port - the resolutions
on $PORT will be discarded."
    fi
  fi
fi

# --- 3+4. classify every commit: fork-only (skip) / mixed (hard fail) / shared (port)
CANDIDATES=$(git rev-list --reverse --no-merges "$UPSTREAM_MAIN..$SOURCE")
[ -n "$CANDIDATES" ] || die "no commits on $SOURCE that are not already in $UPSTREAM_MAIN"
PORTED_SHAS=()
PORTED_LOG=()
SKIPPED_LOG=()
for sha in $CANDIDATES; do
  subject=$(git log -1 --format=%s "$sha")
  shared=""; forkonly=""; unowned=""
  while IFS= read -r f; do
    if is_fork_only "$f"; then forkonly+="$f"$'\n'
    elif is_owned_shared "$f"; then shared+="$f"$'\n'
    else shared+="$f"$'\n'; unowned+="$f"$'\n'; fi
  done < <(git diff-tree --no-commit-id --name-only -r "$sha")
  if [ -n "$forkonly" ] && [ -n "$unowned" ]; then
    die "commit ${sha:0:9} ($subject) mixes fork-only and shared paths - split the commit, then re-run.
fork-only files:
$forkonly
shared files:
$shared"
  elif [ -n "$unowned" ]; then
    log "port commit ${sha:0:9} $subject"
    PORTED_SHAS+=("$sha")
    PORTED_LOG+=("${sha:0:9} $subject")
  else
    log "skip tooling commit ${sha:0:9} $subject"
    SKIPPED_LOG+=("${sha:0:9} $subject")
  fi
done
[ ${#PORTED_SHAS[@]} -gt 0 ] || die "nothing upstream-able on $SOURCE (every commit is fork tooling)"

# --- 5. replay: fresh from upstream/main, or resume appending the missing tail -----
if [ "$RESUME" = true ]; then
  git checkout "$PORT"
  cherry_status=$(git cherry "$PORT" "$SOURCE" "$UPSTREAM_MAIN")
  PICK_SHAS=()
  for sha in "${PORTED_SHAS[@]}"; do
    if printf '%s\n' "$cherry_status" | grep -q "^- $sha$"; then
      log "already applied on $PORT: ${sha:0:9} $(git log -1 --format=%s "$sha")"
    else
      PICK_SHAS+=("$sha")
    fi
  done
else
  log "creating $PORT from $UPSTREAM_MAIN"
  git checkout -B "$PORT" "$UPSTREAM_MAIN"
  PICK_SHAS=("${PORTED_SHAS[@]}")
fi
if [ ${#PICK_SHAS[@]} -gt 0 ]; then
  for sha in "${PICK_SHAS[@]}"; do
    git cherry-pick "$sha" || die "cherry-pick of ${sha:0:9} conflicted (worktree left on $PORT mid-cherry-pick).
Recovery: resolve the conflicts, 'git add' the resolutions, 'git cherry-pick --continue'
(or 'git cherry-pick --abort' to discard the pick), then re-run this script WITHOUT
--force: picks already on $PORT are kept and the run resumes at the first unapplied commit."
    log "cherry-picked ${sha:0:9}"
  done
else
  log "nothing new to cherry-pick - $PORT already carries every upstream-able commit"
fi

# --- 6. assert A: port branch diff contains no fork-only path ------------------
leaked=""
while IFS= read -r f; do
  is_fork_only "$f" && leaked+="  $f"$'\n'
done < <(git diff --name-only "$UPSTREAM_MAIN..HEAD")
[ -z "$leaked" ] || die "assert A failed - fork-only paths on the port branch:
$leaked"

# --- 7. assert B: no fork-only content marker in the ported shared files -------
for m in "${MARKERS[@]}"; do
  mfile="${m%%$'\t'*}"; mstring="${m#*$'\t'}"
  if git grep -F -q "$mstring" HEAD -- "$mfile" 2>/dev/null; then
    die "assert B failed - fork-only marker '$mstring' present in $mfile on the port branch"
  fi
done
log "asserts passed: no fork-only paths, no fork-only content markers"

# --- 8. build + test on the port branch (direct dotnet: fast-checks.sh is fork-only)
log "building and testing $PORT"
if command -v mise >/dev/null 2>&1; then
  mise exec -- dotnet build Homestead.sln -c Release --nologo -v q
  mise exec -- dotnet test Homestead.sln -c Release --no-build --nologo
else
  echo "port-upstream: WARNING: mise not on PATH - building with an SDK the repo does not pin (.mise.toml pins dotnet 9)" >&2
  dotnet build Homestead.sln -c Release --nologo -v q
  dotnet test Homestead.sln -c Release --no-build --nologo
fi

# --- 9. push the port branch to origin - the only push, and never elsewhere ----
git push -u "$FORK_REMOTE" "$PORT"

# --- 10. restore the worktree and print the human handoff ----------------------
git checkout "$SOURCE"
echo
log "done. port branch: $FORK_REMOTE/$PORT"
echo "Ported commits (on $PORT):"
printf '  %s\n' "${PORTED_LOG[@]}"
if [ ${#SKIPPED_LOG[@]} -gt 0 ]; then
  echo "Skipped fork tooling commits (not ported):"
  printf '  %s\n' "${SKIPPED_LOG[@]}"
fi
echo
echo "Compare URL (human opens the upstream PR from here):"
echo "  https://github.com/meue/monogameSettlers/compare/main...$PORT_PREFIX:$PORT?expand=1"
echo
echo "Human command (fill in the title from the PR draft; body from the draft file):"
echo "  gh pr create --repo meue/monogameSettlers --base main --head $PORT_PREFIX:$PORT --title '<from the PR draft>' --body-file .plans/upstream-pr-<issue>.md"
echo
echo "Upstream PR creation is a human step (agents are deny-gated from meue/*)."
