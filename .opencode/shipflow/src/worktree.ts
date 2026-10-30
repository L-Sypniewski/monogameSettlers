// Worktree strategy: git worktree + the consumer's sync contract
// (copyFiles/symlinkDirs/postCreate), all injectable via options.
import { execFile } from "node:child_process";
import {
  copyFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  realpathSync,
  rmSync,
  symlinkSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";

const DEFAULT_COPY_FILES: string[] = [];
const DEFAULT_SYMLINK_DIRS: string[] = [];
const DEFAULT_POST_CREATE: string[] = [];

export interface WorktreeStrategyOptions {
  sourceDirectory: string;
  copyFiles?: string[];
  symlinkDirs?: string[];
  postCreate?: string[];
}

function run(
  cmd: string,
  args: string[],
  opts: { cwd: string; signal?: AbortSignal },
): Promise<{ stdout: string; stderr: string }> {
  return new Promise((res, rej) => {
    execFile(
      cmd,
      args,
      { cwd: opts.cwd, signal: opts.signal, maxBuffer: 32 * 1024 * 1024 },
      (err, stdout, stderr) =>
        err
          ? rej(Object.assign(err, { stderr: String(stderr) }))
          : res({ stdout: String(stdout), stderr: String(stderr) }),
    );
  });
}

function shellRun(
  command: string,
  cwd: string,
  signal?: AbortSignal,
): Promise<void> {
  return new Promise((res, rej) => {
    execFile("bash", ["-c", command], { cwd, signal }, (err) =>
      err ? rej(err) : res(),
    );
  });
}

export function makeWorktreeStrategy(options: WorktreeStrategyOptions) {
  const sourceDirectory = resolve(options.sourceDirectory);
  const copyFiles = options.copyFiles ?? DEFAULT_COPY_FILES;
  const symlinkDirs = options.symlinkDirs ?? DEFAULT_SYMLINK_DIRS;
  const postCreate = options.postCreate ?? DEFAULT_POST_CREATE;

  return {
    id: "shipflow",

    async create(
      input: { sourceDirectory: string; directory: string; branch?: string },
      context: { signal: AbortSignal },
    ): Promise<{ directory: string }> {
      const src = resolve(input.sourceDirectory ?? sourceDirectory);
      const directory = resolve(input.directory);
      const branch = input.branch ?? directory.split("/").pop()!;

      // A new branch per worktree, never a silent default-branch start point;
      // an already-existing branch name falls back to checkout.
      try {
        await run("git", ["worktree", "add", "-b", branch, directory], {
          cwd: src,
          signal: context.signal,
        });
      } catch {
        await run("git", ["worktree", "add", directory, branch], {
          cwd: src,
          signal: context.signal,
        });
      }

      for (const file of copyFiles) {
        const from = join(src, file);
        if (!existsSync(from)) continue;
        const to = join(directory, file);
        mkdirSync(dirname(to), { recursive: true });
        copyFileSync(from, to);
      }

      for (const dir of symlinkDirs) {
        const from = join(src, dir);
        if (!existsSync(from)) continue;
        const to = join(directory, dir);
        // The fresh checkout materializes tracked placeholder content (the
        // dirs hold only .gitkeep); the shared source supersedes it. An
        // existing symlink is already correct.
        if (lstatSync(to, { throwIfNoEntry: false })?.isSymbolicLink()) continue;
        rmSync(to, { recursive: true, force: true });
        mkdirSync(dirname(to), { recursive: true });
        symlinkSync(from, to, "dir");
      }

      for (const command of postCreate) {
        try {
          await shellRun(command, directory, context.signal);
        } catch (e) {
          console.warn(
            `[shipflow] worktree postCreate command failed (non-fatal): ${command}: ${(e as Error).message}`,
          );
        }
      }

      // `git worktree list` reports symlink-resolved paths; returning the same form keeps create and list in agreement
      return { directory: realpathSync.native(directory) };
    },

    async remove(
      input: { directory: string; force: boolean },
      context: { signal: AbortSignal },
    ): Promise<void> {
      await run(
        "git",
        [
          "worktree",
          "remove",
          ...(input.force ? ["--force"] : []),
          resolve(input.directory),
        ],
        { cwd: sourceDirectory, signal: context.signal },
      );
      await run("git", ["worktree", "prune"], {
        cwd: sourceDirectory,
        signal: context.signal,
      });
    },

    async list(
      src: string,
      context: { signal: AbortSignal },
    ): Promise<readonly { directory: string; type: "root" | "worktree" }[]> {
      const { stdout } = await run("git", ["worktree", "list", "--porcelain"], {
        cwd: resolve(src ?? sourceDirectory),
        signal: context.signal,
      });
      const out: { directory: string; type: "root" | "worktree" }[] = [];
      for (const line of stdout.split("\n")) {
        if (line.startsWith("worktree ")) {
          out.push({
            directory: line.slice("worktree ".length),
            type: out.length === 0 ? "root" : "worktree",
          });
        }
      }
      return out;
    },
  };
}
