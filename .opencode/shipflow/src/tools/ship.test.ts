// Regression guard for issue #57: create_pr must fail (shipped:false) when
// `gh pr create` exits non-zero or prints no PR URL, and must keep passing
// the argv/cwd spawn contract through spawnText. Offline: a fake `gh` shim
// on PATH intercepts the spawn; gh auth is neutered so a bypassed shim can
// never create a real PR.
import { afterAll, afterEach, beforeAll, expect, test } from "bun:test";
import {
  chmodSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { create_pr } from "./ship.ts";

const SLUG = "fixture-slug";
const PR_URL = "https://github.com/L-Sypniewski/monogameSettlers/pull/1234";
const ARGS = {
  slug: SLUG,
  title: "test: create_pr exit codes",
  body:
    "Route create_pr's gh call through spawnText and fail on non-zero exit.\n\n" +
    "## ELI5\n" +
    "When the command that makes a pull request fails, the tool now reports the failure instead of pretending it worked.",
  head: "issue-57-test",
  base: "main",
};

type ToolResult = {
  title?: string;
  output?: string;
  metadata?: Record<string, unknown>;
};

type Fixture = { wt: string; argvFile: string; cwdFile: string };

const ORIGINAL_ENV = {
  PATH: process.env.PATH,
  GH_TOKEN: process.env.GH_TOKEN,
  GITHUB_TOKEN: process.env.GITHUB_TOKEN,
  GH_CONFIG_DIR: process.env.GH_CONFIG_DIR,
};

let fixture: Fixture | undefined;

const real = Bun.spawn;

beforeAll(() => {
  // bun 1.3.14: env-inheriting Bun.spawn children receive the startup
  // environ, not the live process.env, so the PATH/FAKE_GH_* mutations
  // below are injected explicitly at the spawn seam; the spawned processes
  // themselves are fully real.
  Bun.spawn = ((argv: string[], opts: any = {}) =>
    real(argv, {
      ...opts,
      env: { ...process.env, ...(opts.env ?? {}) },
    })) as typeof Bun.spawn;
});

afterAll(() => {
  Bun.spawn = real;
});

function setup(mode: string): Fixture {
  const wt = mkdtempSync(join(tmpdir(), "shipflow-create-pr-"));
  try {
    mkdirSync(join(wt, ".plans"), { recursive: true });
    writeFileSync(
      join(wt, ".plans", `${SLUG}.loop.json`),
      JSON.stringify({
        replan_count: 0,
        last_verify: { result: "pass", command: "fixture" },
      }),
      "utf8",
    );
    const ghConfig = join(wt, "gh-config");
    mkdirSync(ghConfig);
    const shimDir = join(wt, "shim");
    mkdirSync(shimDir);
    const argvFile = join(wt, "argv.rec");
    const cwdFile = join(wt, "cwd.rec");
    // ASCII record-separator delimits argv entries because --body spans lines.
    writeFileSync(
      join(shimDir, "gh"),
      [
        "#!/usr/bin/env bash",
        'printf \'%s\\036\' "$0" > "$FAKE_GH_RECORD_ARGV"',
        'for a in "$@"; do printf \'%s\\036\' "$a"; done >> "$FAKE_GH_RECORD_ARGV"',
        'pwd -P > "$FAKE_GH_RECORD_CWD"',
        'case "${FAKE_GH_MODE:-}" in',
        '  fail) echo "gh: token is empty" >&2; exit 1 ;;',
        "  empty) exit 0 ;;",
        `  ok) echo "${PR_URL}" ;;`,
        "  # exec, not fork: a forked sleep holds the stdout pipe open past the kill",
        "  sleep) exec sleep 5 ;;",
        "esac",
        "exit 0",
      ].join("\n") + "\n",
      "utf8",
    );
    chmodSync(join(shimDir, "gh"), 0o755);
    process.env.PATH = `${shimDir}:${ORIGINAL_ENV.PATH ?? ""}`;
    process.env.FAKE_GH_MODE = mode;
    process.env.FAKE_GH_RECORD_ARGV = argvFile;
    process.env.FAKE_GH_RECORD_CWD = cwdFile;
    process.env.GH_TOKEN = "";
    process.env.GITHUB_TOKEN = "";
    process.env.GH_CONFIG_DIR = ghConfig;
    return { wt, argvFile, cwdFile };
  } catch (e) {
    // a setup that throws mid-way never assigns `fixture`, so afterEach
    // would leak the tmp worktree
    rmSync(wt, { recursive: true, force: true });
    throw e;
  }
}

afterEach(() => {
  if (fixture) {
    rmSync(fixture.wt, { recursive: true, force: true });
    fixture = undefined;
  }
  for (const [k, v] of Object.entries(ORIGINAL_ENV)) {
    if (v === undefined) delete process.env[k];
    else process.env[k] = v;
  }
  delete process.env.FAKE_GH_MODE;
  delete process.env.FAKE_GH_RECORD_ARGV;
  delete process.env.FAKE_GH_RECORD_CWD;
});

async function run(mode: string, signal?: AbortSignal): Promise<ToolResult> {
  fixture = setup(mode);
  return (await create_pr.execute(ARGS, {
    worktree: fixture.wt,
    signal: signal ?? new AbortController().signal,
    progress: async () => {},
  })) as ToolResult;
}

test("gh non-zero exit: shipped:false, stderr surfaced, no url", async () => {
  const r = await run("fail");
  expect(r.metadata?.shipped).toBe(false);
  expect(String(r.metadata?.error)).toContain("gh: token is empty");
  expect(r.title).toContain("gh error");
  expect(r.metadata?.url).toBeUndefined();
});

test("gh exit 0 with empty stdout: shipped:false (URL-less output)", async () => {
  const r = await run("empty");
  expect(r.metadata?.shipped).toBe(false);
  expect(String(r.metadata?.error)).toContain("no PR URL");
});

test("gh success: metadata shape unchanged", async () => {
  const r = await run("ok");
  expect(r.metadata).toEqual({
    shipped: true,
    url: PR_URL,
    prNumber: 1234,
    slug: SLUG,
  });
  expect(r.title).toBe("ship_create_pr");
});

test("spawn contract: argv flags verbatim, cwd = worktree", async () => {
  await run("ok");
  const argv = readFileSync(fixture!.argvFile, "utf8").split("\x1e");
  if (argv[argv.length - 1] === "") argv.pop();
  // Bun resolves argv[0] through PATH before exec, so $0 records the
  // resolved shim path - pinning that the fake gh, not a real one, ran.
  const shimPath = realpathSync(join(fixture!.wt, "shim", "gh"));
  expect(argv.slice(0, 3)).toEqual([shimPath, "pr", "create"]);
  expect(argv).toContain("--draft");
  for (const [flag, value] of [
    ["--title", ARGS.title],
    ["--body", ARGS.body],
    ["--head", ARGS.head],
    ["--base", ARGS.base],
  ] as const) {
    const i = argv.indexOf(flag);
    expect(i).toBeGreaterThan(-1);
    expect(argv[i + 1]).toBe(value);
  }
  expect(readFileSync(fixture!.cwdFile, "utf8").trim()).toBe(
    realpathSync(fixture!.wt),
  );
});

test("abort mid-flight: shipped:false, uncertain-state wording", async () => {
  const controller = new AbortController();
  setTimeout(() => controller.abort(), 10);
  const r = await run("sleep", controller.signal);
  expect(r.metadata?.shipped).toBe(false);
  expect(String(r.output)).toContain("state unknown");
  expect(String(r.output)).toContain("verify before retrying");
});
