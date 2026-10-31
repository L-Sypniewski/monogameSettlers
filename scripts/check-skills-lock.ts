/**
 * check-skills-lock.ts - one-directional drift guard over skills-lock.json vs .agents/skills/.
 *
 * Every lock entry must resolve to an existing .agents/skills/<name>/ directory and reproduce its
 * computedHash under the pinned digest (SHA-256 over localeCompare-sorted files, update(relativePath)
 * then update(content) with no separator - the skills CLI's folder digest, vendored from
 * vercel-labs/skills src/local-lock.ts). Skill directories without a lock entry are sanctioned
 * (hand-vendored skills) and out of scope: the lock is the authority, not the directory listing.
 *
 * Usage:
 *   bun run scripts/check-skills-lock.ts
 *
 * Exit: 0 = clean, 1 = drift findings, 2 = crash (missing/unparseable skills-lock.json).
 * CI: .github/workflows/build.yml (test job) + scripts/fast-checks.sh.
 */

import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = fileURLToPath(new URL("..", import.meta.url));
const LOCK_PATH = join(repoRoot, "skills-lock.json");
const SKILLS_DIR = join(repoRoot, ".agents", "skills");

export type SkillFile = { relativePath: string; content: Buffer };

export type LockEntry = {
  computedHash: string;
  ref?: string;
  [key: string]: unknown;
};

export type LockFile = { version: number; skills: Record<string, LockEntry> };

export type Finding = { skill: string; message: string };

export function computeDigestFromEntries(entries: SkillFile[]): string {
  const sorted = [...entries].sort((a, b) =>
    a.relativePath.localeCompare(b.relativePath),
  );
  const hash = createHash("sha256");
  for (const entry of sorted) {
    hash.update(entry.relativePath);
    hash.update(entry.content);
  }
  return hash.digest("hex");
}

// null = the skill directory does not exist; only regular files count, .git/node_modules
// directories are skipped at any depth (upstream walk semantics).
export function collectSkillFiles(dir: string): SkillFile[] | null {
  if (!existsSync(dir)) return null;
  const results: SkillFile[] = [];
  const walk = (current: string): void => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const fullPath = join(current, entry.name);
      if (entry.isDirectory()) {
        if (entry.name === ".git" || entry.name === "node_modules") continue;
        walk(fullPath);
      } else if (entry.isFile()) {
        results.push({
          relativePath: relative(dir, fullPath).split("\\").join("/"),
          content: readFileSync(fullPath),
        });
      }
    }
  };
  walk(dir);
  return results;
}

export function evaluateEntry(
  name: string,
  entry: LockEntry,
  files: SkillFile[] | null,
): Finding | null {
  if (files === null) {
    return {
      skill: name,
      message: `.agents/skills/${name}/ does not exist on disk`,
    };
  }
  const digest = computeDigestFromEntries(files);
  if (digest !== entry.computedHash) {
    return {
      skill: name,
      message: `computedHash mismatch (lock ${entry.computedHash}, disk ${digest})`,
    };
  }
  return null;
}

function run(): void {
  const lock: LockFile = JSON.parse(readFileSync(LOCK_PATH, "utf8"));

  const findings: Finding[] = [];
  const names = Object.keys(lock.skills);
  for (const name of names) {
    const files = collectSkillFiles(join(SKILLS_DIR, name));
    const finding = evaluateEntry(name, lock.skills[name], files);
    if (finding) findings.push(finding);
  }

  if (findings.length === 0) {
    console.log(
      `OK - all ${names.length} skills-lock entries reproduce on disk`,
    );
    process.exit(0);
  }
  for (const f of findings) {
    console.error(`VIOLATION ${f.skill}: ${f.message}`);
  }
  console.error(`${findings.length} skills-lock drift finding(s)`);
  process.exit(1);
}

// Guarded so the side-effecting block (process.exit) runs only when this file is the entry
// script, not when imported (e.g. by bun:test).
const isMain = process.argv[1] === fileURLToPath(import.meta.url);

if (isMain) {
  try {
    run();
  } catch (err) {
    console.error("check-skills-lock.ts crashed:", err);
    process.exit(2);
  }
}
