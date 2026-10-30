# Subagent Delegation Rules

Auto-loaded via opencode `instructions`. Apply to all delegation decisions.

## When to delegate (vs main context)

**Delegate when:**
- Task produces verbose output (test runs, logs, GitHub/MCP fetches) - keep main context clean
- Work is self-contained and returns a summary
- You need to enforce tool restrictions
- Multiple independent threads can run in parallel

**Stay in main context when:**
- Frequent back-and-forth needed
- Phases share significant context (plan → implement → test)
- Quick targeted change; latency matters

## Parallel vs sequential

Fan out only when work is **truly independent**:
- Different files, no shared imports → separate subagents
- Different concerns on same diff (security / perf / tests) → separate subagents
- Same file, or sequential steps (A→B→C) → single agent

**Coding caveat (Anthropic):** most coding tasks have fewer parallelizable threads than research. Default to sequential.

## opencode mechanics

- Delegate via the **subagent tool**. Subagents get their own context window.
- Every subagent tool call takes three required arguments: `agent`, `description` and `prompt`. `description` is a short 3-5 word label shown in the UI, not optional metadata; omitting it fails the call.
- **Parallel fan-out** = emit ONE assistant message with multiple subagent tool calls.
- **Sequential chain** = call A → result → call B with A's output.
- The spawner's `subagent` permission rules (`action: subagent`, `resource: <agent ID>`) control which agents it may launch; a spawned agent runs under its OWN configured permissions, not a subset of its parent's.
- Only `mode: subagent` / `mode: all` agents are launchable - `mode: primary` agents run only as a session's main agent. An agent you expect to spawn MUST NOT be `mode: primary`.
- `hidden: true` keeps an agent out of listings and the subagent catalog; it stays callable through an explicit permission allow.
- `mode: subagent` for workers; `model` inherits from the parent session when unset.
- The builtin `general` and `explore` agents are leaves: `explore` is read-only navigation, `general` denies questions and launching subagents. Anything fanned out to them cannot fan out further.
- To forbid children entirely, deny `subagent` for `*` and add no allows. Nesting depth is bounded (`experimental.subagent_depth`); keep chains shallow.

## Subagent delegation template

Tool arguments (all three required - omitting `description` fails the call):
- `agent`: which agent to launch (e.g. `general`)
- `description`: short 3-5 word label shown in the UI
- `prompt`: the task, carrying the fields below

`prompt` body fields:
- **Objective**: what to accomplish
- **Context**: file:line, relevant state, exact request
- **Tools/Sources**: which tools/docs to use
- **Output Format**: structured format expected
- **Boundaries**: in/out of scope
- **Report back with**: artifact/reference + commit SHA or file path

> **Citation discipline**: workers that produce cited output (e.g. `code-review`
> finding `basis` fields, `plan-orchestrator` research-note blocks) follow the
> shared rule at `.opencode/agent-resources/shared/rules/citations.md`
> (source + link + quote for every external claim). The per-agent worker prompts
> (`code-review/templates/worker-prompt.md`, `plan-orchestrator/templates/worker-prompt.md`)
> implement this skeleton and add the citation requirement on top.

## Principles

1. Default to main context; delegate deliberately (subagents pay latency + token cost)
2. Fan out only when truly independent (no shared files, no step dependencies)
3. ALWAYS isolate high-volume output (tests, logs, GitHub/MCP) into subagents
4. Each delegation needs objective + output format + tools + boundaries
5. Plan fan-in (synthesis) BEFORE fan-out
6. Avoid "game of telephone" - subagents write artifacts, return lightweight references
7. Group related work (same file/nearby lines/concern) into one subagent; unrelated → separate

## Error handling

- Subagent fails: note and continue with siblings; surface in summary
- Tool failure: retry once, fall back (e.g., MCP → gh CLI), or report
- Decide per workflow: rollback / retry / escalate

## Anti-patterns

- Spawning many subagents for trivial work (overhead exceeds value)
- Fan-out without fan-in plan (wasted work, no synthesis)
- Parallel mutations on shared files (merge conflicts)
- Nesting too deep (omit Task from tools to forbid children)
- Routing cheap read-only work to expensive models (use Haiku-class for exploration)
- Vague delegation prompts → duplicated/gapped work
1