# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-06, second pre-compaction handoff (main session, by context-handoff)
> **Recovery**: read "Quick Recovery" first. This is the MAIN SESSION's orchestrator view. Tasks run as subagents;
> per-task detail lives in each POML's `<completion>` element and in `notes/`. Sub-agents never edit this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No main-session task. Orchestrating. **Index: 57 tasks — 29 ✅ · 3 🔄 (081, 097, 098) · 25 🔲.** Drift check clean. |
| **Background agents still running at handoff** (notifications arrive after compaction) | (1) **081 round 5 DONE** on PR #1309 @ `506b59525` (CI green; R4-1..R4-9 fixed, AST analyzer `xrmCapabilityAnalyzer.ts`, audit table = PR comment 6008616047). Round-5 review = **PASS-WITH-FINDINGS** (R5-1..R5-9: analyzer silent-pass holes → invert default to blind-spot + symbol-based seeding; guard not PR-enforced (only nightly continue-on-error); openPlaybookLibrary test breaks under package-only install; displaced eslint-disables; `||` sites uncounted; R4-1 pinned only for live search; main.tsx untested; nameResolution metadata source; wizard userId frame skip). **081 merge `5b9dbeab4` + round 6 DONE → head `d2d0a163f`, CI green. Review of both = PASS-WITH-FINDINGS** (analyzer still silent on anonymous returning fns/IIFE/getter/JSX-prop arrow/dynamic import/destructured getXrm; noise; guard step order). **Round 7 DONE → head `de20d3eac`, + main-session `.claude/` ci-cd skill + CHANGELOG commit `639f46d19`; Tier 1 job `xrm-capability-guard` red/green proven. Round-7 review = FAIL on B1: `ci-router.yml` docs_only skips ALL Tier 1 for client+doc PRs. Owner 2026-10-06: fix docs_only = every file is docs (exception extended; spec updated). Round 8 sent (B1 + analyzer M1/M2/m1-m5/n1; bar = no silent pass on named shapes + truthful limits).** Also **owner decision 2026-10-06: guard becomes a BLOCKING Tier 1 job** (new `classify-tier1` output for client paths, fails Router; §6.5 path A exception to ci-cd-unit-test-remediation-r1 FR-A02 — main session must record it in spec.md ADR Tensions + apply the agent's `.claude/CHANGELOG.md` entry). Then focused review of round 7 → ASK owner to merge. Extra baseline worktree `C:\wt081c` (remove after). Then ONE focused review of round 6 + merge commit → on PASS ASK owner to merge #1309. (2) **098 — agent FINISHED 2026-10-06** (`C:\wt098`, branch `fix/event-dates-date-only` @ `f6ac1fcd0`, rebased on #1302 head `ea92bd502`): all writers send a calendar date (ribbon + dialogs, EventsPage, side-pane picker; CommunicationRiActionService uses the recipient's local today via new shared `DataverseUserTimeZone`; TaskActionCore/ActionSeam/CreateTaskNodeExecutor no UTC shift); readers fixed (DataGrid `dateonly` renderer from metadata `dateTimeBehavior`, `parseDueDate` in side pane/calendar/Daily Briefing, VisualHost CalendarVisual); live proof 204s + old ribbon 400; tests fail on old code; its own code-review/adr-check passed with fixes applied; publish +4,208 B. **No PR yet.** PR body draft: scratchpad `task098/pr-098-body.md`. Conflicts vs #1309 = two import lines only (`CalendarWorkspaceWidget.tsx:130` keep both; `EventsPage/src/registerEventHandlers.ts:21` → `import { registerCommandHandler, cleanGuid, getXrm, formatDateOnly } from '@spaarke/ui-components';`). Depends on #1309 for VisualHost due cards/ViewDataService/LW feed/Briefing `formatDueDate`. Documented-not-changed: Briefing "today" + "Query Overdue Tasks" use UTC day boundary (pre-existing). (3) **Prototype-vs-solution investigation** → `notes/v4-prototype-vs-solution.md` (not yet written; its helpers reported — see "UI/UX findings" below). |
| **#1302 state (2026-10-06)** | #1312 (uac-r2 route-authorization sweep) landed and conflicted; 097 agent reconciled in round 8: merge `5f83e1510` + test fix → head **`8d291fbb8`**, CI 41 pass / 0 pending, all event test projects + live harness pass, reconciliation table in PR body. ONE mechanism (master's `RecordRouteAccessAuthorizationFilter`), decisions A+B implemented. Not carried (owner follow-ups): 401/503 split for unresolved caller; regarding number from record-type catalog (master covers matter/project only). Round-8 review = **PASS-WITH-FINDINGS** (auth identical to master on every route; findings: Rule 5 still `eq Open` vs decision A; Rules 1/3 + B contact branch untested (mutations survived); dead resolver helpers + duplicate name-field map; regarding number null for invoice/analysis/account/work assignment/budget → read from `sprk_recordtype_ref`). **Round 9 DONE → head `dcdc83995`, CI green** (Rule 5 uses IsOpenWork; mutation tests; one name map; regarding number from `sprk_recordtype_ref` via `RecordNumberFieldOf` in event create + inbound email resolver + analysis stager; live INV-002 / analysis number). Round-9 check = PASS-WITH-FINDINGS: **Rule 5 had no assignee filter** (every Draft event would get an "Assigned:" To Do; 58 of 69 candidates unassigned) + catalog-fault ordering in email resolver/analysis stager loses name/URL + tests/cache. **Round 10 DONE → head `83ed11584`** (Rule 5 assignee filter + ordering + paging; catalog-fault guards in resolver/analysis/event create; no null caching; merged master after #1319). Stale "Assigned:" To Dos in dev = 0 (no cleanup). CI green. **Running: focused check of round 10 (`C:\wt097t`) + 097 agent measuring publish size/CVE.** Both clean → merge → merge (owner-approved). Owner list: 401/503 split for unresolved caller (#1312's 403 contract). #1309 also conflicted with #1312; 081 agent merging master then round 6. |
| **098 state (2026-10-06)** | Branch head `1c48fd1ec`: Briefing + "Query Overdue Tasks" UTC boundaries fixed (user-local today; tests fail on old). Parked until #1309 merges, then ONE rebase onto master doing: import-line conflicts, `task098/apply-r4-5-guard.py` (CalendarWorkspaceWidget type guard, R4-5), **owner decision: TodoGenerationService uses the To Do recipient's zone (assignee → owner → UTC, cached per user)**, tests, review gates, size vs fresh master, open PR. |
| **#1302 vs #1312 owner decisions (2026-10-06)** | Adopt master's `RecordRouteAccessAuthorizationFilter` (404 no-read), route deletions stand, keep 097's master bug fixes (priority 100000000+, To Do status values/F5, eventlog column, RegardingNameFields, external SPA). **A:** Reassigned IS completable — one `IsOpenWork` predicate for complete + To Do generation. **B:** "my events" = owner OR assigned-to-my-contact OR `sprk_createdbyperson` = me (runs as caller). 097 agent finishing merge + tests + live + CI. |
| **Next Action** | In order, as each completes: **(a) Merge #1302** (owner-approved) once the round-8 review passes (fix findings first if any) and `gh pr checks 1302` shows 0 pending/fail: `gh pr merge 1302 --squash --delete-branch`; then mark 097 ✅ (POML status + `<completion>`, TASK-INDEX). 098 stays parked until #1309 merges (then ONE rebase; see 098 row). **(b) #1309**: when round 6 + master merge report, run ONE focused independent review of both; if it passes, ASK the owner to approve merging #1309 (merge it BEFORE 098; known conflict with #1302 = modify/delete on `EventDetailSidePane/src/components/TodoSection.tsx`, keep the deletion); then mark 081 ✅ and write its record into `notes/081-progress.md`. **(c) 098**: independent review of its PR, merge order after #1309; then ✅. **(d)** When `notes/v4-prototype-vs-solution.md` lands: commit it, then give the owner ONE consolidated decision list, **R-11 first** (see below), then update spec + add the new tasks (007, 008, 036, 043, 044, 056, 057, 058, Ontology admin slices). **(e)** Then 007/008, then **031**. |
| **Branch / git** | `docs/ontology-platform-design` pushed and in sync at handoff (last `919db11e4`). |

