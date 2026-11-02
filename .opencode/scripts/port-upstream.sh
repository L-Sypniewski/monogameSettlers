#!/usr/bin/env bash
# Port the upstream-able commits of a fork branch: replay them onto upstream/main, verify the
# result contains no fork-only paths or content, push it as port/<branch> to origin, and print
# the compare URL + human gh command for the upstream PR. Recovery from a hard fail is manual
# by design - a contaminated port branch is worse than a stopped script.
set -euo pipefail

EXCLUDE="$(dirname "$0")/port-upstream.exclude"
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

# --- fork-only exclusion data: paths (skip commits, reject mixes) + content markers
[ -f "$EXCLUDE" ] || die "missing exclusion config: $EXCLUDE"
FORK_PATHS=()
MARKERS=()
while read -r line; do
  case "$line" in
    ''|'#'*) continue ;;
    marker\ *)
      read -r -a words <<< "$line"; mfile="${words[1]}"
      MARKERS+=("$mfile"$'\t'"${line#marker "$mfile" }")
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

# --- 1. fetch both remotes (tags included: MinVer reads them) ------------------
log "fetching upstream (with tags) and origin"
git fetch upstream --tags
git fetch origin
git rev-parse --verify -q "$UPSTREAM_MAIN" >/dev/null || die "$UPSTREAM_MAIN not found after fetch"
git rev-parse --verify -q "$SOURCE" >/dev/null || die "source branch not found: $SOURCE"
if ! { git diff --quiet && git diff --cached --quiet; }; then
  die "working tree not clean - commit or stash first"
fi

# --- 2. refuse an existing remote port branch unless --force (delete + recreate)
if git ls-remote --heads "$FORK_REMOTE" "$PORT" | grep -q .; then
  if [ "$FORCE" = true ]; then
    log "--force: deleting existing $FORK_REMOTE/$PORT and re-porting"
    git push "$FORK_REMOTE" --delete "$PORT"
  else
    die "$PORT already exists on $FORK_REMOTE. Recovery: re-run with --force (delete + recreate)."
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
  shared=""; forkonly=""
  while IFS= read -r f; do
    if is_fork_only "$f"; then forkonly+="$f"$'\n'; else shared+="$f"$'\n'; fi
  done < <(git diff-tree --no-commit-id --name-only -r "$sha")
  if [ -z "$shared" ]; then
    log "skip tooling commit ${sha:0:9} $subject"
    SKIPPED_LOG+=("${sha:0:9} $subject")
  elif [ -n "$forkonly" ]; then
    die "commit ${sha:0:9} ($subject) mixes fork-only and shared paths - split the commit, then re-run.
fork-only files:
$forkonly
shared files:
$shared"
  else
    log "port commit ${sha:0:9} $subject"
    PORTED_SHAS+=("$sha")
    PORTED_LOG+=("${sha:0:9} $subject")
  fi
done
[ ${#PORTED_SHAS[@]} -gt 0 ] || die "nothing upstream-able on $SOURCE (every commit is fork tooling)"

# --- 5. replay the selected commits onto upstream/main -------------------------
log "creating $PORT from $UPSTREAM_MAIN"
git checkout -B "$PORT" "$UPSTREAM_MAIN"
for sha in "${PORTED_SHAS[@]}"; do
  git cherry-pick "$sha" || die "cherry-pick of ${sha:0:9} conflicted (worktree left on $PORT mid-cherry-pick).
Recovery: resolve the conflicts, 'git add' the resolutions, 'git cherry-pick --continue',
then re-run this script with --force."
  log "cherry-picked ${sha:0:9}"
done

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
dotnet build Homestead.sln -c Release --nologo -v q
dotnet test Homestead.sln -c Release --no-build --nologo

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
