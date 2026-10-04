# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-04 03:10Z (main session; tasks 006 + 002 completed)
> **Recovery**: read "Quick Recovery" first. This file is the MAIN SESSION's orchestrator view: several tasks
> run as subagents in parallel, so per-task detail lives in each POML's `<completion>` element and in `notes/`.
> Sub-agents were told not to edit this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none active in the main session.** Last completed: **006** (dedicated writer identity) and **002** (privilege re-verify), both ✅ 2026-10-04 |
| **Step** | — |
| **Status** | orchestrating; waiting on background agents 021 and 089 |
| **Next Action** | (1) When **021** reports: record it in POML + TASK-INDEX, then dispatch **030** (deps 021 + 006 now both satisfied) and **022** (held only to keep 021 alone in this worktree). (2) When **089** reports: record it, then dispatch **091**. (3) **081** waits for PRs #1118 + #1121 to merge, then merge master into this branch. Writer identity for 030: client id `69040982-612e-469e-a85f-26d5172367c5`, systemuser `3121bf1b-9fbf-f111-aaaf-0022482913fc` (see `notes/security-roles.md` §9) |
| **Running in background** | **021** predicate compiler (opus subagent, in THIS worktree; told not to touch this file) · **089** `cleanGuid` follow-up (in `C:\wt089`, PR #1121: converge the ~40 deferred call sites + case-semantics audit). Their reports arrive as agent messages; record them in POML + TASK-INDEX |
| **Branch / git** | `docs/ontology-platform-design`, 0 behind `origin/master`; the 006/002 records are committed and pushed with this checkpoint |
| **Index** | 50 tasks: **19 ✅ · 0 🔄 · 31 🔲**. Drift check clean (50/50) |

### Files modified since the last commit
- none (all committed with this checkpoint)

### Live changes made by task 006 (outside git)
- Azure: UAMI `mi-ontology-writer-dev` (rg-spaarke-dev) created and attached to `spaarke-bff-dev` (additive; app restarted; healthy)
- Dataverse `spaarkedev1`: app user `# mi-ontology-writer-dev` with ONE role `Spaarke Ontology Service`; two negative-test Decision Records created and deleted (0 rows remain)
- Seen, not caused: `/healthz/catalog` 503 = AI catalog drift, logged since 2026-09-29 (owned outside this project)

### Owner decisions made 2026-10-03 (all recorded in task files)
| Item | Decision |
|---|---|
| 002 writer principal | **Option A**: dedicated least-privileged identity, task **006** (002 and 030 now depend on 006) |
| 022 invalid rule bodies | **Validate at evaluation time**, fail closed; authoring in the Spaarke Platform app stays |
| C-21 Pillar-9 privacy shim | **Delete** (done in PR #1120) |
| C-17 due-date tiers | **3/7/10 days** (task 081) |
| Cleanup placement | Fix everything now, never defer to issues; cleanup unrelated to ontology goes to **its own PR** (see `notes/cleanup-placement-plan.md`) |
| CalendarSidePane | **Not currently in use** but may return: fixed, not deleted (PR #1114) |
| Task 005 spend snapshots | **Keep** |

### Open PRs (merging is the owner's call; I do not merge)
| PR | Content | Note |
|---|---|---|
| #1111 (draft) | This branch | |
| #1118 | C-10 To-Do scorer, **live bug** | **Merge first**: task 081 (C-13) reuses its `dateLocal.ts`; it also turns a guard test green that has been red on master since 2026-08-17 |
| #1114 | C-23 Calendar | Overlaps #1119 in `Spaarke.Events.Components/src/components/index.ts` (separate blocks) |
| #1116 | C-22 InsightSummaryCard | Web resource: needs a deploy after merge |
| #1117 | C-5, C-16 Compose | 7 edited test suites only run in CI |
| #1119 | C-2, C-24, C-25 Events leftovers | |
| #1120 | C-19, C-21, C-14, C-20, C-26, C-6, C-27; closes #1112, #1113 | Conflicts with this branch in `Spaarke.Visuals/src/components/index.ts`: keep `VisualMetricCard`, drop TrendCard lines. Reviewer to confirm ADR-020 SemVer path-A exception |
| #1121 | C-7, C-12, C-15 | 089 follow-up in progress |

### Held, and why
- **081** waits for **#1118 AND #1121** to merge (C-13 needs `dateLocal.ts`; C-8 starts in `DailyBriefingApp.tsx`, which #1121 edits), then merge master into this branch
- **091** (C-18 sweeps) after 089 finishes, so it does not compete for CPU
- **022** can start any time (decision made); not dispatched yet to keep 021 alone in this worktree
- **030** needs 021 + 006

### Still with the owner
- **Task 010 criterion 5**: Daily Briefing widget render check (needs a browser)

### Verified this session (do not redo)
- Full BFF suite uncontended at wave close: **Test Run Successful, 14,356 total / 14,302 passed / 54 skipped / 0 failed**. The 83/124-failure runs earlier were agent contention.
- Publish size: master `62277d50a` 45.66 MB / 212 files vs branch 45.67 MB / 212 files (+0.01 MB, Compress-Archive). Master worktree `C:\wt111m` kept for re-measurement.
- Shared git stash stack: 3 entries, all owned by other sessions, untouched. Sub-agents were told not to use bare `git stash`.

---

### Do NOT re-litigate

`design.md` §10 holds **29 settled decisions** and §8 has **no open items** — D-1..D-8, CM-2, CM-4, CM-6..CM-11,
BR-1..BR-6 are all closed, several of them twice (BR-1 and §5.1 were each reversed once). The schema is **built**.
If something feels unsettled, read §8.0 / §8.0a / §8.0b before reopening it.

### Critical context in three sentences

R1 builds the **intelligence layer from the Spaarke data model forward** — no connector, no LEDES; the data is
assumed present. The differentiated claim is one predicate: *a communication classified **fee or scope change**
in the window **AND** no **budget revision** in that window* — two sources, which no incumbent can evaluate. A
**Signal** is a condition that held; a **Work Item** is the actionable unit it produces; every human resolution
writes a **Decision Record**.

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
`c:\code_files\spaarke-prototype\projects\2026-10-spaarke-console\` — v2.1, findings 1–17, round-2 review pending.