### Decisions waiting on the owner (bring as ONE list)
1. 🔴 **R-11 — blocks 031**: `sprk_dedupekey` is a unique key and the 030 writer leaves a Resolved row Resolved, so a dismissed/resolved/superseded Signal can NEVER re-raise (breaks D-11's 30-day re-fire, Do-lane re-raise, supersede-and-re-raise on rule publish). Proposed: add an episode number to the key + a re-raise rule (`notes/v4-reconciliation.md` R-11, C-1). Also C-12 (no role can write `sprk_policyversion`, so derive version end from `sprk_currentversion`).
2. **UI/UX (from the three notes)**: recording vs executing (most v4 actions have no write path; FR-18 promises recording only); who writes the Decision Record (user per the roles vs BFF writer per task 040); no non-chat gate entry point (task 053 assumes one); Decision Record can't hold v4's step/follow-on shape, no follow-on→decision link except email `correlationid`; Signal writer refuses event/work-assignment/service-request subjects (no Do-lane Signals yet); rules offer one proposed action (one text field) vs v4's 3–4; dropping FR-27's row ⋮ menu + `OutcomeCard` reuse (needs sign-off); FR-28 allows one new component vs v4's ~7; Ontology admin's home in the Console (no top bar in the real Console; nothing role-gates sections today) + D-13 amending D-3 + admin write identity; Assistant "why isn't X here?" needs an evaluation trace nothing records; Email Review vs spec A-5; v4's OS-theme fallback breaks ADR-021 (do not port); landing/source columns have no creating task; narrative is LLM-written today (062 is new work).
3. **Modal (notes/modal-wizard-canonical-approach.md)**: adopt `SprkModal` envelope + `WizardShell` engine, delete `WizardModal`, launch in-app from our apps (white header = Dataverse `navigateTo` target:2 chrome, unthemeable); ADR-050 amendment (path B); ~7–9 dev-days; separately approve 5–8 days to move today's wizards off `navigateTo` when launched in-app.
4. **Events (097)**: what counts as open/overdue event work (currently one named predicate `EventStatusCode.IsOpenWork` = Draft, Open, On Hold, Reassigned — owner may change it); two status columns (`statuscode` vs `sprk_eventstatus`); reschedule writes `sprk_duedate` while Briefing reads `sprk_finalduedate` first; ~~"my events" gap~~ DECIDED 2026-10-06 (owner/assignee/creator); re-parent re-own now handled by master's `PATCH /{id}/filing` (#1312); ~~Reassigned completable~~ DECIDED yes; soft-delete/cancel routes deleted by #1312 (owner); still confirm: Completed stays Active (schema-required).
5. **To Do composite score**: move its five 3/7/10 copies to calendar days? (re-ranks boards).
6. ~~CI full-unit-test job never completes~~ **RESOLVED by another project** (Tier 2 now sharded: api-insights-integration / api-office / api-seam / rest, all passing; commit a6a5109be). Drop from the list. NEW small item: Tier 2 advisory "ADR Compliance (NetArchTest)" has `timeout-minutes: 3` (`ci-tier2-advisory.yml`) and the arch suite grew to 806 tests with #1312 → runs 2m52s–3m24s, intermittently cancelled (master `d254d7166` cancelled, `dc469d4a2` passed). Advisory only; owner may want it raised to 5 (one-line, ci-workflows hot path, uac-r2/CI owner's file). Also: #1309's guard step (R5-2) sits in a `continue-on-error` job → not blocking; review of round 6 will recommend how to make it blocking.
7. Still open from before: writer privileges on `Spaarke Ontology Service` + moving the writer into the customer BU.

### Owner decisions made 2026-10-05 / 06 (recorded)
| Decision | Where |
|---|---|
| Merge #1123, #1286, #1287, #1294, #1302 — **all merged except #1302 (approved, pending checks)** | TASK-INDEX |
| ADR-009 path A for 096's verdict cache — approved | spec.md ADR Tensions |
| v4 (`HANDOFF.md` @ `ae1cc9f`, findings 1–40) is the UI baseline; flag what the solution can't do; Ontology admin in R1 (coordinator's call, delegated); one canonical modal approach | design.md header/§11/§12, spec.md header/§8.2, project CLAUDE.md §3.3 |
| SmartTodo's palette is the ONE due-urgency scheme (overdue red · 0–3 dark orange · 4–7 yellow · 8–10 grey · beyond none) | notes/081-progress.md |
| 081 ships as its own PR to master | 081 POML notes |
| Event routes: the solution's established record-level authorization pattern (done in #1302: caller-rights probe, AppendTo on parent, Create privilege, I-6 ownership) | PR #1302 |
| Event date columns → Date Only (CanChangeDateTimeBehavior switched on; UTC conversion; 5 hand-corrected) — **done in spaarkedev1**; per-environment procedure in `docs/data-model/sprk_event-date-columns.md` (098 branch) | task 098 |
| Pinned notice #1308 for other projects (PCF builds now stop on real failures) | GitHub |

