// Board wiring and the auto-merge risk profile are repo policy, read from
// the repo's own `.shipflow.json` at tool-call time (refreshConfig) - a swap
// is a one-line tracked change and different repos point wherever they like.
// The board block is required for board-coupled tools; `autoMerge` is
// optional and gates the finalize tool's merge arm. The repo identity for
// API paths is derived from the git origin remote, not configured.
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

// Workflow-owned labels, rejected at issue creation no matter the repo:
// 'ship-ready' gates the autonomous dispatch frontier and 'needs-spec' marks
// dispatcher-side intake failure - both are set by their owning flows only.
export const WORKFLOW_RESERVED_LABELS: readonly string[] = [
  "ship-ready",
  "needs-spec",
];

export interface BoardConfig {
  owner: string;
  number: number;
  statusTodo: string;
}

export interface AutoMergeConfig {
  // Diff-path allowlists for the auto-merge tier classifier - the arming
  // requirement: presence of this block is what arms the merge arm.
  docsPrefixes: string[];
  // Tail globs ("*.md") matched against root-level files only.
  docsRootGlobs: string[];
  testPrefixes: string[];
  testExclusionPrefixes?: string[];
}

export interface ShipflowConfig {
  board: BoardConfig;
  autoMerge?: AutoMergeConfig;
}

export const CONFIG_FILE = ".shipflow.json";

export const shipflowConfig: {
  board?: BoardConfig;
  autoMerge?: AutoMergeConfig;
} = {};

// Whether the effective config came from a .shipflow.json (vs an explicit
// configureShipflow override, which survives a missing file).
let fromFile = false;

// Test/embedder seam: replace the effective config outright (undefined clears).
export function configureShipflow(cfg: ShipflowConfig | undefined): void {
  delete shipflowConfig.board;
  delete shipflowConfig.autoMerge;
  fromFile = false;
  if (cfg) {
    shipflowConfig.board = cfg.board;
    shipflowConfig.autoMerge = cfg.autoMerge;
  }
}

// Apply the repo's .shipflow.json, if present. Returns whether a config file
// was found.
export function refreshConfig(worktree: string): boolean {
  const path = join(worktree, CONFIG_FILE);
  if (!existsSync(path)) {
    if (fromFile) configureShipflow(undefined);
    return false;
  }
  let raw: any;
  try {
    raw = JSON.parse(readFileSync(path, "utf8"));
  } catch (e) {
    throw new Error(
      `${CONFIG_FILE} is not valid JSON: ${(e as Error).message}`,
    );
  }
  const board = raw?.board;
  if (
    !board ||
    typeof board.owner !== "string" ||
    !Number.isInteger(board.number) ||
    typeof board.statusTodo !== "string"
  ) {
    throw new Error(
      `${CONFIG_FILE}: \`board\` requires owner (string), number (integer), statusTodo (string)`,
    );
  }
  const am = raw?.autoMerge;
  if (am !== undefined) {
    if (
      !Array.isArray(am?.docsPrefixes) ||
      !Array.isArray(am?.docsRootGlobs) ||
      !Array.isArray(am?.testPrefixes)
    ) {
      throw new Error(
        `${CONFIG_FILE}: \`autoMerge\` requires docsPrefixes[], docsRootGlobs[] and testPrefixes[]`,
      );
    }
  }
  configureShipflow({ board, ...(am ? { autoMerge: am } : {}) });
  fromFile = true;
  return true;
}

// Board accessors throw with the enablement message when wiring is off.
export function boardRef(): BoardConfig {
  if (!shipflowConfig.board) {
    throw new Error(
      `no ${CONFIG_FILE} with a \`board\` in this repo - board wiring is off; add the file to enable it`,
    );
  }
  return shipflowConfig.board;
}

export function autoMergeRef(): AutoMergeConfig | undefined {
  return shipflowConfig.autoMerge;
}

// Plugin-option kill switch for the auto-merge arm (setup-applied; survives
// config refreshes, which never touch it).
let autoMergeArmEnabled = true;

export function configureAutoMergeArm(enabled: boolean | undefined): void {
  autoMergeArmEnabled = enabled !== false;
}

export function autoMergeArmed(): boolean {
  return autoMergeArmEnabled && shipflowConfig.autoMerge !== undefined;
}

const slugCache = new Map<string, string>();

// "owner/repo" from the origin remote (SSH or HTTPS form). Git is always
// present where these tools run - they drive gh throughout.
export function repoSlug(worktree: string): string {
  const cached = slugCache.get(worktree);
  if (cached) return cached;
  let url = "";
  try {
    url = execFileSync("git", ["-C", worktree, "remote", "get-url", "origin"], {
      encoding: "utf8",
    }).trim();
  } catch {
    throw new Error(
      `cannot derive the repo identity: no origin remote in ${worktree}`,
    );
  }
  const m = /[:/]([^/:]+)\/([^/]+?)(?:\.git)?$/.exec(url);
  if (!m) {
    throw new Error(`cannot parse owner/repo from origin remote: ${url}`);
  }
  const slug = `${m[1]}/${m[2]}`;
  slugCache.set(worktree, slug);
  return slug;
}
