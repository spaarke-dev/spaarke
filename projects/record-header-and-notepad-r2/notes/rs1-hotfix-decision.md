# RS-1 — Hotfix now (v1.0.21) vs wait for R2

> **Task**: [040](../tasks/040-fix-rs1-matter-summary-select.poml) · **Spec**: FR-23 / RS-1 · **Design**: §9
> **Written**: 2026-08-25 · **Status**: ⏳ awaiting owner decision (escalation fired per root CLAUDE.md §6)

---

## What is broken

`MatterHeaderPcf` **v1.0.20** — the version deployed today — names `sprk_mattersummary` in its
`useRecordFieldValues` `$select`. That column was deleted during the 2026-08-25 summary
standardization. Dataverse rejects the whole request:

```
GET /sprk_matters(<id>)?$select=sprk_matternumber,sprk_mattername,_sprk_mattertype_value,
                                _sprk_practicearea_value,sprk_matterdescription,sprk_mattersummary
→ HTTP 400  0x80060888
  "Could not find a property named 'sprk_mattersummary' on type 'Microsoft.Dynamics.CRM.sprk_matter'."

Same list with sprk_recordsummary → HTTP 200
```

Re-verified live against `spaarkedev1` on 2026-08-25. **This is not a degraded sparkle — the entire
header fails to load on every Matter record**, because one bad column name invalidates the whole
`$select`. The owner's screenshot in [`matter-record-header.jpg`](matter-record-header.jpg) shows the
control working, but it **predates the column deletion**.

## What this task already fixed (source only)

The repo is now correct: the `$select`, the summary read, the comments, the manifest string, and the
tests all reference the shared `RECORDSUMMARY_FIELD` constant. Tests 7/7 green; `build:prod` green;
bundle 62.5 KiB (was ~62.3 KiB — no meaningful change).

**Fixing source does not repair production.** The deployed control keeps returning 400 until a build
is packed and imported. That is the decision below.

---

## Option A — ship v1.0.21 now

| | |
|---|---|
| **Work** | Version bump ×5 (ADR-020), pack, import, publish, hard refresh |
| **Repairs production** | ✅ immediately |
| **Unblocks task 002** | ✅ the runtime half (see below) |
| **Wasted effort** | The control is retired by [task 081](../tasks/081-retire-matterheaderpcf.poml) anyway |

## Option B — wait for R2

| | |
|---|---|
| **Work** | None now |
| **Repairs production** | ❌ Matter headers stay broken for weeks |
| **Unblocks task 002** | ❌ see below |
| **When** | Task 080 migrates Matter **last** — end of a 16-link critical path |

---

## The task-002 consequence (this is the part that isn't obvious)

Task 002 captures the parity baseline that task 080 later regression-tests against. It splits in two:

- **Static / visual half — already captured.** [`matter-parity-baseline.md`](matter-parity-baseline.md)
  was seeded from the owner's pre-deletion screenshot: 3-column layout, spans, grey read cells,
  em-dash empty state, pill lookups, footer placement. This half does **not** depend on the decision.
- **Runtime half — blocked without a rendering deployed control.** Four behaviours a screenshot
  cannot capture: form-buffer dirty state **with no re-render flash** (the v1.0.7 fix), the 25%×35%
  Notepad modal, the `openTodos` SmartTodo filter, and the outstanding dark-mode capture. The PCF
  test harness has no real `Xrm.Page`, so the form-buffer behaviour in particular cannot be
  exercised locally — it needs a live form.

Per [`TASK-INDEX.md`](../tasks/TASK-INDEX.md), **002 is a blocking hub for all of Phase 1 and Phase 2**.
So Option B does not merely leave production broken — it stalls R2's critical path at its first hub,
or forces 002 to ship a weaker baseline built from code-reading plus R1's notes.

---

## ⚠️ Honest risk on Option A — the hotfix is less surgical than it looks

The source change is six references. **The shipped bundle is not.** v1.0.20 was packed months ago;
a v1.0.21 pack compiles against `@spaarke/ui-components` **today** (the lockfile still recorded
`2.3.0`; the shared lib is now `2.4.0`, with `pdfjs-dist` and an added `@spaarke/auth` dep among the
drift). So a "one-line hotfix" would actually ship however much shared-lib change has accumulated
since v1.0.20.

Mitigating evidence, not proof: the bundle size is essentially unchanged (62.5 KiB vs R1's 63,812
bytes), which is inconsistent with large behavioural drift; the MatterHeader suite is green; and the
POML's ui-tests exercise the main surface (header renders, retrieve returns 200, sparkle popover,
dark mode). Size parity is not content parity — the ui-tests are what would actually catch a
regression, and they must be run if Option A is chosen.

---

## Recommendation — **Option A, ship v1.0.21**

Three reasons, in order of weight:

1. **Production is broken for every Matter user right now.** Weeks of a dead header on the primary
   entity is a materially worse outcome than one throwaway deploy.
2. **It unblocks R2's own critical path.** 002 gates Phases 1 and 2; the runtime capture needs a
   live control.
3. **The change is small and the bundle is measurably stable.** Risk concentrates in the shared-lib
   drift above — which the ui-tests are designed to catch, and which R2 will have to absorb at task
   080 regardless.

The counterargument worth stating: this is disposable work on a control with a scheduled retirement.
That is true, and it is outweighed by (1).

---

## Decision

| Field | Value |
|---|---|
| **Decision** | ✅ **Option A — ship v1.0.21** |
| **Decided by** | Ralph (owner), in-session |
| **Date** | 2026-08-25 |
| **Rationale** | Accepted the recommendation as written: production breakage on the primary entity outweighs the disposable-work objection, and it unblocks task 002's runtime capture, which gates Phases 1–2. |

**Import model**: the owner imports the packed solution to Dataverse manually; this task produces the
`.zip` and hands over the path. The POML ui-tests therefore run **after** the owner's import, not as
part of the pack step.

**If Option A is chosen** → bump to 1.0.21 in all 5 ADR-020 locations, deploy via `pcf-deploy`, run
both POML ui-tests, then task 002 captures the runtime half normally.

**If Option B is chosen** → no version bump, no deployment; this branch carries source/test/manifest
changes only. Task 002 and [`TASK-INDEX.md`](../tasks/TASK-INDEX.md) must be annotated that the
runtime half of the baseline cannot come from the deployed control, and 080's parity gate narrows
to what the static baseline plus code-reading can support.
