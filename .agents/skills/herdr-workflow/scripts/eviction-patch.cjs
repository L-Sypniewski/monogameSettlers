// Byte-patch opencode v2's location-eviction idle TTL in the shipped binary.
//
// WHY: opencode v2's background service runs a LocationActivity sweep that
// interrupts EVERY active session in a location once its idle TTL (60 minutes
// by default) lapses, then logs "location services evicted" (bundle
// decompile; upstream anomalyco/opencode#51343 tracks the same mechanism
// killing pending question forms). Long unattended turns - a 73-minute
// subagent call, a turn blocked on a question - read as "idle" to the sweep
// and die mid-flight. The shipped build exposes no config knob for the TTL,
// so this patch rewrites the literal in place.
//
// WHICH literal: the bundle carries the TTL twice - as JS source text
// (`e.timeToLive??"60 minutes"`) AND as a NUL-delimited entry in the
// precompiled string table that the runtime actually executes. Patching only
// the source text is inert (a service started on a source-only patch still
// evicts at +61 min). The table entry is the knob; the source text is
// rewritten too so both copies stay consistent.
//
// Safety contract (invoked from lib.sh hr_launch, inside the launch pane):
//   - byte-length-preserving replacement: no bundle offsets shift
//   - applied only when the pattern occurs EXACTLY once; any drift (version
//     bump changed the literal) warns and skips - launches never break
//   - preflight: skipped unless the target is stat-stable AND executes
//     cleanly - never freeze a download-in-progress as the installed binary
//   - always exits 0: warn-and-continue, the patch is an enhancement
//   - HERDR_EVICTION_PATCH_TARGET overrides the binary path (tests)
const fs = require("fs");
const { spawnSync } = require("child_process");

const target =
  process.env.HERDR_EVICTION_PATCH_TARGET ||
  "/home/vscode/.opencode/bin/opencode";
const SRC_FROM = 'e.timeToLive??"60 minutes"';
const SRC_TO = 'e.timeToLive??"9999 weeks"';
const TBL_FROM = "\x0060 minutes\x00";
const TBL_TO = "\x009999 weeks\x00";

try {
  if (
    Buffer.byteLength(SRC_FROM) !== Buffer.byteLength(SRC_TO) ||
    Buffer.byteLength(TBL_FROM) !== Buffer.byteLength(TBL_TO)
  ) {
    console.log(
      "[herdr] eviction-patch: internal error - replacement length differs, skipping",
    );
    process.exit(0);
  }
  // Preflight: a download-in-progress or truncated binary must never be
  // patched - the copy+rename below would freeze the partial state as the
  // installed binary. Stat-stability catches an active write; the exec
  // probe catches a stable-but-incomplete file. Real binaries are >100 MB;
  // the test fixtures are tiny text files and skip the probe.
  // Exits here are safe: no temp copy exists yet.
  const s1 = fs.statSync(target);
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1500);
  const s2 = fs.statSync(target);
  if (s1.size !== s2.size || s1.mtimeMs !== s2.mtimeMs) {
    console.log(
      "[herdr] eviction-patch: target still changing (installer writing?) - skipping this run; the path unit retries on the next binary change",
    );
    process.exit(0);
  }
  if (s1.size >= 1024 * 1024) {
    const probe = spawnSync(target, ["--version"], { timeout: 30000 });
    if (probe.error || probe.status !== 0) {
      console.log(
        "[herdr] eviction-patch: target does not execute cleanly (incomplete download?) - skipping; re-run the opencode installer",
      );
      process.exit(0);
    }
  }
  // Never write the target in place: the running service EXECUTES that file,
  // and Linux refuses open-for-write on a busy executable (ETXTBSY). Patch a
  // same-dir copy and rename over the entry instead - the running process
  // keeps the old inode, the next start picks up the patched file.
  // NOTE: no process.exit() inside the try - it skips finally and would leak
  // the copy, which retriggers the host-side systemd path unit in a loop.
  // pid-unique: the host-side systemd path unit can fire while a manual run
  // is mid-flight; a shared tmp name made the two runs rename each other's
  // copy away (ENOENT).
  const tmp = `${target}.herdr-patch.${process.pid}.tmp`;
  let outcome = "";
  try {
    fs.copyFileSync(target, tmp);
    const body = fs.readFileSync(tmp).toString("latin1");
    const hits = body.split(TBL_FROM).length - 1;
    const patched = body.split(TBL_TO).length - 1;
    if (hits === 0 && patched >= 1) {
      outcome = "[herdr] eviction-patch: already patched - nothing to do";
    } else if (hits !== 1) {
      outcome = `[herdr] eviction-patch: expected exactly 1 occurrence of the TTL string-table entry, found ${hits} - skipping (opencode version drifted; unattended turns stay exposed to hourly eviction interrupts)`;
    } else {
      fs.chmodSync(tmp, fs.statSync(target).mode);
      let next = body.replace(TBL_FROM, TBL_TO);
      // source text: optional (a build may ship bytecode only), rewritten for consistency
      if (next.split(SRC_FROM).length - 1 === 1) next = next.replace(SRC_FROM, SRC_TO);
      fs.writeFileSync(tmp, Buffer.from(next, "latin1"));
      fs.renameSync(tmp, target);
      outcome =
        "[herdr] eviction-patch: applied - location idle TTL 60 minutes -> 9999 weeks (interrupt-on-eviction effectively disabled)";
    }
  } finally {
    try {
      fs.unlinkSync(tmp);
    } catch {
      // rename consumed it, or it never existed
    }
  }
  console.log(outcome);
} catch (err) {
  console.log(
    `[herdr] eviction-patch: failed: ${err && err.message ? err.message : err}`,
  );
}
