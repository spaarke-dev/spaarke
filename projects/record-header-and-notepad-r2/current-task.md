# Current Task State — record-header-and-notepad-r2

> **Last Updated**: 2026-08-25 (by `context-handoff` — end of planning session)
> **Recovery**: Read "Quick Recovery" first. Then [`CLAUDE.md`](CLAUDE.md), then [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none in progress — planning complete, implementation not started |
| **Phase** | 2 → 3 boundary. 30 tasks generated and validated. |
| **Status** | ready to execute |
| **Next Action** | **`work on task 040`** → invokes `task-execute` on [`tasks/040-fix-rs1-matter-summary-select.poml`](tasks/040-fix-rs1-matter-summary-select.poml). Fixes live production breakage; no dependencies. Then 001 + 002. |
| **Blocked by** | Nothing. |

### Critical context (3 sentences)

R2 replaces the withdrawn four-per-entity-PCF plan with **ONE configuration-driven `Spaarke.Records.RecordHeader`** control, rolled out to six entities with Matter migrated last as the parity regression test. Every schema fact in `spec.md` §9 was **live-verified against `spaarkedev1`** — the original field lists were substantially wrong, and verification also surfaced two live production breakages that are now tasks 040/041. Nothing has been implemented; the branch contains design + spec + plan + tasks only.

### 🔴 Start here and why

**Task 040 first.** The shipped `MatterHeaderPcf` v1.0.20 `$select` names `sprk_mattersummary`, deleted during the 2026-08-25 summary standardization. **Re-verified live 2026-08-25**: that exact query returns **HTTP 400**; swapping to `sprk_recordsummary` returns 200. The whole header fails, not just the sparkle. It has no dependencies and blocks task 002's runtime capture.

### Files modified this session

| File | Purpose |
|---|---|
| `plan.md` | **NEW** — 7-phase WBS, discovered resources, parallel groups, DoD |
| `tasks/` (30 `.poml` + `TASK-INDEX.md`) | **NEW** — full task decomposition |
| `notes/matter-parity-baseline.md` | **NEW** — parity baseline seeded from the owner screenshot |
| `notes/matter-record-header.jpg` | **NEW** — owner-supplied v1.0.20 screenshot (pre-deletion) |
| `../INDEX.md` | R2 registry row added |
| `current-task.md` | this handoff |

All committed — see the commit referenced below. **Working tree is clean.**

---

## Where things stand

### Artifact status

✅ `design.md` · ✅ `spec.md` (27 FR / 11 NFR / 24 criteria) · ✅ `plan.md` · ✅ `tasks/` (30 POMLs) · ✅ `TASK-INDEX.md` · ✅ `projects/INDEX.md` row
⏳ Not run: task execution · portfolio registration (`/devops-project-register` — README's pointer still reads "TBD") · no PR opened

### Pipeline run (2026-08-25)

`/project-pipeline` Steps 0–3, stopped before branch/commit/execution per operator choice. Pre-flight caught the branch **163 commits behind `origin/master`**; verified the drift touched **zero** R2 dependency paths, then merged clean. Build green (0 errors, 7 warnings).

**Validation**: `scripts/Validate-TaskPoml.ps1` → **PASS**, 30 scanned, **0 errors**, 30/30 well-formed XML. The 10 warnings are triaged in `TASK-INDEX.md` — all are the `role="new"` heuristic firing on **test files and notes artifacts**, which §11 does not govern. Do not "fix" them by adding hollow `<justification>` blocks.

### ⚠️ Traps the next session must respect

1. **040 before 002.** Task 002 captures the Matter parity baseline, but the header currently 400s. The static/visual half is already captured in `notes/matter-parity-baseline.md` from the owner screenshot; the **runtime** half (dirty-state-no-flash, Notepad modal, openTodos filter) needs 040 shipped first.
2. **File collisions across parallel groups** — 021 and 040 both edit `MatterHeaderView.tsx`; 022 owns `hooks/index.ts`; 015 owns `fields/index.ts`. Full table in `TASK-INDEX.md` § "Cross-group file collisions".
3. **`npm run build:prod`**, never `npm run build`, for PCF.
4. **Task 086 is main-session-only** — it writes `.claude/`, where sub-agents cannot.
5. **Dark-mode baseline still missing** for task 080's parity gate.

---

## Decisions Log

| Date | Decision |
|------|----------|
| 2026-08-21 | One configurable control replaces four cloned PCFs |
| 2026-08-21 | JSON-on-manifest config; `sprk_headerconfiguration` table explicitly rejected |
| 2026-08-21 | DEF-06 + DEF-08 dropped from R2 |
| 2026-08-22 | Control identity option B reaffirmed after the corrected trade; forms ship inside a transported solution; metadata reuses `IDataverseClient` (extended with `targets`) |
| 2026-08-24 | JSON-only config confirmed; retire `MatterHeaderPcf` on delivery; §9 rewritten from live schema; per-entity layouts confirmed; `BooleanField` kept; skeleton takes a `columns` prop; em-dash `''` everywhere (**required marker NOT adopted** — consequence documented in design §6.1) |
| 2026-08-25 | Summary field standardized to **`sprk_recordsummary`** (avoids collision with Microsoft OOB "AI summary"); columns already created by owner → R2 does **no** schema work |
| 2026-08-25 | Lookups use the **OOB `Xrm.Utility.lookupObjects` picker** — deletes the custom OData search builder rather than hoisting it; Matter's lookup interaction changes, so the parity criterion excludes it |
| 2026-08-25 | **`sprk_agreement`** added as a sixth entity; owner created its main form and added `sprk_regardingagreement` to `sprk_todo` + `sprk_memo` → R2's only toolbar-map change |
| 2026-08-25 | `+ New` quick-create in the OOB picker is a **target-entity** Dataverse setting, out of R2 scope; recommended: enable for `contact`, leave the five taxonomy tables off |

---

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 1 | Ship a **v1.0.21 hotfix** for RS-1 now, or wait for R2's control? R2 fixes it by construction but is weeks out; the header is broken today. | Ralph | Task 040 (escalates by design) |
| 2 | PCF starting version — `1.1.0` assumed for the renamed control. | Ralph | Task 033 |
| 3 | Register on the portfolio board? README pointer still reads "TBD". | Ralph | Any time — `/devops-project-register` |
| 4 | Required-marker gap: non-text renderers show no `*` (D-10). Revisit if UAT flags it. | Ralph | Post-UAT |

---

## Resume commands

| Intent | Say |
|---|---|
| Start implementing | `work on task 040` |
| Reorient first | `where was I?` → `project-continue` |
| See the task board | open [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) |
| Understand the design | [`design.md`](design.md) §5 (config model) · §9 (verified schema) |
