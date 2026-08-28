# Current Task State — ci-cd-unit-test-remediation-r1

> **Last Updated**: 2026-08-28 18:00 UTC (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. Full narrative + traps: `C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke\memory\session-resume-2026-08-27-ci-remediation.md`
> **Synopsis** (objectives / scope / end state / how CI runs after): <https://claude.ai/code/artifact/7ca88795-5790-43f3-8432-8d84bd3fdec0>

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active task** | none — between tasks |
| **Project state** | 47 tasks · 29 complete · 10 open · 7 closed · 1 partial |
| **Status** | in-progress |
| **Next Action** | **1.** Merge PRs #847, #843, #865, #866, #867 as each goes green (owner pre-approved). **2.** Then start any of tasks 091–095 — all parallel-safe, none gate cutover. |

### Critical context

**The cutover chain is blocked on exactly one thing: the shadow window.** Tasks 084, 085 and 088 were all closed on 2026-08-28, which cleared every other blocker on task 071. Check the window with `pwsh scripts/ci/shadow-window-status.ps1` — it was **6/20 PRs, 0.8/5 days, 0 false greens** at session end.

**Do not `--delete-branch` a PR that is another PR's base.** It auto-closes the stacked PR and a closed PR cannot be reopened or re-based. That killed #856 this session (recovered as #857).

---

## Open work

### A. Five PRs awaiting merge

| PR | Contents | Note |
|---|---|---|
| #847 | ArchTest guard adjudication | I repaired its two real failures — a build break (`CustomerRunGuardOptions` migrated off a client secret, test not updated) and `NETSDK1004` (`BuildL2ForCosmosGuard` built L2 without restoring → `Targets="Restore;Build"`). 131/131 ArchTests. ⚠️ worktree `C:/tmp/spaarke-auth-oid` holds this branch — I fast-forwarded it so it cannot regress the merge. |
| #843 | record-header-and-notepad-r2 | I resolved its `FAILURE-MODES.md` conflict as a renumbered union (master keeps low numbers; branch shifts AP-8→AP-9, G-12→G-13, G-13→G-14, G-14→G-15) and added a missing TOC entry. |
| #865 | **ADR-038 enforcement arm** | B4 + B1 as hard blocking guards, armed in the Tier 1 filter. Touches a frozen tier file under the banner's own "guard present but not armed" carve-out; verdict-neutral (both bans at zero). |
| #866 | Dependabot reconciliation | Closed 15 stale PRs; ignores FluentAssertions major (Xceed licence requires paid commercial use). |
| #867 | **Scope adoption** | Known issues become tasks 091–095. |

### B. The cutover chain — strictly sequential, gated on the window

`071 cutover → 075 soak (7d) → 077 retire sdap-ci.yml → 076 (30d measurements) → 090 wrapup`
(076 depends on 071, not on 077.)

### C. Adopted defect tasks — parallel-safe, gate 090 not cutover

| Task | Issue | Subject |
|---|---|---|
| 091 | #848 | Tier 2: 5 real-clock unit-test failures → `FakeTimeProvider` |
| 092 | #850 | Tier 2 Prettier not developer-reproducible (CI 1907 vs local 46) |
| 093 | #849 | Link validator: 86% of its own scan corpus is out of scope |
| 094 | #864 | Remaining 15 ADR-038 bans — arm what can arm green |
| 095 | #851 | Jest workflow, **Phase 1 plumbing only** — baseline 39 packages, then stop |

**Why these are in the project and not deferred** (owner-directed): four are defects in this project's own deliverables per the spec's Affected Areas list. #851 is a deliberate split — jest *test architecture* stays out of scope; *"no workflow runs jest at all"* is a CI gap and CI is this project.

---

## Decisions made this session (do not re-litigate)

1. **084 closed** — 246 of 247 rows were false positives. The one genuine row is `new OpenAiClient(...); Assert.NotNull(client);`.
2. **085 closed** — 1,124 renames, zero behavioral change, 30+ worktrees of merge conflicts.
3. **088 closed moot** — its guard passes; the only remaining `Microsoft.Graph` strings are comments, several *documenting* compliance. **Do not re-open on grep evidence** — `HaveDependencyOn` is IL-level and grep is not.
4. **Standing rule (in spec)** — no DELETE bucket is acted on until it has had one clean verification round over **every** row, via a code path independent of the classifier.
5. **Shadow window is 20 PRs + ≥5 days**, not 14 days. The script implements the amended rule correctly.

---

## Files modified this session

- `.github/workflows/sdap-ci.yml` — removed both auto-commit push-backs; concurrency keyed on SHA
- `.github/workflows/ci-tier1-blocking.yml` — armed 5 ADR-038 guard facts (disclosed in `projects/INDEX.md`)
- `.github/dependabot.yml` — FluentAssertions major ignored
- `tests/Spaarke.ArchTests/Adr038TestBanGuardTests.cs` — new; `SourceScan.cs` — added `TestSourceFiles()`
- `projects/ci-cd-unit-test-remediation-r1/scripts/Classify-BffUnitTests.ps1` — six rounds of hardening
- `projects/ci-cd-unit-test-remediation-r1/` — spec, TASK-INDEX, tasks 083–085/088/091–095, notes
- 54 BFF test methods deleted across 29 files (task 083)
