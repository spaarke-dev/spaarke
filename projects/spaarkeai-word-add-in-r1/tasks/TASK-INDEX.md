# Task Index — `spaarkeai-word-add-in-r1`

> **Generated**: 2026-09-04 by `/project-pipeline` (initialize-only)
> **Total**: 38 tasks across 5 phases (028 added 2026-09-08 — finding F-h; 009 + 017 added 2026-09-09 — jest harness + RTL alignment)
> **Status legend**: 🔲 not started · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked · ⏭️ deferred

**Execute via `task-execute` only.** Never read a POML and implement manually (root CLAUDE.md §4).

---

## Phase 0 — De-risk and baseline

Gates most of Phases 1–3. Do not size Phase 1 until this closes.

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 001 | Worktree bootstrap and true typecheck baseline | ✅ | MINIMAL | sonnet / medium | — | none |
| 002 | **Spike-1**: `document.url` shape for SPE files in Word desktop | 🔲 | STANDARD | opus / high | P0-spikes | none |
| 003 | **Spike-2**: Office Dialog API for opening a record | ✅ | STANDARD | sonnet / high | P0-spikes | none |
| 004 | **Spike-3**: can a task pane open the Copilot pane (timeboxed) | ✅ | MINIMAL | sonnet / medium | P0-spikes | none |
| 005 | **Spike-4**: does the add-in save path share the shipped collision semantics | ✅ | STANDARD | opus / high | P0-spikes | none |
| 006 | FR-18: clear typecheck debt in `shared/taskpane` — ⚠️ re-scope to production types (B1) | ✅ | FULL | sonnet / high | P0-typecheck | 001 |
| 007 | FR-18: clear typecheck debt in `shared/adapters` + `shared/services` — ⚠️ re-scope (B1) | ✅ | FULL | sonnet / high | P0-typecheck | 001 |
| 008 | FR-18: clear typecheck debt in `outlook/` (**`word/` has zero**) — re-scoped (B1) | 🔲 | FULL | sonnet / high | P0-typecheck | 001 |
| 009 | Repair the jest harness (jest-dom) + re-measure test-file debt | ✅ | FULL | sonnet / high | P0-typecheck | 001 |
| 017 | Align `@testing-library/react` to v16 (React 19) + re-measure suite | 🔲 | FULL | sonnet / high | — | 009 |

> ✅ **Operator decisions 2026-09-09 (post-wave).** **(1) FR-18 is MET** — production typecheck 88 → **0**
> (006 = 73, 007 = 11, 008 = 4). **(2) The 289 test-file errors are CONSCIOUSLY ACCEPTED**, trigger to
> revisit = *when a build surfaces them*. Verified inert: no CI job typechecks `office-addins`
> (`sdap-ci.yml` runs `tsc --noEmit` for `Spaarke.AI.Widgets` only), test files are not in webpack's
> graph (which is why the build is green today WITH all 289 present), and `ts-jest isolatedModules` is
> transpile-only so they cannot fail a test run. They surface only on a manual `npm run typecheck`.
> **(3) React 19 stays; `@testing-library/react` aligns to it** — PlaybookBuilder (^16.0.0) and
> SemanticSearch (^16.1.0) already run RTL 16 against React 19; `office-addins` on ^14.2.1 was the sole
> outlier. PCF (React 16.14 / RTL 12.1.5) is a separate correct lane. Task **017** executes the
> alignment; it resolves 009's escalation trigger 2.

