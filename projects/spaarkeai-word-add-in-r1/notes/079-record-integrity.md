# Task 079 — record integrity reconciliation

> **Task**: `tasks/079-record-integrity-reconciliation.poml` · FULL · opus / xhigh · directional
> **Started**: 2026-10-02 · owner: *"yes proceed with next tasks"*
> **Status**: ✅ **DONE 2026-10-02** — every acceptance criterion met; every spec amendment owner-signed (§5).

**Dependency deviation, stated.** 079 depends on 076, 077 and 078. 077 and 078 are closed (⚠️). **076 is 🔲**,
waiting on the owner's three numbering answers. The owner asked for the next tasks, so 079 ran anyway: FR-13 is
recorded as *open, owned by 076, owner-held*, not reconciled.

---

## 1. (e) Ten POMLs that did not parse → 0 errors

`pwsh scripts/Validate-TaskPoml.ps1 projects/spaarkeai-word-add-in-r1/tasks`

| | Scanned | Clean | Errors | Warnings |
|---|---|---|---|---|
| **Before** | 86 | 50 | **10** | 32 |
| **After** | 86 | 58 | **0** | 34 |

The warnings went up by two only because 018 and 028 now parse and their existing warnings show (missing
`<justification>` / `<ui-tests>`); no new warning was introduced.

| POML | Error | Fix (recorded content preserved) |
|---|---|---|
| 005 | `rigor-reason` start tag ≠ `notes-completion` end tag | Prose quoted two tag names unescaped: `` `<rigor-reason>` `` and `` `<steps>` `` → `&lt;…&gt;`. The validator showed one; the second surfaced after the first was fixed |
| 010 | name cannot begin with ' ' | `(all < 64 KB` → `&lt;` |
| 018 | `div` start tag ≠ `completed` end tag | `` `<div>`s `` → `&lt;div&gt;` |
| 028 | name cannot begin with '>' | `` `Unmodelled<>` `` → `&lt;&gt;` |
| 053 | `relevant-files` start tag ≠ `notes` end tag | `` `<relevant-files>` `` → `&lt;…&gt;` |
| 009, 017, 019, 043, 044 — **and 018, 028** | missing `<steps>` | **Seven, not five**: 018 and 028 also have no `<steps>`, hidden behind their parse errors. These were authored without steps and executed against their goal, constraints and acceptance criteria. Each got `<steps mode="directional">` with ONE step that says exactly that, plus a comment stating it was added by 079 and is **not** a claim about how the work was done. No steps were invented after the fact |

The five fixes for malformed XML are escapes only: the rendered text is byte-for-byte what was recorded.

## 2. (d) Status drift → 0, and the index made machine-readable

**The seven** — POML `not-started` while ✅ — were resolved from git, not by picking a side. Each has a close
commit, so the POML was the stale side:

| Task | Evidence |
|---|---|
| 014 | `713710a39` docs: close 014 |
| 025 | `cb6397492` docs: close 025 |
| 031 | `8c2b50213` docs: close 031 |
| 037 | `6f29ef236` docs: close 052, 054 and 037 |
| 050 | `d7ca4e095` docs: close 050 |
| 051 | `12a0b4258` docs: close 051 |
| 054 | `6f29ef236` (as 037) |

**Found by the full pairing, not in the review's list:**