### Open PRs and merge order
| PR | Task | State |
|---|---|---|
| **#1302** | 097 events API repair | Approved; merge when green. Proven on real routes (master: create/get/list/complete returned 500) |
| **#1309** | 081 shared UI cleanup | Round 5 running → focused review → ask owner → merge **before** 098 |
| (098 PR, not yet open) | 098 date columns | After #1302; merge after #1309 |
| #1293 (uac-r2's) | test repairs | Rebasing onto master per agreement |
| #1111 (draft) | this branch | project end |

### Housekeeping for the owner
Delete by hand (sandbox blocks deleting under `C:\`; nothing tracks them): `C:\wt081-base`, `C:\wt097m`, `C:\wtz`, `C:\wt097\TestResults097`, `C:\wt081r`. Throwaway worktrees still registered: `C:\wt081b` (081 master baseline), `C:\wt092`, `C:\wt094`, `C:\wt095`, `C:\wt096` (merged), `C:\wt097`, `C:\wt098`, `C:\wt081`, `C:\wt081m` — remove merged ones after their PRs land.

### Lessons from this session (do not relearn)
- Every independent review found something real (022 ×3, 081 ×4, 097 ×4, 096, #1286). Keep the gate; review each rework round, focused on its diff.
- "Proven live" must mean the **real route**, not replayed payloads (097 round 2 missed a nonexistent column that made create return 500 after writing).
- Fixing a broken route can **expose** a latent hole (097: no record-level authorization once routes stopped crashing).
- A one-way schema change breaks every reader/writer of the column — inventory them first (098).
- Contended full suites give false failures; a failure counts only if it reproduces alone and not on master.

### Critical context in three sentences
The backend foundation (schema, roles, compiler 021, writer 030, gate 022) is done; the next build step, the evaluator 031, is blocked on the owner's R-11 decision. The UI baseline is prototype v4, reconciled in three notes with a consolidated owner decision list still to deliver. Most of the session went into master-wide defects the ontology work exposed (build pipeline, events API, AI schema race, date columns), each fixed in its own reviewed PR.

---

## What exists now — verified 2026-10-02, not assumed

### Dataverse — solution `OntologyPlatformSolution`, publisher **Spaarke** (`sprk`)

| Table | OTC | `sprk_` cols | Notes |
|---|---|---|---|
| `sprk_signal` | 11003 | 59 | Alternate key `sprk_dedupekey` → **Active** |
| `sprk_decisionrecord` | 11002 | 22 | Append-only by privilege |
| `sprk_policy` | 11000 | 17 | Alternate key `sprk_policycode` → **Active** |
| `sprk_policyversion` | 11001 | 17 | Immutable by privilege |
| `sprk_budgetrevision` | 10999 | 15 | Path B's second conjunct reads `sprk_revisedon` |

130 `sprk_` columns · 24 lookups · both alternate keys **Active** · zero logical names with an underscore between
words. Field detail + the creation recipe: [`notes/schema-draft.md`](notes/schema-draft.md).

### Security — three roles, privileges verified by query

`Spaarke Console User` · `Spaarke Ontology Administrator` · `Spaarke Ontology Service` (6 copies each — one per
business unit, which is normal). Auditing enabled on the two ledger tables. Matrix:
[`notes/security-roles.md`](notes/security-roles.md).

**Verified present** on `Spaarke Console User`, all at depth **4** (Parent: Child BU): `prvReadsprk_Signal`,
`prvWritesprk_Signal`, `prvCreatesprk_DecisionRecord`, `prvReadsprk_DecisionRecord`.

**Verified ABSENT** — the four that carry the guarantees: `prvCreatesprk_Signal` (so a Work Item can exist
*only* because a rule produced it), `prvWritesprk_DecisionRecord`, `prvDeletesprk_DecisionRecord`,
`prvWritesprk_PolicyVersion`.

> ### ✅ The union check was run — the result is benign, and must not be "fixed"
>
> Append-only is a property of the **union** of all roles, so every role was checked for Write/Delete on the two
> ledger tables. Three hold them: **System Administrator** and **System Customizer** (both unavoidable and
> already documented), plus the platform roles **`Service Writer` / `Service Deleter`**.
>
> Those two are held **exclusively by Microsoft first-party application identities** — every holder has an
> `applicationid` and the `#` system-user prefix (AIBuilder, DV-MetadataService, PowerPages Data Runtime, PPMI
> managed identities, Power Apps Checker, AppDeploymentOrchestration …). **No human user and no Spaarke identity
> holds either one.** Dataverse auto-grants these to its own services on every new custom table.
>
> **No action — and specifically do not strip privileges from those roles**: it would break Power Pages, AI
> Builder and solution deployment. Append-only holds against every human and against Spaarke's own application
> identity, which is the realistic bar.

