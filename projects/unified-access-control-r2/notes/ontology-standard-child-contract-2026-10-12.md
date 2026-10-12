# Ontology Signals and Decision Records adopt the standard child-record contract (2026-10-12)

**Decision (owner, round 95):** `sprk_signal` and `sprk_decisionrecord` become ordinary secure-schema children. They use the same filing, stamping, ownership, lineage, reconciliation and share-mirroring machinery as To Do, Event and Communication. They have no ontology-specific access path.

**Split of work:**
- **spaarke-ontology-platform-r1** implements the schema delta and the writers.
- **unified-access-control-r2** adds the lineage rows and the census guard, and reviews every ontology PR against this note.

**Inputs:** the audit [ontology-secure-schema-audit-2026-10-12.md](ontology-secure-schema-audit-2026-10-12.md) (findings F1–F9), and round 94 / round 95 in [session27-owner-decisions-and-research.md](session27-owner-decisions-and-research.md). File references are to origin/master `9695a4ca7` unless stated.

## 1. The rule in one paragraph

A Signal or Decision Record is filed under its **subject**: the record the policy evaluated, such as a communication, document, matter or to-do.
- **Filing, the core-ancestor stamps, the owner and the shares are LIVE.** They follow the subject's current family. When the subject is re-filed, these columns move with it in the same operation.
- **The decision context is IMMUTABLE.** It records what the decision was about at the time: `sprk_corerecordid` (text), `sprk_corerecordtype`, the policy version and the evidence snapshot. It never changes and is never read as lineage.

This is the D-34 split: **history is preserved, and visibility follows the family.**

## 2. Why

Today the Decision Record is owned by resolving its **core only** (`DecisionRecordWriter.cs:353-359`, ontology branch). Its core is frozen when the subject is re-filed. Two leaks follow:
- **F1:** a Signal that is secure through its subject, under an ordinary core, produces an ordinary-owned Decision Record that the core's business unit can read.
- **F5:** a subject that moves from ordinary into secure leaves its Signals' and Decision Records' filing behind, still readable by the old business unit.

Both come from having a second, ontology-only access path. Joining the standard contract removes that path. The secure-family design ([secure-family-design-research-2026-10-12.md](secure-family-design-research-2026-10-12.md)) would later add one canonical `sprk_securefamily` lookup on these tables, like every other family table. This contract is the prerequisite either way.

## 3. The contract: components and what each table must do

