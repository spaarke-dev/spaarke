# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-02 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. Everything needed to continue is in this file.
> *(Supersedes the 2026-09-30 checkpoint entirely — that one named rev 3, a now-dead branch, and the D-1 spike
> as the next action. All three are obsolete.)*

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | **Design COMPLETE.** `design.md` **rev 10** · every open decision settled · §8.0a "Still open" is **empty** |
| **Dataverse** | ✅ **All five tables CREATED in `spaarkedev1`** · security roles created · privileges **verified by query** · auditing on |
| **NEXT ACTION** | **Run `/design-to-spec`** on `projects/spaarke-ontology-platform-r1/design.md` |
| **Branch** | `docs/ontology-platform-design` — ⚠️ **NOT** `docs/ontology-platform-phase0` (squash-merged, dead) |
| **Git** | 4 ahead / **44 behind** `origin/master`. Clean, all pushed. **Merge master before any deploy** |
| **PR #1032** | ✅ **MERGED** to master as `93634db58` |

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

**`/design-to-spec`** on `design.md` (rev 10, six owner feedback rounds absorbed, no open decisions). Then
`/project-pipeline` → `task-execute`.

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
| [`design.md`](design.md) | **rev 10 — the decisions.** §10 = 29 settled · §8 = none open |
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