---

## The next action, concretely

**`task-execute` on task 001**, or say **"continue"**. The pipeline is done; what remains is execution.

### Where to start, and why that order

**001 (schema) first** — four columns are missing and two of them block a path outright: without
`sprk_decisionrecord.sprk_action` the deny path cannot save, and without the three `sprk_servicerequest`
columns success criterion 10 cannot be measured. Exact settings are in the POML.

**003 (ADR-040 amendment) in parallel** — it touches `.claude/` so it is **main-session only** (sub-agents
cannot write there; that boundary is working correctly, not a bug). It must merge **before or alongside task
031**, so starting it early removes it from the critical path.

**Then wave A** (010, 011, 012) — the three cleanup items that gate the worklist row.

### The two tasks that carry the project's risk

| Task | Why |
|---|---|
| **021** — the predicate compiler | 🔴 `notExists` has **no prior art anywhere in the repo**. A broken anti-join fails by returning *every* row or *no* row, and **both read as a working predicate** — which is why its acceptance criteria include an empty-source-table test. Serial, opus, xhigh |
| **074** — the recall measurement | 🔴 **It can fail the project.** At 70% recall the differentiated claim misses 30% of real cases **while every other criterion passes green**. Floor is ≥80% on ≥50 labelled items |

And the one most easily lost into implementation: **030 includes FR-14**, setting the Signal's owner from its
grouping matter. One line now; a re-own of every row later; invisible until then because nothing errors.

