#!/usr/bin/env bash
# usage: helper library for herdr workflow scripts - source it, do not execute directly

hr_die() { echo "[herdr-workflow] ERROR: $*" >&2; exit 1; }
hr_warn() { echo "[herdr-workflow] WARN: $*" >&2; }
hr_info() { echo "[herdr-workflow] $*" >&2; }

hr_maybe_trust_hint() {
  if printf '%s' "${1:-}" | grep -qiE 'dubious ownership|safe\.directory'; then
    hr_warn "git refused access (dubious ownership). Verify the repository, then re-run this script with --trust-repository."
  fi
}

hr_require_cmds() {
  local launch=0
  if [[ "${1:-}" == "--launch" ]]; then launch=1; shift; fi
  local cmds=("$@")
  if (( ${#cmds[@]} == 0 )); then cmds=(herdr jq gh git); fi
  if (( launch )); then cmds+=(devcontainer docker); fi
  local missing=() c
  for c in "${cmds[@]}"; do
    command -v "$c" >/dev/null 2>&1 || missing+=("$c")
  done
  if (( ${#missing[@]} > 0 )); then
    hr_die "missing required commands: ${missing[*]}"
  fi
}

# Launch deps for repos WITH a devcontainer: devcontainer + docker ride along with the base
# set (herdr/jq/gh/git). Repos WITHOUT one (no .devcontainer/devcontainer.json) run the agent on
# the host, so they require neither the devcontainer CLI nor Docker.
hr_require_launch_cmds() {
  local wt="${1:-$PWD}"
  if hr_has_devcontainer "$wt"; then
    hr_require_cmds --launch
  else
    hr_require_cmds
  fi
}

hr_repo_root() {
  local dir="${1:-$PWD}" root
  root=$(git -C "$dir" rev-parse --show-toplevel 2>/dev/null) || hr_die "not a git repository: $dir"
  git -C "$root" remote get-url origin >/dev/null 2>&1 || hr_die "remote 'origin' not configured in $root"
  printf '%s\n' "$root"
}

# hr_launch_mode_host DIR: repo opt-out from devcontainer launches, declared in
# .herdr-workflow.json as "launchMode": "host". Motivated by linked worktrees: their
# .git file points at the main repo's .git/worktrees/<name> via an absolute HOST
# path, which cannot resolve inside the container, and credentials forwarded via
# remoteEnv depend on the environment of whatever daemon started the container.
# A host launch needs neither, so repos that dispatch worktree runs opt out.
hr_launch_mode_host() {
  local cfg="${1:-$PWD}/.herdr-workflow.json"
  [[ -f "$cfg" ]] || return 1
  jq -e 'select((.launchMode? // "") == "host")' "$cfg" >/dev/null 2>&1
}

# hr_has_devcontainer DIR: whether the worktree carries a devcontainer config to launch into
# (.devcontainer/devcontainer.json). Repos without one launch the agent on the host instead of
# attempting a devcontainer launch that would fail with "Dev container config not found".
hr_has_devcontainer() {
  local wt="${1:-$PWD}"
  hr_launch_mode_host "$wt" && return 1
  [[ -f "$wt/.devcontainer/devcontainer.json" ]]
}

hr_default_branch() {
  local b line
  b=$(git symbolic-ref --short refs/remotes/origin/HEAD 2>/dev/null || true)
  b=${b#origin/}
  if [[ -n "$b" ]]; then printf '%s\n' "$b"; return 0; fi
  while IFS= read -r line; do
    if [[ "$line" == "ref: refs/heads/"* ]]; then
      b=${line#ref: refs/heads/}
      b=${b%%$'\t'*}
      b=${b%% *}
      if [[ -n "$b" ]]; then printf '%s\n' "$b"; return 0; fi
    fi
  done < <(git ls-remote --symref origin HEAD 2>/dev/null || true)
  for b in master main; do
    if git show-ref --verify --quiet "refs/remotes/origin/$b"; then printf '%s\n' "$b"; return 0; fi
  done
  hr_die "unable to determine default branch of origin (tried origin/HEAD, symref lookup, master, main)"
}

hr_slugify() {
  local s="$1" max="${2:-6}" out="" i=0 part
  local parts=()
  s=${s,,}
  s=$(printf '%s' "$s" | LC_ALL=C tr -s -c 'a-z0-9' '-')
  s=${s#-}
  s=${s%-}
  if [[ -z "$s" ]]; then printf 'task\n'; return 0; fi
  IFS='-' read -r -a parts <<< "$s"
  for part in "${parts[@]}"; do
    if [[ -z "$part" ]]; then continue; fi
    if (( i >= max )); then break; fi
    out=${out:+$out-}$part
    i=$((i + 1))
  done
  if [[ -n "$out" ]]; then printf '%s\n' "$out"; else printf 'task\n'; fi
}

hr_json() {
  local out err
  err=$(mktemp)
  if ! out=$("$@" 2>"$err"); then
    cat "$err" >&2
    hr_maybe_trust_hint "$(cat "$err")"
    rm -f "$err"
    hr_die "command failed: $*"
  fi
  rm -f "$err"
  printf '%s\n' "$out"
}

hr_primary_workspace() {
  local repo_root="$1" repo_name="$2" real ws
  real=$(realpath "$repo_root")
  ws=$(hr_json herdr workspace list | jq -r --arg root "$real" --arg name "$repo_name" '
    ([.result.workspaces[]
      | select(.worktree != null and .worktree.repo_root == $root and .worktree.is_linked_worktree == false)
      | .workspace_id][0] // "") as $byroot
    | if $byroot != "" then $byroot
      else ([.result.workspaces[] | select(.label == $name) | .workspace_id][0] // "")
      end')
  if [[ -n "$ws" ]]; then printf '%s\n' "$ws"; return 0; fi
  ws=$(hr_json herdr workspace create --cwd "$repo_root" --label "$repo_name" --no-focus \
    | jq -r '.result.workspace.workspace_id // empty')
  [[ -n "$ws" ]] || hr_die "failed to create primary workspace for $repo_name"
  hr_info "created primary workspace $ws for repo $repo_name"
  printf '%s\n' "$ws"
}

hr_find_worktree() {
  local ws="$1" branch="$2"
  # Path first: a closed worktree has a null open_workspace_id, and bash read
  # (IFS=$'\t') swallows a leading empty TSV field - id-first made the path land
  # in the id variable, falsely reporting closed worktrees as already open.
  hr_json herdr worktree list --workspace "$ws" | jq -r --arg b "$branch" '
    [.result.worktrees[]?
      | select(.is_linked_worktree == true and .branch == $b)
      | [((.path // "") | tostring), ((.open_workspace_id // "") | tostring)]][0]
    // [] | if length == 2 then @tsv else empty end'
}

hr_quote() { printf '%q' "$1"; }

hr_shim_dir() {
  printf '%s\n' "$HOME/.herdr/shim"
}

hr_ensure_agent_shim() {
  local agent="${1:-opencode}" dir
  dir=$(hr_shim_dir)
  mkdir -p "$dir"
  cp -f /bin/bash "$dir/$agent"
  # darwin: /bin/bash is an Apple platform binary; macOS 26 AMFI SIGKILLs a copied
  # platform binary at exec, so re-sign the copy ad-hoc to keep it runnable.
  if [[ "$(uname -s)" == "Darwin" ]]; then
    codesign -f -s - "$dir/$agent" >/dev/null 2>&1 \
      || hr_warn "ad-hoc resign of $dir/$agent failed; macOS may SIGKILL the launch (copied platform binary)"
  fi
}

# A missing bridge silently degrades agent visibility for the entire launch - fail
# explicitly instead; HERDR_BRIDGE_OPTIONAL=1 downgrades to the old warn-and-continue.
hr_ensure_herdr_bridge() {
  if [[ -S "$HOME/.herdr/bridge/herdr.sock" ]]; then
    return 0
  fi
  if command -v systemctl >/dev/null 2>&1; then
    systemctl --user start herdr-bridge.service >/dev/null 2>&1 || true
    sleep 1
    if [[ -S "$HOME/.herdr/bridge/herdr.sock" ]]; then
      return 0
    fi
  fi
  if [[ "${HERDR_BRIDGE_OPTIONAL:-}" == "1" ]]; then
    hr_warn "herdr bridge socket unavailable - agents inside devcontainers stay invisible to herdr (see scripts/herdr-bridge.service); continuing"
    return 0
  fi
  hr_die "herdr bridge socket unavailable - agents inside devcontainers would stay invisible to herdr. Install the shipped unit: cp scripts/herdr-bridge.service ~/.config/systemd/user/ && systemctl --user daemon-reload && systemctl --user enable --now herdr-bridge - or start an existing one: systemctl --user start herdr-bridge. Set HERDR_BRIDGE_OPTIONAL=1 to downgrade this to a warning and continue."
}

# Docker --mount type=bind errors on a missing source instead of auto-creating it, so a
# gitignored scratch dir the devcontainer expects in the worktree (e.g. .opencode-data)
# kills every fresh worktree at docker run. Precreate missing worktree-relative bind-source
# dirs from the target repo's devcontainer.json (JSONC: mount strings are grepped, not
# jq-parsed). Skipped on purpose: .devcontainer/** (initializeCommand stages those - a
# missing one is a staging bug that must fail loudly, not be masked) and dot-suffixed
# basenames (file binds: mkdir would mount an empty dir over the file target and silently
# break it). ${localEnv:HOME} sources are host-global and never match the extraction.
hr_precreate_bind_dirs() {
  local wt_dir="$1" rel
  local cfg="$wt_dir/.devcontainer/devcontainer.json"
  [[ -f "$cfg" ]] || return 0
  while IFS= read -r rel; do
    [[ -n "$rel" ]] || continue
    [[ "$rel" == .devcontainer/* ]] && continue
    case "${rel##*/}" in ?*.*) continue ;; esac
    if [[ ! -e "$wt_dir/$rel" ]]; then
      mkdir -p "$wt_dir/$rel"
      hr_info "precreated missing bind-source dir: $wt_dir/$rel"
    fi
  done < <(grep -o '"source=${localWorkspaceFolder}/[^",]*' "$cfg" \
    | sed 's/^"source=\${localWorkspaceFolder}\///')
}

# Ids of RUNNING devcontainers whose local_folder label matches the worktree;
# the devcontainer CLI sets that label to the workspace folder path, so the
# match is exact and stopped containers drop out of docker ps on purpose.
# realpath first: the label holds the resolved folder, so a --path with symlinks
# or trailing dots must be normalized to still match.
hr_devcontainer_ids() {
  local real
  real=$(realpath "$1" 2>/dev/null) || real="$1"
  docker ps --filter "label=devcontainer.local_folder=$real" --format '{{.ID}}'
}

# Prints the id of the first running devcontainer of the worktree in which the
# agent runtime has a live process; prints nothing and returns 1 when there is
# none. Probed via docker exec ps, not docker top: top misses userns-remapped
# (rootless-docker) processes. The match is argv0-exact (basename == agent): a
# substring match false-positives on post-create shells whose args merely
# mention the agent's config paths or download URLs. The serve daemon is a
# singleton background service the next client reuses, not a live agent, so a
# container running only it still gets a relaunch; exclusion is by invocation
# shape, so prompt text mentioning the service does not lift the guard. A
# failing probe on a
# container docker ps still lists counts as alive: a wrong "dead" answer
# double-runs a session, a wrong "alive" one only strands a relaunch behind an
# explicit warn.
hr_agent_alive_in_devcontainer() {
  local wt_dir="$1" agent="$2" cid procs hits
  # Host-mode repos have no devcontainer to host an agent in - nothing can be alive there, so
  # the launch (which falls back to the host) proceeds.
  hr_has_devcontainer "$wt_dir" || return 1
  for cid in $(hr_devcontainer_ids "$wt_dir"); do
    if ! procs=$(docker exec "$cid" ps -eo args 2>/dev/null); then
      printf '%s\n' "$cid"
      return 0
    fi
    hits=$(printf '%s\n' "$procs" | grep -E "(^|/)${agent}([[:space:]]|$)" | grep -vE "(^|/)${agent} serve([[:space:]]|$)" || true)
    if [[ -n "$hits" ]]; then
      printf '%s\n' "$cid"
      return 0
    fi
  done
  return 1
}

# hr_release_stale_agent PANE: drop a hook-held agent registration left on PANE by a
# previous container run. Hook reports (source herdr:*) own the pane's agent state and
# disable screen detection, so after a devcontainer restart the last reported state
# (e.g. working) sticks until something releases it. Callers invoke this only after the
# live-agent guard passed, so anything still registered is stale. Best-effort: a failed
# release never blocks a launch.
hr_release_stale_agent() {
  local pane="$1" info agent source
  info=$(herdr pane get "$pane" 2>/dev/null | jq -r '.result.pane | [.agent_session.source // "", .agent // ""] | @tsv' 2>/dev/null) || return 0
  IFS=$'\t' read -r source agent <<<"$info"
  [[ -n "$source" && -n "$agent" ]] || return 0
  if herdr pane release-agent "$pane" --source "$source" --agent "$agent" --seq "$(date +%s%N)" >/dev/null 2>&1; then
    hr_info "released stale $agent registration on pane $pane"
  fi
  return 0
}

# First pane of an open workspace - the root pane herdr worktree workspaces
# launch their agent into; pane ids sort stably in creation order.
hr_workspace_first_pane() {
  hr_json herdr pane list --workspace "$1" | jq -r '[.result.panes[]?.pane_id][0] // empty'
}

# hr_wait_shell_ready PANE: a fresh pane's interactive zsh can sit in oh-my-zsh's
# "[Y/n]" update prompt, which swallows the first typed characters of the launch line.
# Decline it and wait for a quiet screen before typing. Bounded and best-effort;
# HERDR_NO_SHELL_WAIT=1 skips it.
hr_wait_shell_ready() {
  local pane="$1" prev="" cur="" last="" i
  [[ "${HERDR_NO_SHELL_WAIT:-0}" == "1" ]] && return 0
  for ((i = 0; i < 40; i++)); do
    cur=$(herdr pane read "$pane" --source visible --lines 30 2>/dev/null) || cur=""
    last=$(printf '%s\n' "$cur" | grep -v '^[[:space:]]*$' | tail -n 1 || true)
    if [[ "$last" == *"[Y/n]"* ]]; then
      herdr pane send-keys "$pane" n >/dev/null 2>&1 || true
      prev=""
    elif [[ -n "$cur" && "$cur" == "$prev" ]]; then
      return 0
    else
      prev="$cur"
    fi
    sleep 0.5
  done
  hr_warn "pane $pane did not settle within 20s - typing the launch line anyway"
  return 0
}

hr_launch() {
  local pane="$1" prompt="$2" wt_dir="${3:-$PWD}" resume="${4:-0}" agent="${5:-opencode}" shim script payload agent_cmd oc_args="" evict_cjs="" def_agent=""
  [[ "$agent" == "opencode" || "$agent" == "claude" ]] || hr_die "unknown agent: $agent (opencode|claude)"
  if [[ "$agent" == "opencode" ]]; then
    oc_args="--auto"
    [[ "$resume" == "1" ]] && oc_args="--auto --continue"
  elif [[ "$resume" == "1" ]]; then
    oc_args="--continue"
  fi
  def_agent=$(hr_opencode_default_agent "$wt_dir")
  # agent identifiers only - the value is interpolated into a shell case pattern below
  [[ "$def_agent" =~ ^[A-Za-z0-9_-]+$ ]] || def_agent=""
  # Repos without a devcontainer config launch the agent on the host in the pane - no container,
  # no devcontainer up/exec, no eviction patch, no default-agent gate (nothing was restarted).
  local in_container=0
  if hr_has_devcontainer "$wt_dir"; then in_container=1; fi
  hr_ensure_agent_shim "$agent"
  hr_ensure_herdr_bridge
  hr_release_stale_agent "$pane"
  hr_precreate_bind_dirs "$wt_dir"
  # opencode v2 eviction hardening (see eviction-patch.cjs header): patch the
  # binary inside the freshly provisioned container and restart the service,
  # so the session-hosting code is the patched one before any TUI connects.
  evict_cjs="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/eviction-patch.cjs"
  # bash renamed to the agent binary name: herdr detects pane agents by process name
  # (claude additionally registers via its SessionStart integration hook). The -c payload
  # must not end with the long-running chain: bash exec-optimizes the final -c command
  # into it, replacing the shim (herdr's process evidence); the temp script + trailing rc/echo/rm keep the shim alive as the chain's parent.
  shim="$(hr_shim_dir)/$agent"
  # The pane runs the user's interactive zsh, where bash printf %q escaping is NOT safe:
  # %q leaves '#' bare, and under zsh extendedglob '#' is a glob operator - with nomatch
  # set, zsh aborts the whole line ("no matches found") before anything runs. So keep the
  # typed line metacharacter-free: all variable content (worktree dir, prompt) lives in a
  # temp script parsed by the shim's own bash (where %q is exact), and the typed line is a
  # fixed-shape single-quoted payload containing no single quote.
  # devcontainer exec's default userEnvProbe (loginInteractiveShell) replays the probed
  # login-shell env - including credentials sourced from the staged host env file - as
  # docker exec -e argv flags, exposing tokens to any host ps. Probe none keeps argv to
  # remoteEnv only; credentials still reach the agent via the container's own env
  # (docker run --env-file), and the PATH the probe used to supply is replaced by the
  # explicit remote-env below (.opencode/.bun/mise-shims/nvm/dotnet/local dirs; harmless
  # on containers where some dirs don't exist).
  local oc_exec_path='/home/vscode/.opencode/bin:/home/vscode/.bun/bin:/mnt/mise-data/shims:/usr/local/share/nvm/current/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/home/vscode/.dotnet:/home/vscode/.dotnet/tools:/home/vscode/.local/bin'
  # the claude-code devcontainer feature may install to ~/.claude/local instead of /usr/local/bin;
  # harmless on containers where the dir does not exist (same rationale as the dirs above).
  if [[ "$agent" == "claude" ]]; then oc_exec_path="$oc_exec_path:/home/vscode/.claude/local"; fi
  script=$(mktemp "${TMPDIR:-/tmp}/herdr-launch.XXXXXX")
  {
    printf '%s\n' "cd $(hr_quote "$wt_dir") || exit 1"
    printf '%s\n' 'echo "[herdr] agent bridge up"'
  # claude parity with opencode --auto --prompt: the prompt is the positional arg of an
  # interactive session - headless -p renders nothing in the pane and cannot be steered;
  # permission behavior comes from container-local settings, never the argv.
  if [[ "$agent" == "claude" ]]; then
    if [[ -n "$prompt" ]]; then
      agent_cmd="claude $(hr_quote "$prompt")"
    elif [[ "$resume" == "1" ]]; then
      # claude leaves headless-created sessions out of --continue (documented upstream),
      # so an interactive resume resolves the newest session recorded for this
      # worktree cwd and resumes it by id; --continue is the no-session fallback.
      agent_cmd="sh -c 'f=\$(grep -l \"\\\"cwd\\\":\\\"\$(pwd)\\\"\" \"\$HOME\"/.claude/projects/*/*.jsonl 2>/dev/null | xargs -r ls -t | head -n 1); s=\${f##*/}; if [ -n \"\$f\" ]; then claude --resume \"\${s%.jsonl}\"; else claude --continue; fi'"
    else
      agent_cmd="claude $oc_args"
    fi
  elif [[ -n "$prompt" ]]; then
    agent_cmd="opencode $oc_args --prompt $(hr_quote "$prompt")"
  else
    agent_cmd="opencode $oc_args"
  fi
  if (( in_container )) && [[ "$agent" == "opencode" && "${HERDR_NO_EVICT_PATCH:-0}" != "1" && -f "$evict_cjs" ]]; then
    printf '%s\n' 'devcontainer up --workspace-folder . || exit 1'
    printf '%s\n' '_hr_cid="$(docker ps -q --filter label=devcontainer.local_folder="$PWD" | head -n1)"'
    printf '%s\n' 'if [ -z "$_hr_cid" ]; then'
    printf '%s\n' '  echo "[herdr] WARN: no devcontainer found for $PWD - skipping eviction patch"'
    printf '%s\n' 'else'
    printf '%s\n' "  devcontainer exec --default-user-env-probe none --remote-env PATH=$oc_exec_path --workspace-folder . node -e \"\$(cat $(hr_quote "$evict_cjs"))\" || echo '[herdr] WARN: eviction patch step failed - continuing unpatched'"
    printf '%s\n' '  docker exec -u vscode "$_hr_cid" pkill -f "opencode serve --service" 2>/dev/null || true'
    # plain docker exec carries no remoteEnv: the restarted service hosts every later session's
    # tool shells, so pass the herdr identity plus the full exec PATH the TUI's later --remote-env
    # (client process only) can never deliver to it.
    printf '%s\n' "  docker exec -u vscode -e HERDR_ENV=1 -e HERDR_SOCKET_PATH=/herdr/herdr.sock -e HERDR_PANE_ID=$(hr_quote "$pane") -e PATH=$(hr_quote "$oc_exec_path") \"\$_hr_cid\" sh -c \"command -v opencode >/dev/null 2>&1 && opencode service start >/dev/null 2>&1 || true\" || true"
    printf '%s\n' 'fi'
  elif (( in_container )); then
    printf '%s\n' 'devcontainer up --workspace-folder . || exit 1'
  fi
  # The eviction patch restarts the opencode service, discarding its loaded project
  # config; a TUI --prompt session created before the restart finishes loading resolves
  # its agent from built-ins alone (default_agent and @mention both miss), so gate on
  # the project default agent becoming visible from the worktree cwd. Best-effort.
  if (( in_container )) && [[ -n "$def_agent" ]]; then
    printf '%s\n' '  _hr_gate_cid="$(docker ps -q --filter label=devcontainer.local_folder="$PWD" | head -n1)"'
    printf '%s\n' "  [ -n \"\$_hr_gate_cid\" ] && docker exec -u vscode -w \"\$PWD\" \"\$_hr_gate_cid\" sh -c 'command -v opencode >/dev/null 2>&1 || exit 0; i=0; while [ \$i -lt 60 ]; do out=\$(opencode debug agents 2>/dev/null); case \"\$out\" in *${def_agent}*) exit 0;; esac; i=\$((i+1)); sleep 1; done; echo \"[herdr] WARN: opencode default agent ${def_agent} not visible after 60s - launching anyway\"' || true"
  fi
  if (( in_container )); then
    printf '%s\n' "devcontainer exec --default-user-env-probe none --remote-env PATH=$oc_exec_path --workspace-folder . $agent_cmd"
  else
    # No devcontainer config in the worktree - host-mode launch: run the agent start line
    # straight in the pane (the script already cd'd into the worktree above).
    printf '%s\n' "$agent_cmd"
  fi
  } >"$script"
  payload="bash $(hr_quote "$script"); rc=\$?; echo \"[herdr] launch exited (\$rc)\"; rm -f -- $(hr_quote "$script")"
  if [[ "$payload" == *"'"* ]]; then
    rm -f -- "$script"
    hr_die "launch payload would contain a single quote (unsupported TMPDIR?) - refusing a pane line zsh could misparse: $script"
  fi
  hr_wait_shell_ready "$pane"
  if (( in_container )); then
    hr_info "launching devcontainer + $agent in pane $pane"
  else
    hr_info "no devcontainer config - host-mode agent launch in pane $pane"
  fi
  hr_json herdr pane run "$pane" "$shim -c '$payload'" >/dev/null
}

# hr_project_item_id: print the id of the project item whose content.url matches $1 from a
# gh project item-list JSON payload on stdin; prints nothing when no item matches.
hr_project_item_id() {
  local url="$1"
  jq -r --arg url "$url" \
    '[.items[]? | select(.content != null and .content.url == $url) | .id][0] // empty'
}

# Best-effort issue triage: assign the issue to the gh-authenticated account (@me) and move
# it to the configured project's in-progress status option. The board mapping is repo-specific
# (.herdr-workflow.json at the repo root) - see SKILL.md for the schema. Best-effort by
# contract: any failure logs a warn and returns 0, so board problems never fail worktree
# creation. All operations are idempotent (re-running on an already-synced issue is a no-op).
hr_board_sync() {
  local repo_root="$1" n="$2" url="$3"
  local cfg="$repo_root/.herdr-workflow.json"
  if [[ ! -f "$cfg" ]]; then
    hr_info "no .herdr-workflow.json in repo root - board sync off for issue #$n"
    return 0
  fi
  # select() yields nothing (jq -e then exits non-zero) when any required key is missing or
  # mistyped; unknown keys fall through the extraction and are ignored. -s takes [0], so
  # the first JSON document wins when the file holds several; trailing garbage still fails jq.
  local vals
  if ! vals=$(jq -ser '.[0]
      | select(.projectOwner | type == "string" and length > 0)
      | select(.projectNumber | type == "number")
      | select(.statusField | type == "string" and length > 0)
      | select(.inProgressOption | type == "string" and length > 0)
      | [.projectOwner, (.projectNumber | tostring), .statusField, .inProgressOption] | @tsv' \
      "$cfg" 2>/dev/null); then
    hr_info ".herdr-workflow.json invalid or missing required keys - board sync off for issue #$n"
    return 0
  fi
  local owner pnum field option
  IFS=$'\t' read -r owner pnum field option <<<"$vals"

  if [[ -z "$url" ]]; then
    hr_warn "board sync: no issue URL for #$n - skipping assign + move (continuing)"
    return 0
  fi

  local out
  if out=$(gh issue edit "$n" --add-assignee @me 2>&1 >/dev/null); then
    hr_info "board sync: issue #$n assigned to @me"
  else
    hr_warn "board sync: failed to assign issue #$n to @me: $out (continuing)"
  fi

  # item-edit speaks internal ids only, so resolve the project id behind the number first.
  local project_id perr pout msg
  perr=$(mktemp)
  if ! project_id=$(gh project view "$pnum" --owner "$owner" --format json 2>"$perr" \
      | jq -r '.id // empty') || [[ -z "$project_id" ]]; then
    pout=$(cat "$perr")
    rm -f "$perr"
    msg="board sync: project $owner/$pnum not found - issue #$n not moved: $pout (continuing)"
    if [[ "$pout" =~ scope|credential|auth ]]; then
      msg+=" - token may lack the project scope; try: gh auth refresh -s project"
    fi
    hr_warn "$msg"
    return 0
  fi
  rm -f "$perr"

  # Option names match case-sensitively: "In progress" and "In Progress" are different
  # options in different projects, so the config's exact spelling is passed through as-is.
  local ids field_id option_id ferr fout
  ferr=$(mktemp)
  if ! ids=$(gh project field-list "$pnum" --owner "$owner" --format json --limit 100 2>"$ferr" \
    | jq -r --arg f "$field" --arg o "$option" '
      [.fields[]? | select(.name == $f)][0]
      | if . == null then "NOT_FOUND"
        elif .options == null then "NOT_SINGLE_SELECT"
        else [.id, ([.options[]? | select(.name == $o) | .id][0] // "")] | @tsv end'); then
    fout=$(cat "$ferr")
    rm -f "$ferr"
    hr_warn "board sync: field-list failed for project $owner/$pnum: $fout - issue #$n not moved (continuing)"
    return 0
  fi
  rm -f "$ferr"
  if [[ "$ids" == "NOT_FOUND" ]]; then
    hr_warn "board sync: field '$field' not found in project $owner/$pnum - issue #$n not moved (continuing)"
    return 0
  fi
  if [[ "$ids" == "NOT_SINGLE_SELECT" ]]; then
    # gh emits .options only for single-select fields - null means the field cannot take one
    hr_warn "board sync: field '$field' in project $owner/$pnum is not a single-select field - issue #$n not moved (continuing)"
    return 0
  fi
  if [[ "$ids" != *$'\t'* ]]; then
    hr_warn "board sync: field lookup for '$field' in project $owner/$pnum returned no usable result - issue #$n not moved (continuing)"
    return 0
  fi
  IFS=$'\t' read -r field_id option_id <<<"$ids"
  if [[ -z "$option_id" ]]; then
    hr_warn "board sync: option '$option' not found on field '$field' in project $owner/$pnum - issue #$n not moved (continuing)"
    return 0
  fi

  # The issue URL is the item identity: numbers repeat across repos in one project, and
  # draft items carry no content at all.
  local item_id="" payload total_count="" list_limit add_msg="" lerr aerr
  lerr=$(mktemp)
  aerr=$(mktemp)
  if payload=$(gh project item-list "$pnum" --owner "$owner" --format json --limit 500 2>"$lerr"); then
    item_id=$(printf '%s' "$payload" | hr_project_item_id "$url") || item_id=""
    total_count=$(printf '%s' "$payload" | jq -r '.totalCount // empty') || total_count=""
  else
    hr_warn "board sync: item-list failed for project $owner/$pnum: $(cat "$lerr") (continuing)"
  fi
  # item-list pages 100 items at a time and stops silently at --limit, so a miss inside the
  # first 500 proves nothing when the project holds more - list the full count before item-add
  list_limit=500
  if [[ -z "$item_id" && "$total_count" =~ ^[0-9]+$ ]] && (( total_count > 500 )); then
    list_limit=$total_count
    if payload=$(gh project item-list "$pnum" --owner "$owner" --format json --limit "$list_limit" 2>"$lerr"); then
      item_id=$(printf '%s' "$payload" | hr_project_item_id "$url") || item_id=""
    else
      hr_warn "board sync: item-list failed for project $owner/$pnum: $(cat "$lerr") (continuing)"
    fi
  fi
  if [[ -z "$item_id" ]]; then
    if payload=$(gh project item-add "$pnum" --owner "$owner" --url "$url" --format json 2>"$aerr") \
        && item_id=$(printf '%s' "$payload" | jq -r '.id // empty') && [[ -n "$item_id" ]]; then
      hr_info "board sync: issue #$n added to project $owner/$pnum"
    else
      add_msg=$(cat "$aerr")
      # item-add failed or its response carried no id - one full re-list before giving up,
      # so a server-side add or a beyond-page-limit "already exists" is not mistaken for a miss
      if payload=$(gh project item-list "$pnum" --owner "$owner" --format json --limit "$list_limit" 2>"$lerr"); then
        item_id=$(printf '%s' "$payload" | hr_project_item_id "$url") || item_id=""
      else
        add_msg+="; item-list: $(cat "$lerr")"
      fi
    fi
  fi
  if [[ -z "$item_id" ]]; then
    rm -f "$lerr" "$aerr"
    hr_warn "board sync: issue #$n not in project $owner/$pnum and item-add failed: ${add_msg:-no stderr captured} - not moved (continuing)"
    return 0
  fi
  rm -f "$lerr" "$aerr"

  if out=$(gh project item-edit --id "$item_id" --field-id "$field_id" --project-id "$project_id" \
      --single-select-option-id "$option_id" 2>&1 >/dev/null); then
    hr_info "board sync: issue #$n moved to '$option' in project $owner/$pnum"
  else
    hr_warn "board sync: failed to move issue #$n to '$option' in project $owner/$pnum: $out (continuing)"
  fi
  return 0
}

# Project default agent from the worktree's opencode config (comment-tolerant sed:
# jq cannot parse JSONC; single command, no pipeline - safe under pipefail).
# Empty when unset anywhere.
hr_opencode_default_agent() {
  local wt="$1" f v
  for f in "opencode.jsonc" "opencode.json" ".opencode/opencode.jsonc" ".opencode/opencode.json"; do
    [[ -f "$wt/$f" ]] || continue
    v=$(sed -n 's/.*"default_agent"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$wt/$f")
    v=${v%%$'\n'*}
    if [[ -n "$v" ]]; then printf '%s' "$v"; return; fi
  done
  return 0
}

# Mention prefix for issue-mode launch prompts: explicit intent on top of the
# default-agent readiness gate (a config-loaded TUI prompt resolves default_agent
# on its own). Unset by default (skill stays generic).
hr_issue_prompt_mention() {
  local repo_root="$1"
  if [[ -n "${HERDR_ISSUE_PROMPT_MENTION:-}" ]]; then
    printf '%s\n' "$HERDR_ISSUE_PROMPT_MENTION"
    return 0
  fi
  local cfg="$repo_root/.herdr-workflow.json"
  if [[ ! -f "$cfg" ]]; then
    return 0
  fi
  # Same leniency contract as hr_board_sync: any config problem means no prefix, never a failed dispatch.
  jq -ser '.[0] | select(.issuePromptMention | type == "string" and length > 0) | .issuePromptMention' "$cfg" 2>/dev/null || true
}

hr_issue_prompt() {
  local mention="$1" number="$2" title="$3" url="$4"
  printf '%s' "${mention:+$mention }Work on issue #${number}: ${title} - ${url}"
}

hr_tracking_guard() {
  local path="$1" branch="$2" fresh="${3:-0}" out
  if (( fresh )); then
    git -C "$path" branch --unset-upstream >/dev/null 2>&1 || true
  fi
  if git -C "$path" rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' >/dev/null 2>&1; then
    if out=$(git -C "$path" push 2>&1); then
      if [[ "$out" == *"Everything up-to-date"* ]]; then printf 'up-to-date\n'; else printf 'yes\n'; fi
    else
      hr_warn "push rejected for branch $branch (continuing):"
      printf '%s\n' "$out" >&2
      hr_maybe_trust_hint "$out"
      printf 'failed\n'
    fi
  else
    if out=$(git -C "$path" push -u origin "$branch" 2>&1); then
      if [[ "$out" == *"Everything up-to-date"* ]]; then printf 'up-to-date\n'; else printf 'yes\n'; fi
    else
      hr_warn "push failed for branch $branch (continuing):"
      printf '%s\n' "$out" >&2
      hr_maybe_trust_hint "$out"
      printf 'failed\n'
    fi
  fi
  return 0
}