| Task | Was | Now | Why |
|---|---|---|---|
| 065 | POML "escalated — awaiting owner decision"; index ✅ | `completed` | Owner decided 2026-09-30 (`1ce8261a4`): `TargetEntity` never required, criteria 2/8 void, residual delivered by 080 |
| 057 | ➡️, POML `not-started` | ➡️ `[done]`, `completed` + reason | Handed to UAC-r2 (#1014) by owner decision 2026-09-28; nothing done here |
| 061 | ➡️, POML `not-started` | ➡️ `[done]`, `completed` + reason | Handed to UAC-r2 (#1015) 2026-09-28; **delivered there as their task 120** (their #1015 is still open — theirs to close) |
| 066 | "➡️ 080", POML "escalated — awaiting owner decision" | ➡️ `[done]`, `completed` + reason | Owner chose option A 2026-09-30; task 080 delivered it (`5d870b898`) |
| 081 | ⏸, POML `not-started` | ➡️ `[done]`, `completed` + reason | Interim carve-out, *"obsolete the moment 080 lands"*; owner chose to wait for 080, which merged and was deployed to dev 2026-10-02. **Never executed** — the row and the POML say so |
| 082 | row broken across **27 lines** (bullets inside a table cell) | one row, joined with `<br>` | It broke the table in rendering and hid the row from every parser. Content unchanged |

Every changed POML carries a comment naming the old status and the evidence. ⚠️ **Vocabulary gap**: the token set
has no "handed off / superseded"; those four use ➡️ (how) + `[done]` (closed for this project), and say which.

**The ASCII token was missing from all 86 status cells.** task-create makes it mandatory (`✅ [done]`, `🔲 [open]`,
…; FAILURE-MODES G-16 — `grep` cannot see 🔲). Added to every cell; the table shape is unchanged.

**The drift checker (UPDATE item 1).** `scripts/check-task-status-drift.ps1` on master expects the marker BEFORE the
id in the first cell (`| ✅ [done] 001 |`); this index uses task-create's own template layout (`| 001 | … | ✅ [done] |`),
so master's checker reads only the risk table (`| **002** | … |`) and reports phantom drift. **Fixing it here would
duplicate work already done**: `customer-provisioning-orchestration-r1` commit `233ff9341` (unmerged) adds exactly
this layout. Run against this index from a temporary copy (not committed): **86 POMLs, 86 rows, no drift.** Until
that merges, push-to-github Step 1.65 is red for this project for a parser reason, not a data reason.

## 3. (c) The fifteen questionable ✅ claims — adjudicated one by one

The review said "14"; its list names 15 (011 included), and 079's UPDATE item 3 adds 011 explicitly. All 15 below.
Evidence gathered by a read-only subagent; every verdict is this task's own.

| Task | Claim at issue | Evidence found | Verdict | Reason |
|---|---|---|---|---|
| 010 | Host auto-detection "must be verified live" | Decided 2026-09-09 as option B (`36bb6dc3d`): both panes pass the `Office.onReady` host; `Office.context.host` is a fallback only. Live saves worked in Word and Outlook (09-18, 09-30; platform not recorded). The index row still said "decision open" | **Supported** · desktop × web matrix **deferred to 042** | The record was stale, not the work: the row now says decided |
| 011 | JSON manifest sideloaded, desktop + web (ACs 6/7) | The 09-18 "close" was an XML re-upload. Task 078 corrected both artifacts to ⚠️ and carries ACs 6/7 to its owner install step | **Corrected already (078)** | Not reopened: 078 owns the gap and is ⚠️ with it named |
| 013 | 4 `<ui-tests>` | Explicitly "not run, not claimed" at close; ui-test 2 (new document) live-confirmed 09-18 after #997 | **Legitimately deferred to 042** (partly run) | Waiting on a live host, said so at close |
| 021 | 5 `<ui-tests>` | **No deferral recorded at close** (POML, note, row, close commit). Automated ACs met (incl. the Picklist cast defect, fixed with a real-`Entity` test) | **Legitimately deferred to 042** — deferral recorded **late, by 079** | Same category as its siblings; the omission was the record's, now fixed in the row |
| 025 | SC-6 "test asserts no new collision logic" | That test does not exist and the property is false. Behaviour is real: `OfficeCreateCollisionTests` (no bytes move, one version) + `SaveFlowCollision.test.tsx` | **Behaviour supported; SC-6 claim corrected** (FR-12 + SC-6 amended, owner-signed) | #1005 (found live 09-18) fixed by 055, deployed 09-19, live re-check → 042 |
| 026 | 5 `<ui-tests>` | Note §7: "UNVERIFIED live … same as the other Office-host UI tests" | **Legitimately deferred to 042** | |
| 027 | ACs 2–4 + 6 `<ui-tests>` (record opens, usable, edits propagate) | Note: "not run or claimed as passing" | **Legitimately deferred to 042** | FR-10's wording awaits the owner (§9) |
| 030 | FR-13 Matter number | Owner re-scoped numbering out 2026-09-11; the task record says so. The only test asserts **no** number is sent. SC-8 was recorded PASS | **Supported as re-scoped**; the **spec claim is unsupported → SC-8 now FAIL** | **Not reopened**: the gap is task 076's, which exists and is owner-held. Reopening 030 would create a second open task for one gap |
| 031 | Project number / display name | Same shape: blank by the re-scope, proxy test | **As 030** | **Real, user-visible, already owned**: a pane-created Matter or Project has a blank name (076) |
| 033 | FR-16 halves; 6 `<ui-tests>` | 077 closed all three gaps in code with gated tests; deployed 10-02; no live check since | **Corrected already (077)** · ui-tests **deferred to 042** | |
| 034 | Documents-only; 6 `<ui-tests>` | 077 added the records bridge; ADR-051 row approved | **Corrected already (077)** · ui-tests **deferred to 042** | |
| 036 | 6 `<ui-tests>` | Note: "needs manual verification in a deployed environment" | **Legitimately deferred to 042** | FR-15's Word half awaits the owner (§9) |
| 037 | Ribbon Quick Save / Share; 4 `<ui-tests>` | Explicit deferral. **The only live attempt (09-30) FAILED** — Quick Save traced to identity resolution hitting a 405, attributed to another project overwriting the shared dev BFF; the owner declined a retest | **Legitimately deferred** — ⚠️ **never passed live; possible masked defect** | Not a new finding (recorded in `042-uat-round2`); now an explicit 042 UAT item. Both deploys are current as of 10-02 |
| 040 | AC1 grep = 0 | Re-run on HEAD: the same 9 code hits, all host-shaped DATA (classified §3/§6 of the checklist); 0 `Office.context.host` reads | **AC1 reconciled — reworded** (079's own AC allows it; original kept in a comment) · AC5/AC9 live parity **deferred to 042** | `parity-checklist.md` §6.1 |
| 056 | "CI gates it" | The job fails its OWN run on any production error, but is in neither required check (`Router`; classic protection's `Build & Test (Debug)`) — it does not block a merge. The task's own ACs said "reporting gate, do not promote" | **Supported on its own ACs**; the **spec's FR-18 / SC-12 "CI gates" wording overstates** | Amendment needs owner sign-off (§9); promotion is #996 |

**Reopened: none — and why that is the honest outcome, not a softening.** Every unsupported claim falls in one of
two kinds: a successor task already owns the gap (076 for FR-13, 077 for FR-16, 078 for FR-05, 055 + 042 for
#1005), or the defect was in the record itself and is fixed in the record (SC-6, SC-8, 010's row, 021's missing
deferral, 040's AC1, 056's spec wording). Reopening the original task in the first kind would put two open tasks
on one gap.

**Escalation trigger 1** (*re-verifying a ✅ task uncovers shipped behaviour that does not match its ACs*) **did not
fire on anything new.** Three real behaviours sit behind these claims, and each was already on the record: the
blank Matter/Project name (076), #1005 (fixed by 055, not re-checked live), and 037's ribbon commands (never passed
live). All three are now explicit items with owners (`defer-issues.md` register).

## 4. (a) SC-6 and SC-8 in the 042 tally

`notes/042-uat-results.md` §4, corrected in place with the old evidence struck through:

- **SC-6** — stays **PASS** on the criterion as amended (owner-signed), with **corrected evidence**:
  `OfficeCreateCollisionTests.ColliderCreateSave_UnderAnExistingDocumentsName_RefusesBeforeAnyBytesMove_LeavingItsFileAndVersionUnchanged`
  and the Keep-both / Save-as-new-version / dismiss cases, plus `SaveFlowCollision.test.tsx`.
- **SC-8** — **PASS → FAIL.** Its evidence asserts the opposite (`…_AndNoNumber` → `AssertNoMatterNumberSent`). Owner
  and mapped fields do hold, each cited to a test.
- **Corrected tally**: 5 PASS · 4 PARTIAL · 2 BLOCKED · **2 FAIL** (was 6 / 4 / 2 / 1). The original line is kept.

## 5. (b) Spec amendments — owner sign-off

| FR | Owner answer (2026-10-02) | Written |
|---|---|---|
| **FR-12** (+ SC-6, the Assumptions line, a new ADR Tensions row) | *"Approve as drafted"* | ✅ Describes what shipped: `Fail` on create, `OFFICE_020` before any row, **Keep both / Save as new version** (the latter only for a readable document filed to the same record — #1005), orphaned-name reclaim; the false *"fixed at the shared client upload path"* struck; the path-A row cites the owner's 2026-09-17 *"BUILD IT"* |
| **FR-16** | *"make the amendment so that it's accurate to what exists and how it works"* | ✅ Documents = content similarity (KNN, ≤50, per-row trim); **records = matched by topic, not similarity** (`/api/ai/search/records` seeded by profile keywords → TL;DR → summary; no seed ⇒ no query); Outlook noun from `canGetSender`; a same-session save is found at once; Run Index's route named. Owner Clarification and the Run Index unresolved question updated |
| **FR-15** | First asked the scope; then *"yes option A; but can we have also option B (user can choose which to open?)"* | ✅ Outlook unchanged (native compose); **Word offers a choice** — (A) Spaarke's composer (`sprk_communicationpage`, compose mode, associated to the record) or (B) Outlook on the web. Supersedes the 09-15 "hidden in Word". **Build: new task 086** |
| **FR-10** (+ its ADR-050 row) | First asked for the UX; then *"yes open Spaarke (question is if this can/should be a headerless browser session or is it just full browser?)"* | ✅ Opens **in Spaarke in a browser tab**. Answer recorded in the FR: `openBrowserWindow` always opens a normal browser tab/window — Office offers no address-bar-free option (only the dialog this FR moved away from). Chosen on the owner's delegation: a **focused record page** — app navigation hidden (`navbar=off`), command bar kept for Save. ADR-050 row: no modal involved; Spike-2 question resolved. **`navbar=off` build: task 086** |
| **FR-18 / SC-12** "CI gates" | *"yes follow recommendation"* | ✅ "CI **reports**" (not a merge gate); promotion is #996, and why it is not a settings toggle (path-filtered workflow) |
| **FR-13** | *"for now just use PRJ-###### sequential; for matters MAT-######; we will manually check it — don't make it a project spike"* | ✅ Interim format added to FR-13; build is task 076 (its POML records the answers) |
| FR-05 | — | Delivered by 078 (one unified package), not amended |
| FR-13 | — | Open, task 076, owner-held |

The ADR Tensions footnote now says "the other **eight**" — it said "six" while seven such rows already existed.

## 6. 040's AC1

Reconciled as §3 says: the classification in `parity-checklist.md` §3 — which asked for main-session review — is
confirmed for all nine hits, and AC1 is reworded to what it was always testing: no `Office.context.host` read and no
host-type comparison that decides whether a feature exists. `parity-checklist.md` §6.1 has the re-run.

## 7. `defer-issues.md`

A **register** now heads the file: all 21 entries with status (GitHub read live), owner, and a **"Spec gap?"**
column — the exact question 090's trigger 2 asks. Below it, the open work that is a **task**, not a deferral (076,
083, 042's live list, 079's sign-offs, owner actions, the drift-checker dependency, the two hand-offs), so nothing
is invisible to 090. Stale entries corrected: ISS-004 (owner: project owner; the job does not block), ISS-006
(fixed by 055; live re-check in 042), ISS-011 (done), ISS-018 (live checks done 10-02).

**Spec gaps the register leaves open** — what 090's trigger 2 will see: FR-18's "CI gates" (ISS-004, until amended or
promoted), FR-12's live re-check (ISS-006), FR-08's reload (ISS-021, partly), and FR-13 (task 076).

## 8. UPDATE items

1. **Drift checker** — §2 (dependency on `233ff9341`).
2. **`TodoSourceAccessFilter.cs` comment** — said `EntityAccessFilter`'s table refuses `sprk_todo` for lack of a
   lookup column; it carries `sprk_todo` (added 2026-09-04 once `sprk_relatedtodo` was found). The conclusion was
   right; the premise is corrected (comment-only).
3. **011** — counted among the claims (§3).

**Quality gates (Step 9.5).** The only source change is item 2, an XML doc comment (well-formed `<see>`/`<c>`).
Code review: the new premise was checked against `EntityAccessFilter.cs:146-154` (the `sprk_todo` entry and its
lockstep note) — accurate; no behaviour change. ADR check: no rule touched (no code, DI, route, package or test
change). Everything else this task changed is project records.

**Also found**: master requires **two** checks, not one — the `Router` ruleset check AND classic branch protection's
`Build & Test (Debug)` (`sdap-ci.yml`), read live with `gh api` (subagent). So this project's C# tests do block a
merge while `sdap-ci.yml` exists; the review's §1 and this project's handoffs said `Router` only.

## 9. Counts — from task rows only

86 POMLs, 86 index rows (the goal-eligibility table's wave flags are not counted):

| Status | Count |
|---|---|
| ✅ `[done]` | 72 |
| ➡️ `[done]` — closed for this project (handed off / superseded / obsolete) | 4 |
| ⚠️ `[escalated]` | 5 |
| 🔄 `[wip]` | 2 (042, 079) |
| 🔲 `[open]` | 3 (076, 083, 090) |
| **Closed** | **81 of 86** |

Portfolio board #945 synced to Tasks Completed = 81, Task Count = 86.

## 10. Acceptance — all met (closed 2026-10-02)

| AC | State |
|---|---|
| Validator 0 errors, before/after recorded | ✅ 10 → 0 |
| Seven POML statuses match the index | ✅ (and 5 more) |
| SC-6 / SC-8 corrected | ✅ |
| ADR Tensions row for FR-12; FR-12/15/16 + FR-10's row amended with sign-off | ✅ all owner-signed 2026-10-02 (plus FR-18/SC-12 and FR-13's interim format) |
| 15 claims adjudicated in a table | ✅ §3 |
| 040 AC1 reconciled | ✅ |
| `defer-issues.md` owners | ✅ register |
| Counts from task rows; board synced | ✅ — after closing 079 and adding 086: **87 tasks, 82 closed** (73 ✅ + 4 ➡️ + 5 ⚠️); open 042 🔄, 076, 083, 086, 090 |
