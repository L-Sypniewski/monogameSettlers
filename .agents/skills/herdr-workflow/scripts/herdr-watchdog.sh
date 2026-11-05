#!/usr/bin/env bash
# usage: herdr-watchdog.sh [--interval SECS] [--cooldown SECS] [--once] [--dry-run]
#                          [--prompt TEXT] [--install-systemd | --uninstall-systemd]
#
# Auto-continue opencode agents in herdr worktree panes whose turns were
# interrupted by opencode v2's location-eviction idle sweep: the background
# service interrupts every active session in a location once its idle TTL
# (60 minutes, no config knob) lapses - long subagent calls and turns blocked
# on a question die mid-flight. eviction-patch.cjs (wired into hr_launch) is
# the preventive half; this watchdog is the recovery half and also catches any
# other abort cause that leaves a turn interrupted.
#
# A pane is continued only when ALL guards pass:
#   - opencode agent pane in a linked-worktree workspace (unattended runs;
#     primary checkouts are the user's own, never touched)
#   - workspace not focused (hands off whatever the user is inspecting)
#   - no interactive form open near the bottom ("enter submit" / "esc
#     dismiss" / arrow-select hints - typing would ANSWER the form)
#   - no active turn (the working-state "esc interrupt" footer is absent)
#   - an "interrupted" status line sits near the bottom, and no continuation
#     sentinel was already submitted after it (double-fire guard)
#   - per-pane cooldown elapsed
#
# State: ${XDG_CACHE_HOME:-$HOME/.cache}/herdr-watchdog/<pane_id>.last
# Exit: 0 clean (or installed/uninstalled); 1 fatal (herdr missing/server down
# on a --once pass); 2 usage.

set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib.sh"

INTERVAL=60
COOLDOWN=90
ONCE=0
DRY_RUN=0
ACTION=""
PROMPT='Your previous turn was interrupted by an opencode service eviction (not a user action). Continue from where you left off - do not repeat completed work.'
# short unique marker searched in pane scrollback to detect an already-sent
# continuation (the full prompt contains it verbatim)
SENTINEL='Continue from where you left off'

usage() {
  sed -n '2,5p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'
}

while (( $# > 0 )); do
  case "$1" in
    --interval)  (( $# >= 2 )) || hr_die "--interval requires a value"; INTERVAL=$2; shift 2 ;;
    --cooldown)  (( $# >= 2 )) || hr_die "--cooldown requires a value"; COOLDOWN=$2; shift 2 ;;
    --once)      ONCE=1; shift ;;
    --dry-run)   DRY_RUN=1; shift ;;
    --prompt)    (( $# >= 2 )) || hr_die "--prompt requires a value"; PROMPT=$2; shift 2 ;;
    --install-systemd)   ACTION=install; shift ;;
    --uninstall-systemd) ACTION=uninstall; shift ;;
    -h|--help)   usage; exit 0 ;;
    *)           usage >&2; hr_die "unknown option: $1" ;;
  esac
done

hr_require_cmds herdr jq

UNIT_NAME=herdr-watchdog.service
UNIT_DIR="$HOME/.config/systemd/user"
SELF_ABS="$SCRIPT_DIR/herdr-watchdog.sh"