### What `/design-to-spec` produced and decided (2026-10-03)

[`spec.md`](spec.md) — 44 FRs in ten groups (A schema · B policy · C evaluator+lifecycle · D Decision Record ·
E worklist · F Do lane · G Inquiry · H classifier · I cleanup · J repairs), 9 NFRs, 11 success criteria, the
§11 three-question table for six new components, and a non-empty **ADR Tensions** section.

**Four new decisions — `design.md` §8.0c, spec §9.** None is a re-litigation:

| ID | Decision |
|---|---|
| **D-9** | **ADR-039 → path A** (exception: Policy decides what is *true*, Binding what *executes*) · **ADR-040 → path B** (amendment: `SessionGate` and Decision Record are **siblings**). ⚠️ **The ADR-040 amendment must merge before or alongside the evaluator** |
| **D-10** | Classifier recall floor **≥ 80% on ≥ 50 labelled items** — makes criterion 11 a real gate |
| **D-11** | Suppression counts per **(policy, matter)**, expires **30 days** — resolves the dedupe-grain conflict |
| **D-12** | **One evaluator, cadence by lane, two event hooks.** Reasoning is load-bearing and now in `design.md` **§8.3** |

🔴 **Three schema deltas found by querying the built tables against the draft** — the five tables exist, but:

1. **`sprk_decisionrecord.sprk_action` was never created** → spec **FR-01**. One of D-2's four binding
   constraints ("a deny path has no action"), so the deny path currently cannot save.
