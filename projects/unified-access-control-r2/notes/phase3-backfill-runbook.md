# Phase 3 — Core-ancestor stamp backfill runbook (FR-26 / FR-27, task 053)

> Script: [`scripts/Backfill-CoreAncestorStamps.ps1`](../../../scripts/Backfill-CoreAncestorStamps.ps1)
> Companion notes: [`phase3-derivation-rules.md`](phase3-derivation-rules.md) (task 050, client derivation
> rules) · [`phase3-server-writers.md`](phase3-server-writers.md) (task 052, server writer convergence)
> Spec: `spec.md` FR-26/FR-27. Acceptance: *"a contact with access to Project 1 sees its ... To Dos"* —
> including records created **before** FR-26 shipped, which is exactly what this script closes.

---

## 0. What this closes

Tasks 050/052 made every **new** write stamp the ultimate core-record ancestor onto a child record whose
regarding target is itself child-class (todo → communication → matter, etc.), so the evaluator's
one-hop child-inheritance term can find it. Every row written **before** those tasks shipped has no
stamp and is silently invisible to inheriting principals — the exact F1 failure mode from
`notes/investigation/06-adversarial-critique.md`. This script is the one-time (re-runnable) backfill for
that pre-existing data.

**The script computes ancestry by the identical rule the runtime resolvers use** (`CoreAncestorResolver.cs`
/ `PolymorphicResolverService.ts`): one hop, core-target-stamps-itself, Matter-does-not-inherit-from-Project,
fail-closed on read error. It does **not** re-derive the rule independently — a backfill that computed
ancestry differently from the live resolver would produce confidently wrong access data, which is worse
than no backfill at all.

---

## 1. Two live-metadata findings from authoring this script (2026-09-04)

Per the task brief's instruction to verify column names against **live** Dataverse metadata rather than
trust prior citations, `mcp__dataverse__describe` was run against all six child entities before writing
any query logic. Two findings resulted, one correcting existing project notes and one new:

### 1a. `sprk_todo` DOES have `sprk_regardingservicerequest` today

`phase3-derivation-rules.md` F-050-1 and `phase3-server-writers.md` F-052-1 both state `sprk_todo` has no
service-request ancestor column (citing `docs/architecture/spaarke-todo-architecture.md:34`, and F-050-1
explicitly notes that pass had "no live Dataverse connection"). Live metadata as of 2026-09-04 shows the
column **exists**: `sprk_regardingservicerequest LOOKUP (GUID) (Related table: sprk_servicerequest)` is
present on `sprk_todo`, alongside `sprk_regardingmatter` / `sprk_regardingproject` /
`sprk_regardingworkassignment` — all four canonical ancestor columns are present. Whether this is a
recent schema addition or the prior citation was simply wrong is not determinable from this session; either
way, F-050-2's "known schema hole" for `sprk_todo` may already be moot. **Not fixed here** — 050/052 own
that documentation; this is filed for their owners to reconcile. This script does not care either way,
because it discovers columns live rather than trusting the prior citation (see §2).

### 1b. NEW — `sprk_invoice` and `sprk_document` carry NONE of the four ancestor-stamp columns 🔴

Neither entity has a column literally named `sprk_regardingproject` / `sprk_regardingmatter` /
`sprk_regardingworkassignment` / `sprk_regardingservicerequest`. Both instead carry differently-named
DIRECT association fields:

| Entity | Live association fields (verified 2026-09-04) | `sprk_regarding{core}` family present? |
|---|---|---|
| `sprk_invoice` | `sprk_matter`, `sprk_project` (no workassignment/servicerequest field at all) | **None** |
| `sprk_document` | `sprk_matter`, `sprk_project`, `sprk_workassignment` (no servicerequest field at all) | **None** |

**Consequence under the CURRENT resolver code** (both `CoreAncestorResolver.cs` and
`PolymorphicResolverService.ts` check for the four `sprk_regarding*`-prefixed names exactly, not a
per-entity alias table):

- Neither entity can **receive** a core-ancestor stamp as a host, regardless of what it regards — the
  applicable-column probe always comes back empty for both. This script will report **0 candidates for
  `sprk_invoice` and `sprk_document` on every run**, by design, not because nothing needs backfilling.
- Any child that regards an `sprk_invoice` or `sprk_document` AS ITS TARGET (todo → invoice, communication
  → invoice, analysis → document, etc.) derives **no ancestor at all** through that hop — the resolver's own
  legitimate `NoAncestor` status, not an error. `sprk_document` is a comparatively common target (todo,
  event, and analysis can all regard one directly), so this is not a rare corner.

This script mirrors that behavior exactly (reports `NoAncestor` for those targets, never invents a
workaround by reading `sprk_matter`/`sprk_project` as if they were ancestor stamps) — doing otherwise
would make the backfill's answer diverge from what the live evaluator actually derives.

**This is an owner decision, out of this script's scope.** Two remediation shapes, for the record:

1. Add `sprk_regardingproject` / `sprk_regardingmatter` / `sprk_regardingworkassignment` /
   `sprk_regardingservicerequest` lookup columns to both entities (schema change), then converge the
   resolver's `applicable` probe to find them — OR alias the resolver to also recognize `sprk_matter`
   / `sprk_project` / `sprk_workassignment` as ancestor-equivalent on these two entities specifically
   (a resolver code change, both TS and C# sides, to stay parity-pinned).
2. Accept and document that documents/invoices structurally cannot participate in core-ancestor
   inheritance today — anything regarding one inherits nothing through that hop.

Filed here rather than silently worked around, per this project's established convention (see F-050-2 /
F-052-1 / F-052-2 in the companion notes) of surfacing schema gaps for the owner rather than swallowing
them.