install_systemd() {
  mkdir -p "$UNIT_DIR"
  local unit="$UNIT_DIR/$UNIT_NAME"
  # WHY: the unit must not depend on this checkout's location or branch - run a
  # copy from a stable per-user dir (re-run --install-systemd to refresh it).
  local stable="${XDG_DATA_HOME:-$HOME/.local/share}/herdr-workflow/scripts"
  mkdir -p "$stable"
  cp -f "$SCRIPT_DIR"/*.sh "$stable"/
  chmod +x "$stable"/*.sh
  # WHY: /prune-devcontainers invokes the extensionless ~/.local/bin path.
  if [ -f "$stable/herdr-prune-devcontainers.sh" ]; then
    mkdir -p "$HOME/.local/bin"
    cp -f "$stable/herdr-prune-devcontainers.sh" "$HOME/.local/bin/herdr-prune-devcontainers"
    chmod +x "$HOME/.local/bin/herdr-prune-devcontainers"
  fi
  local self_installed="$stable/$(basename -- "$SELF_ABS")"
  {
    echo "[Unit]"
    echo "Description=herdr watchdog - auto-continue interrupted opencode agents in herdr worktree panes"
    echo ""
    echo "[Service]"
    # systemd user units get a minimal PATH - herdr lives in ~/.local/bin
    echo "Environment=PATH=$HOME/.local/bin:/usr/local/bin:/usr/local/sbin:/usr/bin:/usr/sbin:/bin"
    echo "ExecStart=$self_installed --interval $INTERVAL --cooldown $COOLDOWN"
    echo "Restart=always"
    echo "RestartSec=15"
    echo ""
    echo "[Install]"
    echo "WantedBy=default.target"
  } > "$unit"
  systemctl --user daemon-reload
  systemctl --user enable --now "$UNIT_NAME"
  hr_info "installed + started $UNIT_NAME (unit: $unit)"
}

uninstall_systemd() {
  systemctl --user disable --now "$UNIT_NAME" 2>/dev/null || true
  rm -f -- "$UNIT_DIR/$UNIT_NAME"
  rm -f -- "$HOME/.local/bin/herdr-prune-devcontainers"
  systemctl --user daemon-reload
  hr_info "stopped + removed $UNIT_NAME"
}

case "$ACTION" in
  install)   install_systemd; exit 0 ;;
  uninstall) uninstall_systemd; exit 0 ;;
esac

STATE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/herdr-watchdog"
mkdir -p "$STATE_DIR"

state_file() { printf '%s/%s.last' "$STATE_DIR" "$1"; }

# analyze <tail-file>: decide whether the pane should be continued.
# Prints the skip reason on stderr-free stdout when declining; exit 0 = fire.
analyze() {
  awk -v sentinel="$SENTINEL" '
    { line[NR] = $0 }
    END {
      bottom_form = 0; bottom_working = 0; last_int = 0; sent_after = 0
      for (i = NR; i > 0 && i > NR - 8; i--) {
        if (!bottom_form && line[i] ~ /enter submit|esc dismiss|select +enter/) bottom_form = 1
        if (!bottom_working && line[i] ~ /esc interrupt/) bottom_working = 1
        if (!last_int && line[i] ~ /· interrupted/) last_int = i
      }
      if (bottom_form) { print "form-open"; exit 1 }
      if (bottom_working) { print "turn-active"; exit 1 }
      if (!last_int) { print "no-recent-interrupt"; exit 1 }
      for (i = last_int; i <= NR; i++)
        if (index(line[i], sentinel) > 0) sent_after = 1
      if (sent_after) { print "already-continued"; exit 1 }
      exit 0
    }' "$1"
}

log() { printf '[watchdog] %s\n' "$*"; }

poll() {
  local ws_json panes_json pane pid agent label focused tail_file verdict f age now
  ws_json=$(hr_json herdr workspace list) || { hr_warn "workspace list failed"; return 1; }

  for pid in $(printf '%s' "$ws_json" | jq -r '.result.workspaces[] | select(.worktree != null and .worktree.is_linked_worktree == true) | .workspace_id'); do
    label=$(printf '%s' "$ws_json" | jq -r --arg id "$pid" '.result.workspaces[] | select(.workspace_id == $id) | .label')
    focused=$(printf '%s' "$ws_json" | jq -r --arg id "$pid" '.result.workspaces[] | select(.workspace_id == $id) | .focused')
    if [[ "$focused" == "true" ]]; then
      log "skip $label: focused (user inspecting)"
      continue
    fi
    panes_json=$(hr_json herdr pane list --workspace "$pid") || { hr_warn "pane list failed for $pid"; continue; }

    for pane in $(printf '%s' "$panes_json" | jq -r '.result.panes[] | select(.agent == "opencode") | .pane_id'); do
      tail_file=$(mktemp "${TMPDIR:-/tmp}/herdr-watchdog.XXXXXX")
      if ! herdr pane read "$pane" --source recent-unwrapped --lines 40 > "$tail_file" 2>/dev/null; then
        rm -f -- "$tail_file"; continue
      fi
      verdict=""
      if ! verdict=$(analyze "$tail_file"); then
        # non-empty verdict = skip reason printed by analyze; empty = unexpected
        [[ -z "${verdict:-}" ]] && verdict="analyze-error"
      fi
      rm -f -- "$tail_file"
      if [[ -n "$verdict" ]]; then
        log "skip $pane ($label): $verdict"
        continue
      fi

      f=$(state_file "$pane")
      now=$(date +%s)
      if [[ -f "$f" ]]; then
        age=$(( now - $(cat "$f") ))
        if (( age < COOLDOWN )); then
          log "skip $pane ($label): cooldown (${age}s < ${COOLDOWN}s)"
          continue
        fi
      fi

      if (( DRY_RUN )); then
        log "DRY-RUN would continue $pane ($label)"
      else
        if ! hr_json herdr pane run "$pane" "$PROMPT" >/dev/null; then
          hr_warn "pane run failed for $pane - will retry after cooldown"
          printf '%s\n' "$now" > "$f"
          continue
        fi
        printf '%s\n' "$now" > "$f"
        log "continued $pane ($label)"
      fi
    done
  done
}

while true; do
  poll || { (( ONCE )) && exit 1; hr_warn "poll failed - retrying next interval"; }
  (( ONCE )) && break
  sleep "$INTERVAL"
done
exit 0