| # | Component (owner) | File | What Signal and Decision Record must do |
|---|---|---|---|
| C1 | Polymorphic regarding (ADR-024) | `CoreAncestorResolver.cs:291` `sprk_regardingrecordid` (string), `:294` `sprk_regardingrecordtype` (lookup to `sprk_recordtype_ref`) | Each row carries the pair **plus** the subject's typed `sprk_regarding{entity}` lookup. Both are written together by the regarding builder, never one without the other. |
| C2 | Core-ancestor stamps (FR-26) | `CoreAncestorResolver.cs` (`IntermediateRootColumns` :169, `StampSourceColumns` :212); `CoreAncestorRestamper.cs`; `CoreAncestorStampReconciliationJob.cs` (5 min) | **The stamp columns are `sprk_regarding{matter,project,workassignment,servicerequest}`**, the only four in `CoreAncestorLookups` (:116-122). This is the To Do convention: a subject lookup and its stamp share the column. The stamp is **derived** from the subject by `CoreAncestorResolver` at create time and re-derived on every re-file. Both tables join `StampSourceColumns` as stamped children, never `IntermediateRootColumns`, so the restamper and the 5-minute job keep the stamp fresh. **Typed `sprk_matter`/`sprk_project`/`sprk_workassignment` (DR) and `sprk_matter` (Signal) are NOT stamp columns.** The restamper never writes them. T5 backfills them into the stamp, stops writing them as filing, and only then retires them from lineage (amended 2026-10-12 after the T1 review). |
| C3 | Ownership (D-129, D-130) | `RecordOwnershipResolver.cs`; `OwnedChildWrite.cs` (create, `RefileAsync`) | Every create and every re-file resolves the owner with **all** parents, using the single write path. D-130 cases: SRO if any parent is secure; otherwise the parent's BU default team; with no parent, the acting user's BU default team; if the owner cannot be determined, refuse. No user-owned rows. No direct `owningbusinessunit` writes. People go in Assigned To, not the owner. |
| C4 | Lineage (UAC) | `SecureChildLineage.cs` :91-92 (DR), :133-137 (Signal) | Every filing lookup is a lineage row. The DR row today lists only matter, project and WA; it gains the subject lookups added by C1. The polymorphic pair is not a lookup and is never a lineage row. |
| C5 | Reconciliation reach | `SecureChildReconciler.cs` (on flip); `SecureChildReconciliationJob.cs` (2 min recent-changes pass, writes by default; full sweep gated by `SecureChild:Reconciliation:WritesEnabled`); `SecureRootInheritance.cs` | No ontology code. Reach follows from C4: once the lineage row is complete, a Make Secure or Remove Secure of a root re-owns its Signals and DRs in the same operation as the other children. The 2-minute pass corrects writes made outside the BFF. |
| C6 | Share mirror | `SecureChildShareSynchronizer.cs` (task 149) | No ontology code. A secure root's shares mirror onto its Signals and DRs through the C4 lineage. |
| C7 | Census guard (UAC, F4) | `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` `ChildTables` :42-50 | Add `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision`. Add a guard tying the solution's family tables to `SecureChildLineage.Children` and `config/secure-record-owner-role.json`, so a new family table cannot ship without a lineage row and a role entry. Detect direct `owningbusinessunit` writes. |
| C8 | Append-only (D-34) | DR content columns | After create, only the filing, stamp and owner columns may change, and only through the BFF (C3). The decision-context columns never change. F7 (Console User holds Create on DR and Write on Signal) is an owner role decision; until it is decided, the 2-minute pass is the backstop for ownership only, not for content. |
| C9 | Read paths | Suppression (#1746); Also-resolve (`DecisionCommitService.cs:1079`) | Partition by the **set of secure roots** (F6): ordinary, or the sorted set of secure root ids. Work out a hit's partition from the resolver's current answer, never from the previous episode's owner. A walk result of Undetermined, Failed or a fault is never treated as ordinary. |

## 4. Responsibilities

### spaarke-ontology-platform-r1

1. **Schema delta on `sprk_decisionrecord` (C1).**
   - Add the subject lookups that Signal already carries (`sprk_regarding{communication,document,event,invoice,matter,project,todo,workassignment}`).
   - Add the polymorphic pair if it is absent.
   - Read the live schema first and confirm the column list back to UAC.
   - On `sprk_signal`, confirm the pair exists; add it if not.
   - Deploy with a scoped import only (D-83, `Import-SolutionScoped.ps1`). Never run a full SpaarkeMaster import on dev (#1618).
2. **`DecisionRecordWriter` (F1).**
   - Pass the resolver every parent: the subject of each resolved Signal, every derived core and the DR's own subject.
   - Also-resolve must refuse a Decision Record whose Signals span different secure-root sets. A DR that sums evidence from two secure walls would otherwise be mirrored to both.
3. **`SignalWriter` / `SignalSuppressionService` / `DecisionCommitService` (F2, F3).**
   - Implement the D-130 owner. This means removing the writer-MI ownership and the directly set `owningbusinessunit`.
   - No-core rows go to the acting user's BU default team, never to a user.
   - Replace the `ownerid == caller` read rule with Assigned To or created-by. F2 is blocked on the owner's decision to give BU default teams Read (0x80040299).
4. **Re-file (F5, C2).**
   - Every path that re-files a subject also re-derives the stamps and re-resolves the owner of its Signals and DRs.
   - Do this by registering both tables with `CoreAncestorRestamper`, not with a parallel restamper.
   - Paths covered: the BFF re-file route, the Office save (`ReconciledEmailFiling`) and the AI tool `dataverse.update_record` → `OwnedChildWrite.RefileAsync` (#1747 F1).
5. **D-34 split.**
   - Keep `sprk_corerecordid`, `sprk_corerecordtype`, the policy version and the snapshot immutable.
   - Audit every reader of the typed `sprk_matter`, including the #1747 dedupe key `{policy}|sprk_matter|{matterId}|{episode}` (`SignalWriter.cs:695`). Confirm whether it means "context" (move it to `sprk_corerecordid`) or "filing" (let it follow).
6. **#1746 (C9):** the fix round now running covers F6 and the planner partition.

### unified-access-control-r2

1. **Lineage rows (C4).**
   - Extend `SecureChildLineage` for the new DR subject lookups: 8 of the 9, excluding `sprk_regardingservicerequest`, the same as Signal's row. The typed core columns stay in the row until T5's backfill is verified.
   - **Signal's pair type column is text, not a lookup** (live, deliberate; `SignalWriter.cs:238`). Aligning it is filed as `ontology-deferred`.
   - T4 must make `CoreAncestorRestamper.cs:576` and `CoreAncestorStampReconciliationJob.OrphanClauseFor` treat a pair type column that isn't a lookup as absent.
   - Signal re-files never go through `OwnedChildWrite`'s pair rule.
   - This lands **after** the columns are live on dev, because the lineage reads fail on a column that doesn't exist.
2. **Census guard (C7, F4)**, including the solution↔lineage↔role tie.
3. **F8:** review the budget-revision lineage and role entries when the ontology branch merges.
4. **Review** every ontology PR in this programme against §3 and §5. Re-checks cover the fix diff and its direct callers and callees.
5. **SRO role:** extend `config/secure-record-owner-role.json` (Basic Read) for any table the reconciler starts to re-own, if it is not already listed. Record the refusal first, per `howToExtend`.

## 5. Acceptance criteria (closed set, including negative cases)

Live on dev, with a BFF user who is a member of the matter's BU (U-BU) and a sharee of the secure root (U-S):

1. **Create, secure subject.** A Signal and a DR on a communication filed under secure matter X are SRO-owned at create. Their stamps equal X. U-S reads both; U-BU reads neither.
2. **Create, ordinary subject.** Under ordinary matter M, both are owned by M's BU default team. No row is user-owned.
3. **F1 negative.** The DR resolves a Signal that is secure through its subject (subject under X) while the Signal's core is ordinary M. The DR is SRO-owned, **or** the commit is refused. It is never M-BU-owned.
4. **Re-file ordinary→secure (F5).** Moving the communication from M to X through the BFF re-file route re-owns its Signals and DRs to SRO and re-stamps them to X **in the same operation**. Moving it outside the BFF (OOB form) does the same within one recent-changes cycle (about 2 minutes).
5. **Re-file secure→ordinary.** Moving it back gives M's BU default team ownership. The mirrored shares from X are removed.
6. **D-34.** In 4 and 5, `sprk_corerecordid`, `sprk_corerecordtype`, the policy version and the snapshot are byte-identical before and after.
7. **Two walls, negative.** A DR whose Signals sit under secure roots X and Y (different sets) is refused. No row is mirrored to both X's and Y's sharees.
8. **Root flip.** Make Secure on M re-owns M's Signals and DRs, including a DR reachable only through its subject lookup. Remove Secure reverses it.
9. **No-core.** A Signal or DR with no core is owned by the acting user's BU default team, never by a user.
10. **Guards.** The census fails the build if `sprk_signal`, `sprk_decisionrecord` or `sprk_budgetrevision` loses its lineage row or role entry. It also fails if a writer sets `owningbusinessunit` directly.
11. **Suppression (#1746).** No dismissal stamp crosses between partitions (ordinary, {X}, {Y}, {X, Y}). A subject with an Undetermined walk is never treated as ordinary.

## 6. Sequencing

1. Ontology: the schema delta, confirmed live (§4 ontology item 1), scoped import.
2. UAC: lineage rows plus the census guard in one PR to master. Merged on Router green, deployed.
3. Ontology: the writers (§4 items 2–5), on #1665 (043 rework) and follow-ups. UAC reviews.
4. The live gate (§5) on dev, run by whoever merges last. The record goes in `notes/batch5-live-gates-2026-10-08.md`.
5. Later, if the owner approves the secure-family design: add the `sprk_securefamily` lookup to both tables, like every other family table.

## 7. Mapping to the audit

| Finding | Resolved by |
|---|---|
| F1 | C3 + §4 ontology 2; acceptance 3, 7 |
| F2 | C3 + §4 ontology 3 (blocked on the owner's BU-default-team Read decision) |
| F3 | C3 + §4 ontology 3; acceptance 9 |
| F4 | C7, UAC; acceptance 10 |
| F5 | C1 + C2 + §4 ontology 4, 5; acceptance 4–6 |
| F6 | C9, #1746; acceptance 11 |
| F7 | C8, owner decision |
| F8 | §4 UAC 3 |
| F9 | Owner decision (unchanged by this contract) |

## 8. Open items for the owner

- F2: give BU default teams Read.
- F7: remove Create on DR and Write on Signal from Console User.
- F9: matter-scoped policy visibility.
- The secure-family team type (see the research note).
