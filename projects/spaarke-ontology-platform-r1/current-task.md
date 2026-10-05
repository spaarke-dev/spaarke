# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-05 ~02:30 UTC, after the restart (main session)
> **Recovery**: read "Quick Recovery" first. This is the MAIN SESSION's orchestrator view. Tasks run as subagents;
> per-task detail lives in each POML's `<completion>` element and in `notes/`. Sub-agents never edit this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No main-session task. Four agents were dispatched after the restart (2026-10-05): **022 rework round 2**, **081 rework**, **094 per-PCF release build** (PR #1286), **096** (new: Json.Schema.Net race in AI tool-schema validation, own PR). |
| **Status** | Machine healthy after the restart (27 dotnet procs, 12.6 GB free). **Both independent reviews FAILED with real findings:** 022 round 2 (13 findings, F3 decided: an `exists`-clause template token only when the clause pins the field to one value) and 081 (13 findings + escalation; decisions in `notes/081-progress.md`). Nightly PCF run dispatched on #1123's branch: run `37254845131`. |
| **Next Action** | As each agent reports: **022** → re-run an independent review, then ONE full BFF suite on a quiet machine, then ✅. **081** → write the agent's record into `notes/081-progress.md`, independent re-review, one clean SpaarkeAi/UI.Components jest run, merge into this branch. **094** → apply its `.claude/CHANGELOG.md` text (main session only), confirm per-PCF verification numbers. **092** → quote all 18 jobs of run `37254845131` by log marker, rewrite PR #1123's body. **096** → record PR, mark ✅. Then **031** (nightly evaluator). |
| **Decision this session** | The release build's PCF step builds **each PCF in production mode** (as the nightly workflow does), in #1286. The aggregate root build never worked: TS5083, then out of memory, and it was dev mode. |
| **Branch / git** | `docs/ontology-platform-design` pushed and in sync. 12 commits behind master at restart. |

### Before running ANY full test suite
The machine was starved: ~300 `dotnet` processes, **248 of them VS Code C# Dev Kit build hosts** (`visualstudio-projectsystem-buildhost`, >6 h old), ~4 GB free of 61.6 GB. Every full BFF suite today crashed or timed out on unrelated tests. **After the restart, check `Get-Process dotnet | Measure-Object` before trusting any suite result.**

### Worktrees (all other work is committed and pushed)
| Worktree | Branch | State at handoff |
|---|---|---|
| this one | `docs/ontology-platform-design` | clean, pushed |
| `C:\wt081` | `ontology/081-cleanup` | WIP committed + pushed `89b5230f9`: C-8, C-11, C-13, C-17 implemented, tests mostly verified, final report pending (read the commit message) |
| `C:\wt092` | `fix/master-build-test-baseline` (PR #1123) | WIP committed + pushed `e81d65e64`, clean |
| `C:\wt094` | `fix/pcf-deploy-verify-build` (PR #1286) | clean, pushed |
| `C:\wt095` | `fix/email-attachment-regex-timeout` (PR #1287) | clean, pushed |
| `C:\wt093b`, `C:\wt091`, `C:\wt086`, `C:\wt089` | merged/PR branches | clean, pushed (removable) |
| `C:\wt111m` | detached old master (publish-size baseline) | throwaway; re-measure master fresh instead |

### Open PRs — merge order matters
| PR | What | Merge |
|---|---|---|
| **#1123** | Master build/test repair: 9 PCFs (pdfjs stub, missing deps, flat-control webpack), 17 failing suites, **one user-visible fix** (FR-02 dashboard section height), 18 tsconfig `extends` fixes (WIP `e81d65e64`) | **after 092 finishes** |
| **#1286** | Deploy procedures verify the REAL PCF build result (task 094) | **after #1123 AND after the release build's PCF step passes** (it now fails: first TS5083, fixed in #1123; then an out-of-memory building all 18 PCFs in one process, open) |
| **#1287** | Flaky email-attachment test now tests production (task 095) | ready |
| #1111 (draft) | This branch | at project end |

Merged today: #1118, #1121, #1114, #1116, #1117, #1119, #1122, #1120, #1282, #1285. #1116 changes a Dataverse web resource and needs a deploy to take effect in dev.

### Waiting on the owner
1. **Writer privileges** (task 030 findings; owner leaned "the role should carry it"): add **AppendTo on `sprk_matter` and `sprk_communication` (Global)** and **Write on `sprk_signal` (Global)** to `Spaarke Ontology Service`; and move the writer app user (`3121bf1b-9fbf-f111-aaaf-0022482913fc`) into the **customer BU** (`Spaarke Business Unit 1` in dev), mirroring production (#1094). Until then communication-subject Signals fail (F26) and the matter path works only via the root default team's over-grant.
2. **Prototype round-2 review** (`c:\code_files\spaarke-prototype\projects\2026-10-spaarke-console\`) before task **051** (worklist row) starts.
3. **VS Code build hosts**: reload/close old worktree windows (the restart clears them).
4. Merges in the order above.

### Owner decisions made 2026-10-04 (all recorded in task files / spec)
| Decision | Where |
|---|---|
| ADR-028 path A for the writer's `ManagedIdentityCredential` — **approved** | spec.md §6 ADR Tensions |
| No silent failure: writer refusals log EventId 50300 + metric; evaluator emits a per-run metric + last-success diagnostic; two alert rules at deploy | 030 code; 031 + 035 POMLs |
| Nightly CI production PCF build (advisory) | done: #1282 + #1285 |
| Merge the cleanup PRs | done except #1123/#1286/#1287 |
| #1120 ADR-020 no-version-bump exception — approved, merged | — |
| Fix the flaky email test in its own PR | #1287 |

### Verified today (do not redo)
- Ledger append-only for the writer, re-checked with the CORRECT method (`RetrieveUserPrivileges` ∪ `RetrieveTeamPrivileges`, plus live 403 on update/delete). `RetrieveUserPrivileges` alone understates team-inherited depth — see security-roles.md §9.2.
- Signal ownership settled live: writer owns, `owningbusinessunit` = matter's BU (needs `EnableOwnershipAcrossBusinessUnits`, true in dev). A BU default team cannot own a Signal (403).
- The root default team's `Spaarke Office Add In User` grants Deep Write/Assign/Share on every matter to all ~150 root-BU principals (known dev artifact, #1094).
- `pcf-scripts build` exits 0 when webpack fails; the nightly workflow and #1286 judge from output.
- `Json.Schema.Net` `Evaluate` is not thread-safe on a shared schema (022 locked it).

### Critical context in three sentences
Tasks 021 (predicate compiler) and 030 (Signal writer) are done and committed; 022 (the fail-closed validation gate 031 must use) is done but awaits re-review. Every substantial task this project has had an independent review find something real (030 twice, 022, 094), so keep that gate. Several master-wide build defects surfaced along the way and are being fixed in their own PRs, not on this branch.

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
