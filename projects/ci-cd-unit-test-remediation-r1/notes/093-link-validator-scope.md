# Task 093 — markdown link validator scope (#849)

> **Date**: 2026-08-28 · **Branch**: `ci/task-093-link-validator-scope` · **Status**: complete

`scripts/validate-markdown-links.ps1` is a named deliverable of this project (FR-A03 / CICD-044).
It was reporting **1,212 broken links** across **4,210 files** in CI — a number nobody could triage,
which is operationally the same as reporting nothing.

---

## What was actually wrong

Two separate defects, and **neither was the validator's link-checking logic**, which is sound.

### 1. The scan corpus was almost entirely archival

| Area | Tracked `.md` | Governed? |
|---|---:|---|
| `projects/**` | **3,643** (79.5%) | ❌ historical project records — specs, designs, POMLs, notes. Their links legitimately rot; that is what an archival record does. |
| `.claude/` | 338 | ✅ (minus `worktrees/`, `archive/`) |
| `docs/` | 317 | ✅ |
| `knowledge/` | 138 | ✅ — see note below |
| `src/`, `tests/`, others | ~143 | ✅ |
| **Total tracked** | **4,581** | |

Worse locally: `.claude/worktrees/` is gitignored and holds **117,311 `.md` files** on this machine
(#849 estimated 4,507 — an order of magnitude low). A developer running the script locally saw
thousands of findings and reasonably concluded the tool was broken.

`.claude/archive/` was **also not excluded**, contrary to appearances: the old pattern's
`\.archive` alternation matches a path segment literally named `.archive`, which `.claude/archive`
is not.

### 2. The report was unreadable

`Format-Table -AutoSize` truncated paths to console width. A path you cannot read is a finding you
cannot fix — so even the in-scope findings were unactionable.

---

## What changed

- **`-ExcludeFromRootPattern`** (new): archival/transient areas — `projects`, `.claude/worktrees`,
  `.claude/archive`, `provisioning-runs`, `reports`. Matched against the path **relative to the
  repo root and anchored at the start**, *not* anywhere in the path.

  > That anchoring is load-bearing. An unanchored `projects` would match the absolute path of
  > every file when the caller runs `-Path projects/<name>` — a usage the script's own header
  > documents as an example — and would have silently scanned **zero** files while printing
  > "All markdown links resolved successfully". A scoping fix that fakes a green is worse than
  > the problem it replaces.

- **Explicit `-Path` into an excluded area suppresses the root exclusions.** If you point the
  script at `projects/foo` you asked for it by name. Verified: still scans (27 files, 0 broken).

- **Excluded count is always disclosed** (`99,298 file(s) excluded as archival/transient`). A
  scope reduction must never be able to look like a clean scan.

- **Report rewritten**: per-area rollup first, then one untruncated line per finding.

- **Percent-decoding** for local targets. `Sprint%202/...` would never match `Test-Path` literally.
  Both current instances are genuinely broken either way, so this changes no count today — it is
  here so the next one is not misreported.

- Full **SCAN CORPUS table in the script header**, one stated reason per exclusion.

---

## Result

| | Before | After |
|---|---:|---:|
| Files scanned (local, repo root) | 100,220 | **922** |
| Broken links | 2,563 local / 1,212 CI | **267** |

### The 267 are real — I checked

I deliberately did **not** keep excluding until the number looked good. Sampled findings:

- `docs/guides/PCF-CONTROL-DEVELOPMENT.md`, `docs/architecture/auth-boundaries.md` — genuinely
  missing files.
- `../src/client/shared/Spaarke.Auth/src/strategies/BridgeStrategy.ts#L14` — points at code
  **deleted** under ADR-028. A valuable finding, not noise.
- `.claude/CHANGELOG.md:241` — `[~/.claude/settings.json](file)`, a real authoring bug.
- The two percent-encoded targets are broken decoded as well.

`knowledge/**` (67 findings) was assessed as an exclusion candidate and **kept in**: it carries a
`REFRESH-PROCEDURE.md` + `REFRESH-LOG.md`, is actively curated, and per root CLAUDE.md §15 the
`researcher` subagent consults it *first*. Excluding it would have bought a smaller number by
hiding docs that are supposed to be current.

**267 is genuine documentation debt that the 1,212 was concealing.** Fixing those links is
doc-repair work, not validator work — a separate task, and now a possible one.

---

## Enforcement posture: unchanged (deliberate)

Stays **advisory / `continue-on-error: true`** in Tier 2, per the task's own instruction not to
make it blocking while the count is large. Recommend blocking only once the governed corpus is at
zero and held there.

## Freeze-safety

**No workflow file was touched.** CI invokes the script as `& pwsh -File $script` with **no
arguments**, so `-Path` defaults to `.` and the new corpus applies automatically. This matters:
the shadow window forbids edits to `ci-tier2-advisory.yml` while it runs, and this task needed none.

Note CI runs *with* network (no `-NoNetwork`), so its total will exceed the 267 measured locally by
however many external URLs fail, capped at `MaxExternalChecks` (200).