> ✅ **UNBLOCKED 2026-09-09 by operator decision — A1 + B1.**
> **(A1)** The 11 foreign `Spaarke.Communication.Components/provenance.ts` errors are fixed by commit
> `6b987bb31`, already on this branch and already building. **FR-18's acceptance narrows to the
> `office-addins` package** — `npm run typecheck` need not be clean for foreign files pulled in by the
> `@spaarke/communication-components` path alias.
> **(B1)** **FR-18 means production types only (~99 errors), NOT all 395.** 296 of 395 (75%) sit in test
> files whose suite is already red (13/21 suites failing on a missing `jest-dom` registration) — a
> separate defect that must NOT be entangled with FR-18.
> **RE-SCOPED + re-measured 2026-09-09.** Post-`6b987bb31` the package measures **384** errors (not 395 —
> the 11 foreign ones are gone, confirming A1): **296 test-file · 88 production**. FR-18 now owns the 88.
> Directory boundaries were KEPT rather than merged, because each task carries a protection worth
> preserving (008 guards `WordHostAdapter.getCompressedFile()` for task 010; 007 guards the dead
> `HostAdapterFactory` for F-e). Production split: **006 = 73 · 007 = 11 · 008 = 4**.
> ⚠️ **`word/` now has ZERO production errors** — 008 is outlook-only and must not manufacture scope.
> All three are forbidden from touching test files or trying to make the jest suite pass.
> **New task 009** owns the jest-dom harness gap and re-measures the 296 test-file residual — leaving it
> unowned would have been exactly the burial the deferral policy forbids. 006/007/008/009 are mutually
> disjoint and dispatch in parallel.
>
> ✅ **009 CLOSED 2026-09-09 — hypothesis PARTIALLY confirmed, materially incomplete.** Task 001's
> "missing jest-dom registration" hypothesis was correct in kind but wrong in size: `@testing-library/jest-dom`
> was never installed at all (not just unregistered), and fixing it alone moved suites 13→12 and tests
> 57→41 failed (of 226) — 28%, not "a large share" — with **zero** effect on the typecheck count (jest
> runs tests transpile-only via `ts-jest isolatedModules`, decoupled from `tsc`). A **second, previously
> unknown harness gap of the identical class** (`@testing-library/user-event` imported by 4 suites, never
> installed) had to be found and fixed to unblock those suites structurally. Even with both installed,
> **12 of 21 suites remain red** — dominant cause is a **React 19 / `@testing-library/react@14`
> peer-declared-for-React-18 mismatch** (`useAnnounce.test.ts`, 16/16 tests, `NotFoundError: The node to
> be removed is not a child of this node.`) — **escalation trigger 2 fired**; a testing-library major
> bump was NOT performed (package-graph decision, outside this task's authority). Re-measured test-file
> typecheck: **289** (was 296) — the -7 is **-4 confirmed (user-event TS2307s)** + **-3 concurrent drift**
> from 006/007/008 editing `OutlookAdapter.ts`/`office-js.ts` live in the shared worktree during this
> task's window, not this task's effect. **Recommendation: the residual needs its own task(s)** — a
> React-19/testing-library major-version decision, a separate test-suite repair task for the ~12 failing
> suites' genuine mock/assertion defects, and an explicit operator decision on the still-unowned 289-error
> test-file typecheck debt (not folded into 006/007/008, which are forbidden from touching test files).
> Full record: [`notes/009-jest-harness-repair.md`](../notes/009-jest-harness-repair.md).
>
> <details><summary>Original block reason (resolved — retained for the record)</summary>
>
> ⛔ **006/007/008 were BLOCKED pending an operator decision.** Task 001's escalation trigger 3 fired: the UNASSIGNED bucket contains 11 errors in `../shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` — **outside the `office-addins` package**, pulled in by the `@spaarke/communication-components` path alias. FR-18's "typecheck clean" cannot be met by 006+007+008 alone. Two further findings change the decomposition: the split measures **309 / 45 / 4** (not balanced), and **75% of the debt (296/395) is in test files** whose suite is already red (13/21 suites failing on a missing `jest-dom` registration). See [`notes/typecheck-baseline.md`](../notes/typecheck-baseline.md) § Recommendations.
>
> </details>

> ✅ **005 is CLOSED** (core findings gathered as a review repair 2026-09-08; sections 8-18 — client
> side, full 9-hop server trace, small-vs-large payload, expanded comparison table, task 094 overlap
> boundary, task 025 implementation sketch, open questions — added and every citation independently
> re-verified under formal `task-execute` closure, 2026-09-08).
> [`notes/spikes/spike-4-collision-path.md`](../notes/spikes/spike-4-collision-path.md) confirms finding **F-a**:
> the add-in rides `POST /api/office/save` → the **no-policy** `UploadSmallAsync` overload, which hard-codes
> `ConflictBehavior.Replace` ([`UploadSessionManager.cs:103`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L103)).
> UAC-r2's shipped `Fail` default is on the client/OBO path the add-in does not use, so **FR-12's
> "consume, do not rebuild" premise is false** and its acceptance criterion cannot be met as written.
> A commit implementing a collision fix ahead of this spike (`45f45f626`) was **reverted** (`0b68943d1`); report §7.
>
> ✅ **Operator decisions, 2026-09-08** (report §5): **(1)** path **C → B** — build FR-11 (023 → 024) first,
> re-measure the residual collision surface, then amend FR-12 against what is left. No exception is granted for
> new Office-path collision logic ahead of FR-11; **025** is re-scoped and now depends on 023/024.
> **(2)** **F-h is owned by r1** as task **028**, and its fix is **host-neutral** — keyed on `SaveContentType`
> (`Email`/`Attachment` immutable, `Document` editable), never on Word-vs-Outlook. Host divergence belongs in the
> client adapters, never in save or dedup semantics.

**Gate**: typecheck clean · four spike reports in `notes/spikes/` · FR-01, FR-12 and FR-20 scope decided.

---

## Phase 1 — Foundation

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 010 | FR-04: consolidate onto one Word adapter via `HostAdapterFactory` | 🔲 | FULL | opus / xhigh | P1-a | 006, 007, 008 |
| 011 | FR-05: migrate Word to the unified JSON manifest | 🔄 | STANDARD | sonnet / high | P1-a | none |
| 012 | FR-01 server: document-identity resolver extending `/api/documents` | 🔲 | FULL | opus / high | — | 002 |
| 013 | FR-01 client: `getDocumentUrl` capability and identity threading | 🔲 | FULL | sonnet / high | — | 010, 012 |
| 014 | FR-02: server-side custom XML part GUID stamp (forward-only) | 🔲 | FULL | opus / high | P1-b | 012 |
| 015 | FR-03: Save\|Find tab shell, enable navigation in Word | 🔲 | FULL | sonnet / high | P1-b | 010 |
| 016 | Un-skip `/api/office/save` contract tests + cover the identity route | 🔲 | FULL | sonnet / high | — | 012 |

> 🔄 **011 is build-verified but NOT formally closed.** `word/manifest.json` is authored, `webpack.config.js`
> parameterizes it (id/resource/base-URL, mirroring Outlook's mechanism exactly), the WordApi 1.1→1.3
> mismatch is reconciled with cited evidence (`Word.DocumentProperties` is WordApi 1.3, forced by
> `WordAdapter.getItemId()`/`getSubject()`), both manifests are version-bumped, and `npm run build` /
> `npm run typecheck` are clean. **Acceptance criteria 6/7 (sideload verify on Word desktop + Word on the
> web) are UNVERIFIED** — this agent had no interactive Office host or browser tool available. `word-manifest.xml`
> is retained unchanged in behavior pending that verification. See
> [`notes/011-word-manifest-migration.md`](../notes/011-word-manifest-migration.md).

**Gate**: a Spaarke-sourced document is identified end-to-end · both hosts render both tabs · `/api/office/save` has executing tests.

---

## Phase 2 — Save flow

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 020 | FR-06: filename defaults to Document Name, editable in-pane | 🔲 | FULL | sonnet / high | P2-a | 015 |
| 021 | FR-07: Description becomes Profile, populated from the record | 🔲 | FULL | sonnet / high | P2-a | 013, 015 |
| 022 | FR-08: Generate Profile button and BFF trigger | 🔲 | FULL | sonnet / high | P2-a | 013 |
| 023 | FR-11 server: make `ExistingDocumentId`/`IsNewVersion` real | 🔲 | FULL | opus / xhigh | — | 012 |
| 024 | FR-11 client: default to version; override routes link/graduate | 🔲 | FULL | opus / high | — | 023, 013 |
| 025 | FR-12: surface collision handling per the Spike-4 outcome — ⚠️ **needs re-scope**, premise falsified | ⛔ | FULL | sonnet / high | — | **005**, 023, 024 |
| 026 | FR-09: related-to record card honoring the two-slot model | 🔲 | FULL | sonnet / high | P2-b | 013 |
| 027 | FR-10: open the related record and the Document record | 🔲 | FULL | sonnet / high | P2-b | 003, 026 |
| 028 | 🔴 **F-h/NFR-08**: editable Office saves must link/graduate, never immutable-suppress | ✅ | FULL | **opus / xhigh** | — | none |

**Gate**: identified document saves as a version, not a duplicate row · override creates a linked copy · profile displays · record card opens the record.

---

## Phase 3 — Surfacing Spaarke

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 030 | FR-13: shared server-side creation service (**Matter**) | 🔲 | FULL | opus / xhigh | — | 012 |
| 031 | FR-13: Project creation completeness + QuickCreate routing | 🔲 | FULL | opus / high | — | 030 |
| 032 | **FR-16a: per-row authorization on the similarity surface** | ✅ | FULL | opus / xhigh | — | none |
| 033 | FR-16b: Find view three-state gating and Run Index | 🔲 | FULL | sonnet / high | — | 032, 015, 013 |
| 034 | FR-16c: Find results, lazy-scroll, records bridge decision | 🔲 | FULL | sonnet / high | — | 033 |
| 035 | FR-14: Add To Do carrying document **and** related record | 🔲 | FULL | sonnet / high | P3-c | 013, 026 |
| 036 | FR-15: Send Email via Outlook with document + record links | 🔲 | FULL | sonnet / high | P3-c | 026 |
| 037 | FR-17: wire `quickSave` and `shareDocument` ribbon commands | 🔲 | FULL | sonnet / high | P3-c | 011, 010 |

**Gate**: a pane-created Matter is complete · Find returns permission-trimmed results with a **passing negative test**.

---

## Phase 4 — Parity, deploy, close

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 040 | FR-19: Outlook parity pass and capability-gating audit | 🔲 | FULL | sonnet / high | — | 033, 035, 036 |
| 041 | NFR-09: per-env Entra SPA redirects + deploy-workflow trigger | 🔲 | STANDARD | opus / high | — | 011 |
| 042 | Deploy the add-in and BFF; run UAT against the acceptance set | 🔲 | STANDARD | sonnet / high | — | 040, 041 |
| 090 | Project wrap-up, lessons learned, `/test-diet` gate | 🔲 | MINIMAL | sonnet / medium | — | 042 |

---

## Parallel execution groups

Dispatch each subagent at its POML's `<model-tier>` and `<effort>`. **Max 6 concurrent.** Verify the build between waves (root CLAUDE.md / project-pipeline Step 5): `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed; `npm run build` in `src/client/office-addins` if any `.ts`/`.tsx` changed (**there is no `build:prod` here** — `build` is the production build).

| Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|
| W0 | 001 | — | Serial. Sizes W2. |
| W1 | 002, 003, 004, 005 | — | 4 independent spikes. Can run alongside W0. |
| W2 | 006, 007, 008 | 001 | Disjoint directories. |
| W3 | 010, 011 | W2 · (011 needs nothing) | Disjoint: adapters vs manifest+webpack. |
| W4 | 012 | 002 | Serial — gated on Spike-1. |
| W5 | 013, 016 | 010, 012 | Disjoint: client wiring vs tests. |
| W6 | 014, 015 | 012 / 010 | Disjoint: server stamp vs tab shell. ⚠️ **014 appends to the same contract-test file as 016** — the W5→W6 ordering keeps them apart; do not co-schedule 014 and 016. |
| W7 | 020, 021, 022 | 013, 015 | Disjoint views. |
| W8 | 023 → 024 | 012 | **Strictly serial** — data-integrity change. |
| W9 | 026 → 027 | 013 / 003 | Serial. |
| W10 | 025 | **005** | ⛔ Blocked until Spike-4 closes. `parallel-safe=false`. |
| W11 | 030 → 031 | 012 | Serial. |
| W12 | 032 → 033 → 034 | — | **Strictly serial.** 032 is `parallel-safe=false`. |
| W13 | 035, 036, 037 | 013, 026, 011, 010 | Disjoint. |
| W14 | 040 | 033, 035, 036 | Serial. |
| W15 | 041 → 042 → 090 | 040 | Serial. Deploy is CI-only. |

### Goal-eligibility (`/goal` wave loop)

| Wave | Eligible | Reason |
|---|---|---|
| W2 (006–008) | ✅ | Machine-verifiable end-state (`npm run typecheck` clean), 3 well-specified low-ambiguity tasks. Condition: *"`npm run typecheck` in `src/client/office-addins` exits 0 and `npm run build` is green."* |
| W7 (020–022) | ✅ | 3 well-specified view tasks with closed acceptance sets. |
| W13 (035–037) | ✅ | 3 disjoint adaptation tasks. |
| W1 (spikes) | ❌ | Investigation — no machine-verifiable end-state; outcomes need operator judgment. |
| W8, W10, W12 | ❌ | Data-integrity / security / blocked. Never auto-loop. |
| W15 | ❌ | Deploy and irreversible. |

> The `/goal` evaluator is a **stopping-condition check, not a quality gate.** Step 9.5 (`code-review` + `adr-check`) authority is unchanged, and tasks are never auto-completed on goal achievement.

---

## Critical path

`001 → 002 → 012 → 013 → 023 → 024 → 026 → 040 → 042`

**Spike-1 (002) is the keystone.** If `document.url` is unusable on Word desktop for SPE files, FR-01's primary path fails and the FR-02 stamp becomes the only identity mechanism — which does not work for documents saved before this release. That outcome invalidates the sizing of 012, 013, 021, 023, 024, 026 and 027.

---

## High-risk items

| Task | Risk |
|---|---|
| **002** | Negative result cascades through most of Phases 1–2. Run first. |
| **005** | May reveal the add-in save path has no collision protection at all — a live data-loss finding, not a UX gap. |
| **010** | `HostAdapterFactory` has zero call sites and the "tested" `WordAdapter` uses the broken `body.getOoxml()` path. **Prescriptive order is load-bearing**: port `getCompressedFile()` first, verify Outlook, delete last. |
| **023 / 024** | Data integrity. NFR-08: editable documents MUST use link/graduate, never the immutable suppress path — suppress-forever collapses two distinct drafts into one record. |
| **032** | ✅ **RESOLVED 2026-09-08 — hardened, NOT descoped.** The descope valve ("if hardening proves large, cut Find from r1") was evaluated and not needed: every primitive already existed, including `IAiAuthorizationService`, which was already a dependency of the under-authorizing filter. Rows are now authorized per document, both routes refuse without the published obligation, and `VisualizationEndpoints.cs` is in the `RouteAuthorizationGuardTests` census. The NFR-02 negative test passes and was verified to FAIL (7 of 11) with the row check disabled. Gate for 033 is met. Latency to size 033 against, plus 3 deferred findings, in [`notes/032-authorization-hardening.md`](../notes/032-authorization-hardening.md). |
| **030** | New server-side creation service; blast radius beyond the add-in. Existing `Create*Wizard` components MUST NOT be modified. |

---

## Cross-project coordination

Run `/conflict-check` before **every** BFF PR.

| Project | Overlap |
|---|---|
| `spaarkeai-compose-r8` | The other `.docx` write path; `parallel-safe:false` across the Compose spine. ADR-049 governs. Never delete `docxBridge.ts`. |
| `unified-access-control-r2` | Task 094 (collision pre-flight — task 005/025) · task 095 (two-slot association — task 026). **Do not duplicate.** |
| `email-communication-intelligence-r2` | Authored the current add-in surface + both handoff docs. |

⚠️ **`ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` are FROZEN** under the shadow-comparison window (open 2026-08-27). `deploy-office-addins.yml` is editable — and **does not currently trigger on this branch** (only `master` and `work/SDAP-outlook-office-add-in`).

---

## Findings bound to tasks

Six discovery findings modify spec assumptions. Full detail in [`../plan.md`](../plan.md) §3.

| ID | One-line | Owning task |
|---|---|---|
| **F-a** | The shipped collision handling is on an upload path the add-in does not use — ✅ **CONFIRMED** 2026-09-08, [spike-4 report](../notes/spikes/spike-4-collision-path.md) | 005 → 025 |
| **F-h** | 🔴 Editable Office saves (**both hosts**) run the *immutable suppress* dedup path, which NFR-08 / `DEDUP-AND-SAVE-BACK-IDENTITY.md` §3 forbid; two distinct drafts that are momentarily byte-identical collapse into one record | **028** — owned by r1 per operator decision 2026-09-08; host-neutral, keyed on `SaveContentType` ([spike-4 §3 D2](../notes/spikes/spike-4-collision-path.md)) |
| **F-b** | FR-16's similarity engine has **no per-row authorization** | 032 (gates 033) |
| **F-c** | No single endpoint returns similar documents *and* records | 034 |
| **F-d** | FR-11's `ExistingDocumentId` hook is inert on both sides | 023, 024 |
| **F-e** | FR-04 as written would regress the `.docx` save | 010 |
| **F-f** | `POST /api/office/save` has zero executing contract coverage | 016 |
| **F-g** | `sprk_event` does not exist on `sprk_document` (only `sprk_relatedevent`), yet shipped code authorizes and writes it — the coordination doc's "mappable set" is wrong on `event`, `todo` and `contact` | 026, 035 (premise corrected) · **fix owned by UAC-r2** |
