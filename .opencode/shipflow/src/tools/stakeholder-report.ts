import { z } from "zod";
import { defineTool } from "../define-tool.ts";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";

type PresentedEntry = { reportFile: string; presentedOn: string };
type PresentedState = Record<string, PresentedEntry>;

function statePath(worktree: string): string {
  return join(worktree, "docs", "stakeholder-reports", "presented.json");
}

function readState(worktree: string): PresentedState {
  const p = statePath(worktree);
  if (!existsSync(p)) return {};
  try {
    return JSON.parse(readFileSync(p, "utf8")) as PresentedState;
  } catch {
    return {};
  }
}

function writeState(worktree: string, state: PresentedState): void {
  const p = statePath(worktree);
  mkdirSync(dirname(p), { recursive: true });
  writeFileSync(p, JSON.stringify(state, null, 2) + "\n", "utf8");
}

export const stakeholder_report_check_presented = defineTool({
  description:
    "Read-only: for each given GitHub issue number, report whether it has already " +
    "been included in a prior stakeholder report (and which one/when). Does not mutate state.",
  args: {
    issueNumbers: z
      .array(z.string())
      .describe('Issue numbers to check, e.g. ["123", "124"].'),
  },
  async execute(args, context) {
    const state = readState(context.worktree);
    const alreadyPresented = args.issueNumbers
      .filter((n: string) => state[n] !== undefined)
      .map((n: string) => ({ issueNumber: n, ...state[n] }));
    return {
      title: "stakeholder_report_check_presented",
      output:
        alreadyPresented.length === 0
          ? "none of the given issues were presented before"
          : `already presented: ${alreadyPresented
              .map(
                (e: { issueNumber: string } & Record<string, unknown>) => `#${e.issueNumber} (${e.reportFile}, ${e.presentedOn})`,
              )
              .join(", ")}`,
      metadata: { alreadyPresented },
    };
  },
});

export const stakeholder_report_record_presented = defineTool({
  description:
    "Record that the given GitHub issue numbers were just included in a stakeholder " +
    "report - upserts today's date + the report's .md path per issue number.",
  args: {
    issueNumbers: z.array(z.string()),
    reportFile: z
      .string()
      .describe("Repo-relative path of the report's .md file."),
  },
  async execute(args, context) {
    const state = readState(context.worktree);
    const presentedOn = new Date().toISOString().slice(0, 10);
    for (const issueNumber of args.issueNumbers) {
      state[issueNumber] = { reportFile: args.reportFile, presentedOn };
    }
    writeState(context.worktree, state);
    return {
      title: "stakeholder_report_record_presented",
      output: `recorded ${args.issueNumbers.length} issue(s) against ${args.reportFile}`,
      metadata: {
        issueNumbers: args.issueNumbers,
        reportFile: args.reportFile,
        presentedOn,
      },
    };
  },
});
