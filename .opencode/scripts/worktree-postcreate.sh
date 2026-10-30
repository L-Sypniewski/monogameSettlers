#!/usr/bin/env bash
# opencode worktree postCreate hook (+ reusable from the worktree skill).
#
# A new worktree branch must NEVER track origin/master (or main). That caused a
# real accident: a plain `git push` (no -u) shipped commits straight to master
# because the branch's upstream pointed there. New branches track their OWN
# remote, set by the push below at creation time.
#
# This surgically strips any master/main upstream left on the worktree branch by
# git's default branch.autoSetupMerge=true (when the start point is a remote
# tracking ref), then publishes the branch to origin with -u so it exists on the
# remote from creation and tracks its OWN origin/<branch>.
#
# Usage:
#   worktree-postcreate.sh              # operates on the current worktree (CWD)
#   worktree-postcreate.sh <wt-path>    # operates on a specific worktree dir
set -u

wt="${1:-.}"

br="$(git -C "$wt" branch --show-current 2>/dev/null)" || { echo "[worktree] not a git worktree ($wt); skipping"; exit 0; }
[ -n "$br" ] || { echo "[worktree] no current branch; skipping"; exit 0; }

m="$(git -C "$wt" config "branch.$br.merge" 2>/dev/null)" || true

case "$m" in
  refs/heads/master|refs/heads/main)
    git -C "$wt" config --unset "branch.$br.merge" 2>/dev/null || true
    git -C "$wt" config --unset "branch.$br.remote" 2>/dev/null || true
    echo "[worktree] stripped accidental '$m' upstream from '$br' (was tracking master/main)."
    ;;
  "")
    : # no upstream set - correct for a brand-new branch.
    ;;
  *)
    echo "[worktree] '$br' tracks '$m' - left untouched."
    ;;
esac

# Every worktree branch exists on origin from creation; the -u also sets correct
# upstream tracking. A failed push (offline, missing creds) must not fail
# worktree creation - warn and leave publishing to the next explicit push.
if git -C "$wt" push -u origin "$br"; then
  echo "[worktree] pushed '$br' to origin (upstream set to origin/$br)."
else
  echo "[worktree] WARNING: push of '$br' failed - publish manually: git -C '$wt' push -u origin '$br'"
fi