2. **`sprk_servicerequest` lacks both** the `Inbound`/`Outbound` discriminator **and** `sprk_disposition` →
   spec **FR-02**. CM-5 and **success criterion 10** both depend on them.
3. `sprk_signal` has **no `sprk_subjecttype`** (only `sprk_policy` does), so `sprk_dedupekey` composes from
   `sprk_regardingrecordtype` + `sprk_regardingrecordid` → spec **FR-03**. No decision needed.

All five tables are at **0 rows** — §8.1 seeding is still outstanding, and spec assumption **A-3** adds the
**two negative controls** criterion 2 needs and the checklist omits.

**Two things to carry into the spec that are easy to lose:**

1. 🔴 **Signal ownership — one line in the writer, free now, a migration later.** When the evaluator creates a
   Signal, **set its owner (or owning BU) from its grouping matter.** A Signal is secured by *its own* owner, not
   by the matter it points at, while `sprk_sentence` can carry matter detail — so a service-owned Signal at
   depth-4 read could expose a matter the reader cannot open, with the matter lookup rendering blank while the
   sentence tells them anyway. With **6 business units** this is live, not theoretical.
   [`notes/security-roles.md`](notes/security-roles.md) §4 has it; true privilege *inheritance* stays a deferred ADR.
2. **Both Path B conjuncts are NEW CODE.** `ILiveFactResolver`'s *dispatch* is generic but its **predicates are a
   closed C# `switch`** — nothing reads `sprk_communication`, and the **NOT-EXISTS half has no prior art in the
   repo** (EXISTS does: `DataversePrecedentBoard.cs:182-190`). Do not let *"the resolver is already generic"*
   read as *"no new code."*

**Parallel, blocking nothing**: prototype round-2 review (agree the component kit as the Console's UI contract);
§8.1 dev-data seeding (the exit triple criterion 2 needs).

### Two Dataverse settings still open (owner's call — neither blocks the spec)

A second verification pass on 2026-10-03 ([`notes/security-roles.md`](notes/security-roles.md) **§7**) closed
everything except two choices. It also **fixed** one gap: `sprk_budgetrevision` auditing was off and is now on
(§7.2 — it is the only table whose *absence* a Signal asserts, and §8.2 ruled out bitemporality, so its audit
log is the only way to reconstruct what was true when the evaluator ran).

1. **Add the five tables to the `Spaarke Platform` app** (§7.6). None of them is in **any** app module, so no
   Policy / Signal / Decision Record form can be opened by hand. Harmless for the Console (BFF-read, no
   sitemap) but it bites **§8.1 seeding and debugging**. `Spaarke Platform` is the config app — 90 entities, 87
   `sprk_`; `Matter Management` and `Spaarke AI Setup` hold 0 entity components.
2. **Assign `Spaarke Ontology Administrator` to someone** (§7.7). Nobody holds it, so Policy authoring happens
   as System Administrator and the role's 25 privileges stay **unexercised** until a customer environment hits
   them.

> **Do not re-raise** as defects, both verified normal in §7.3/§7.5: per-BU role copies carry privileges only
> on the **root-BU record** (`Spaarke Core User` shows the same 744-vs-0 shape), and **no** field security
> profile touches the 130 columns — which must stay true, or the `sprk_regarding*` trio diverges per user.

---

## Operational traps — each cost real time

| Trap | What to do |
|---|---|
| **`origin/master` moves very fast** | 70 commits behind within hours; 44 now. **Merge master before any deploy**, or you revert other projects' work |
| **MCP `create_table` cannot set the publisher** | It uses the env default, whose prefix here is **`new`** (CDS default `cr140`). Logical names are **immutable**, so a wrong prefix is delete-and-recreate, not a rename. Use Web API `POST EntityDefinitions` with explicit **PascalCase `SchemaName`** + the `MSCRM.SolutionUniqueName` header |
| **Every `DateTimeAttributeMetadata` needs `DateTimeBehavior`** | Omit it → behavior `None` → filtered-view generation breaks → **every later relationship on that entity fails**, with an error naming the *datetime column*, not the lookup you were creating |
| **The solution-create POST must NOT carry `MSCRM.SolutionUniqueName`** | It validates the header against a solution that does not exist yet → `404 not valid` |
| **BFF unit suite takes ~15 min** | Always `run_in_background`; a foreground run blows the 600s timeout |
| **Swallow-and-log paths are invisible** | App Insights appId `6a76b012-46d9-412f-b4ab-4905658a9559` — `traces` for `[comms-policy]`/`[comms-ri]`, `exceptions` for swallowed throws. How the silent `InvalidCastException` was found |
| **Apostrophes break bash heredocs** | Write commit messages to a file, `git commit -F` |
| **Delegated wide audits failed five times** | A coordinator agent kept fanning out and returning status updates. Run targeted agents **directly** and re-verify load-bearing claims yourself — doing so corrected a finding in our favour |

---

## Scope

**In**: cross-source evaluator · `Existence` rule type · `sprk_budgetrevision` · Inquiry action · Decision Record
· worklist row · `sprk_memo` as source #2 · guidance injection · the **Do lane** (Briefing items *enhanced into*
Work Items) · landing-contract columns only · two repairs (space-bearing matter-number tokenizer; the false
association `reason` string) · **27 component-cleanup items C-1..C-27**.

