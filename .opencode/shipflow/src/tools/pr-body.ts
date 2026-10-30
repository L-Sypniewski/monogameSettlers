// Mechanical backstop for the ship agent's step 5.1 PR-body rule: the very
// last ## section must be the plain-language ELI5 explainer, non-empty.
import { unfencedLines } from "./auto-merge.ts";

const H2 = /^ {0,3}##\s(.*)$/;

export function bodyHasFinalEli5(body: string): boolean {
  const lines = body.split(/\r?\n/);
  let heading = -1;
  let label = "";
  for (const { index, line } of unfencedLines(body)) {
    const m = H2.exec(line);
    if (m) {
      heading = index;
      label = m[1]!;
    }
  }
  if (heading === -1 || label.trimEnd() !== "ELI5") return false;
  return lines.slice(heading + 1).some((l) => l.trim() !== "");
}