---

## 2. Why the script discovers schema live instead of hard-coding it

Finding 1a above is the direct, concrete illustration of the risk: a hand-maintained column list
(exactly what the prior notes carried) went stale without anyone noticing. The script instead queries
`EntityDefinitions(...)/ManyToOneRelationships` for each of the six child entities at the start of every
run and derives, from the live result:

- **AncestorColumns** — which of the four canonical `sprk_regarding{core}` columns actually exist on this
  entity AND correctly target the expected core entity (a name+target double-check, not a name guess).
- **ChildLookups** — every lookup on the entity whose target is itself one of the six CHILD entities (the
  "child-of-child" candidates for the enumeration query).

This means the script's behavior tracks the live schema automatically; it cannot re-encode either of the
two findings above as a stale assumption the way prose can.

---

## 3. Running it

```powershell
# Dry run (default) — zero writes, full summary + log. Always run this first.
.\scripts\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

# Apply — writes every resolvable stamp (unless the escalation gate fires; see §5).
.\scripts\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply

# Stage one entity at a time during a UAT window.
.\scripts\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply -Entities sprk_todo

# Re-run to fixpoint for deep (2+ hop) chains — see §4.
.\scripts\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply
```

Full parameter reference: `Get-Help .\scripts\Backfill-CoreAncestorStamps.ps1 -Full` (comment-based help
covers every parameter + five worked examples).

**Auth**: the operator's own `az` CLI context (`az account get-access-token --resource <url>`) — the same
pattern every other script in `scripts/` uses (e.g. `Backfill-DocumentHasFile.ps1`,
`Migrate-DataverseData.ps1`). No secrets in the script or the repo. Run `az login` first if the script
reports it could not acquire a token.

---

## 4. Reading the summary — "candidates" vs "writes"

The candidate pre-filter is deliberately broad: a row qualifies if its direct target is child-class AND
**any** applicable ancestor column is still null — because which single column will actually be written is
only knowable after reading the target. Under the one-ancestor-per-chain model, a **correctly stamped**
row keeps 3 of its 4 ancestor columns permanently null (only one core type is ever the real ancestor), so
`CandidatesScanned` will **not** trend to zero after a successful backfill. **`ToWrite: 0` is the
convergence signal to watch, not `CandidatesScanned: 0`.**

Deep chains (more than one hop of child-of-child, e.g. Event A → Event B → Communication C → Matter M) are
reported as `Unresolvable` on the FIRST pass — the 1-hop cap (ADR-034) means the script never chases past
the immediate target. The documented remedy is exactly what the task's own escalation trigger names:
**re-run the script until `ToWrite` reaches 0.** Each pass resolves one more hop as the previous pass's
targets get stamped (B gets stamped from C's already-correct M in pass 1; A can then resolve from B's new
stamp in pass 2). No persisted checkpoint is needed for this — the candidate query itself excludes
already-stamped rows, so re-running is always safe and always makes forward progress or reports 0 new
writes.

---

## 5. The escalation gate

Mirrors task 053's own `<escalation><trigger>` verbatim: if total candidates across all entities exceed
**50,000**, or any single entity's unresolvable share exceeds **20%**, `-Apply` prints a CLAUDE.md §6-style
banner and **skips all writes** (exit code 2) unless `-AcknowledgeEscalation` is also passed. Dry-run
always shows the banner too (informational) — review the log's `CONFLICT` / `UNRESOLVABLE` lines before
deciding whether to acknowledge and proceed, or to narrow scope with `-Entities` first.

---

## 6. Exit codes

| Code | Meaning |
|---|---|
| 0 | Clean run — dry-run report emitted with no hard errors, or apply completed with zero write failures |
| 1 | A hard error occurred — an entity's candidate query failed, a target read failed, or one or more writes failed. Check the log for `ERROR` / `ERROR-ENTITY` / `WRITE-FAILED` lines |
| 2 | `-Apply` was requested but the escalation gate fired and `-AcknowledgeEscalation` was not passed — no writes were issued |

---

## 7. What was NOT done, and why

Per the delegating instructions for task 053, **this script was authored and syntax-validated only — it
was never run against a live Dataverse environment.** That is an explicit run-boundary for this execution
(five agents were working concurrently in the same worktree; any live run is reserved as an operator
decision), and it overrides the task POML's step 5 ("test against dev with -WhatIf; capture the summary in
notes"). Consequently:

- **No expected candidate counts are recorded here.** The operator's first `-EnvironmentUrl` invocation
  (no `-Apply`) against dev IS the baseline measurement — capture its summary table into this file (or a
  dated companion note) at that time, rather than trusting any number written here without having run it.
- **Paging beyond 5,000 rows** was verified by code review (the `Invoke-DvGetPaged` helper follows
  `@odata.nextLink` in a `while` loop with no page-count cap) and by an isolated local test of the
  underlying retry/closure mechanism (no network calls — see the task's completion report for the exact
  command and output). It was **not** verified against a real >5,000-row result set.
- **The two live-metadata findings in §1 were captured via `mcp__dataverse__describe` against live
  Dataverse metadata**, which is read-only and does not touch data rows — this is consistent with the
  "no live run" boundary (which is about the script's own writes, not read-only schema verification).

**Execution during the UAT window is the operator's decision to make and record**, per the task's own
`<notes>`. When it happens: run the dry-run first, review the summary + escalation banner, then `-Apply`
(narrowing with `-Entities` for a staged rollout if the candidate count is large), and append the actual
counts + any owner decisions on the §1b gap to this file.