**Out**: Connection Engine / connector / LEDES · the Action **Engine** (an Action *row* on the shipped spine is
in) · Authority · MCP server · bitemporality · per-entity fact or signal tables.

**Cleanup**: only **C-1, C-3, C-4** gate the worklist row. Six items are hazards found here but not caused here —
an untested bulk-delete (`CommandRegistry`), a bypassed ADR-015 privacy contract (Pillar-9), a production render
path that silently shows a placeholder (`InsightSummaryCard`), a function that **already lost data**
(`composeCommentThreadsToDocxAnnotations`), a **live one-day Kanban drift**, and an untrue root-`CLAUDE.md`
Calendar statement. Full list: [`notes/reuse-verification-2026-10-02.md`](notes/reuse-verification-2026-10-02.md)
§8.7 + §8.9.

---

## Document map

| File | Role |
|---|---|
| [`spec.md`](spec.md) | **The specification — what `/project-pipeline` consumes.** 44 FRs · 9 NFRs · ADR Tensions resolved · §9 = D-9..D-12 · §10 assumptions · §11 three unresolved questions |
| [`design.md`](design.md) | **rev 11 — the decisions.** §10 = 29 settled · §8.0c = D-9..D-12 · **§8.3 = why evaluation is scheduled *and* event-driven** |
| [`notes/schema-draft.md`](notes/schema-draft.md) | The five tables field by field + the creation recipe and its traps |
| [`notes/security-roles.md`](notes/security-roles.md) | Privilege matrix, scopes, the Signal-ownership question |
| [`notes/mvp-technical-spec.md`](notes/mvp-technical-spec.md) | Evidence base. **§10.7 = authoritative live row counts** |
| [`notes/reuse-verification-2026-10-02.md`](notes/reuse-verification-2026-10-02.md) | Reuse + duplication audit; C-1..C-27 |
| [`notes/daily-briefing-ontology-fit.md`](notes/daily-briefing-ontology-fit.md) | Why Briefing items become Work Items |
| [`notes/ontology-component-model.md`](notes/ontology-component-model.md) | **Authoritative vocabulary (§3)**; §3.1 = the Signal → Work Item chain |
| [`notes/mvp-synopsis.md`](notes/mvp-synopsis.md) | Scope narrative |
| [`notes/defer-issues.md`](notes/defer-issues.md) | 4 entries, all with GitHub Issue URLs (#1048–1050, #1095) |
| Stamped historical | `ontology-architecture-feedback.md` (APPLIED) · `console-prototype-prompt.md` (CONSUMED) · `phase0-codebase-inventory.md` (SNAPSHOT) · `spaarke-ontology-strategy-synopsis-v2.md` (strategy; superseded for scope) |

**Console prototype** — the UI/UX contract, *not* the implementation (standalone Vite, mocked, reuses nothing):
`HANDOFF.md` @ `ae1cc9f` (v4, findings 1–40) in `spaarke-dev/spaarke-prototype` (`feature/2026-10-spaarke-console`); local `c:\code_files\spaarke-prototype-wt-spaarke-console\projects\2026-10-spaarke-console\`. Owner-accepted baseline 2026-10-05.
