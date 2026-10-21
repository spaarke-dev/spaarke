# Task 189 — D7 T7a: the reconciler never widens a document's own narrowing (#2126)

> 2026-10-20 · branch `uac/189-own-narrowing` (worktree `C:/wt189`) from `origin/master` @ `4f5f22694`
> Status: **DESIGN CHECKPOINT — awaiting the main session's review.** No code and no schema change has been made.
> Read with `2119-manage-access-all-forms-design.md` §6 (D7) and `task-173-child-access-permission.md`.

## 0. Summary

- **The defect.** `ChildAccessPermissionReconciler.RunAsync` (`:234-289`) writes `stored := inherited` on every parented
  To Do, Event, Communication and Document. `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync`
  (`:1045-1089`, run after a `/api/v1/child-records/sprk_document` create) does the same inline. Under D7 rule 2 a
  document's own Restricted hides it from partners (task 188), so both writers silently widen it.
- **Why a record is needed.** "Stored Restricted, parent now Standard" is either a narrowing set on the document (must stay)
  or a value inherited from a parent that has since widened (must follow). The stored value cannot tell them apart.
  Task 175 hit the same ambiguity for work assignments and projects and solved it with `sprk_accessinheritance`: the floor
  last applied plus the record's own value.
- **The design.** Put the same record on `sprk_document`: same column name, same v1 JSON, same `AccessInheritance` parser,
  and task 175's own/inherited classification. Three things differ from task 175:
  - the floor is the 173 inherited value (the tops of the filing);
  - a re-file follows the new parents (the POML constraint keeps today's contract);
  - the column is **not** field-secured (§2.4 says why).
- **The rule for a document.** Its stored value is `max(own, inherited)`. Inherited values follow the parents both ways.
  An own narrowing is never widened by the BFF. Everything that is not an own narrowing behaves exactly as today. To Do,
  Event and Communication are untouched: the record lives on documents only.
- **Fail closed.** An unreadable record, an unknown stored option, or a missing record column writes nothing for that row.
  The row is logged with ids and reported.

## 1. Facts checked (master `4f5f22694` + read-only dev metadata, 2026-10-20)

### 1.1 Which child tables carry Access Permission (spaarkedev1)

| Table | `sprk_accesspermission` | `sprk_accessinheritance` |
|---|---|---|
| `sprk_todo`, `sprk_event`, `sprk_communication` | yes, not secured, audited | — |
| `sprk_document` | yes, not secured, audited (table audit on) | **absent** |
| `sprk_workassignment`, `sprk_project` | yes | yes: **secured**, audited (task 175) |

### 1.2 Can an existing `sprk_document` column carry the own narrowing?

No. I listed all 172 custom attributes on dev and none records an access reason. The JSON columns that do exist belong to
other ledgers:
- `sprk_relocationpending` (the relocation ledger);
- `sprk_fileuploadmetadata`;
- `sprk_invoicehintsjson`;
- `sprk_mattersuggestionjson`.

Overloading any of them would be ambiguous.

### 1.3 Who writes a document's `sprk_accesspermission` today

| Writer | Path | Note |
|---|---|---|
| `ChildAccessPermissionReconciler` (L4, 2 min) | every re-file, every create, hand edits, a parent's change, the sweep | **the defect** |
| `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync` (L1) | after `POST /api/v1/child-records/sprk_document` (`ChildRecordEndpoints.cs:306`) | **the same defect, inline**: a caller's Restricted at create is reset |
| `StampAsync` / `DeriveForHostAsync` / `ApplyInheritedAccessPermissionAsync` / `ResolveInheritedAccessPermissionAsync` | — | **no document caller.** Callers: event, communication, To Do, decision record (grep, all call sites) |
| `PUT /api/v1/documents/{id}` | — | `UpdateDocumentRequest` has no access field |
| Model-driven form | Document main form `9088d6a4`: a plain control + `sprk_accesspermission_inherited` OnLoad lock | Locked on a filed document, editable on a parentless one. Quick Create `11d6fcd8` and "Information" `319381f0` have no control |
| Grid/bulk edit, Web API, import, flows | — | Not field-secured: any user with Write on the row can set it |
| T8 fallback (Office, not built) | `OfficeDocumentPersistence` | Will write Restricted when the email cannot be identified |

### 1.4 Dev data (counts only)

There are 596 documents:

| | Restricted | Standard | null |
|---|---|---|---|
| Parented | 26 | 253 | 0 |
| Parentless | 0 | 7 | 310 |

So no parentless Restricted or Limited exists today. The 26 parented Restricted documents equal their floor, because 173's
reconciler has enforced equality since 2026-10-08.

## 2. Design

### 2.1 Where the own narrowing is recorded

`sprk_document.sprk_accessinheritance` (new on this table): Multiple lines of text, MaxLength 4000, not required, audited,
**not field-secured**, on no form, in SpaarkeCore. It holds task 175's v1 record, parsed by the existing
`AccessInheritance`:

```json
{ "v": 1, "parents": ["sprk_matter:3f1c…"], "floorSecure": false, "floorPermission": 100000000,
  "ownSecure": false, "ownPermission": 100000002 }
```

| Field | On a document |
|---|---|
| `floorPermission` | The inherited value (173's tops rule) last applied. A stored value ≠ `max(own, floor)` means someone outside the inherited-value writers changed it |
| `ownPermission` | The document's own narrowing, or `null` (follows the parents) |
| `parents` | The direct filing parents it was derived from (for diagnosis; a re-file does **not** freeze anything, §2.3) |
| `floorSecure`, `ownSecure` | Always `false` (a child is Secure only through its parent, I-2) |

### 2.2 The decision for one document row (one pure function, both writers)

`InheritedAccessPermission.DecideDocument(stored, recordText, parents, inherited)` lives in `ParentLineage.cs`, next to the
existing rule. The own/inherited classification inside it is task 175's. I **extract** it from
`SecureRootInheritance.Decide` (its Access Permission lines, minus the re-file branch) into
`AccessInheritance.OwnPermissionOf(stored, record, floor)`. Both callers use it, and `Decide`'s existing tests prove that
`Decide` is unchanged.

1. The record cannot be parsed (bad JSON, wrong version, unknown option) → **undetermined**. Nothing is written; the row
   is carried and logged with ids only (AC 3).
2. The stored option is not Standard, Limited or Restricted → undetermined, nothing written. Today this is overwritten;
   for documents that would be ranking an unknown value by guess.
3. **Parentless:** the value is never written (round 81, as today). The record is written only when the stored value is
   Limited or Restricted and the record does not already say so: `{parents: [], floor: Standard, own: stored}`. This way a
   later filing knows the narrowing was the document's own. A Standard or null parentless row gets no write, as today.
4. **Parented, inherited undecidable** → as today: undetermined, carried, nothing written.
5. **Parented, inherited = F:**
   - with no record (existing rows, the backfill rule), `own = stored` when stored is stricter than F, otherwise none;
   - with a record, if stored ≠ `max(record.own, record.floor)` it is an edit made outside these writers:
     - stricter than `record.floor` → `own = stored`;
     - equal to it → `own = none`;
     - looser → own unchanged, and the value is put back.
   - target = `max(own, F)`;
   - **write the value** when stored ≠ target;
   - **write the record** when it differs from the stored one. Exception: no record exists and the new one says
     "no own, floor Standard". Then nothing is written, so the Standard majority never gets a write (no `modifiedon` churn).
   - Value and record go in **one** update, so they change together.
6. **Re-read before a document write** (task 175's pattern, verifier c): just before the update, re-read the stored value
   and the record by id. If either changed since the listing, nothing is written: the row is counted
   `changedConcurrently` and carried, and the next run decides again. A user's narrowing made during a run is never
   overwritten. `IGenericEntityService` has no conditional update, so a millisecond window remains (§4, K).
7. **Every write** counts against the existing write cap (`deferred` past it).

**Tables without the record column** (To Do, Event, Communication; and a document table in an environment where the
schema has not landed):
- **To Do / Event / Communication:** today's code path, byte-for-byte (AC 4).
- **`sprk_document` without `sprk_accessinheritance`:** no document is written, and the table is reported in the run's
  `tablesWithoutColumn` with a warning naming the record column. Fail closed: the own narrowing is unreadable (AC 3).

### 2.3 What the rule gives (examples)

| Situation | Today | After 189 |
|---|---|---|
| Filed under a Standard matter; T8, a caller or a hand edit sets Restricted | reset to Standard within 2 min | **stays Restricted** (own); record `{floor S, own R}` |
| Under a Restricted matter (inherited R); the matter widens to Standard | follows to Standard | follows to Standard (`record.own = null`) |
| Existing row under a Restricted matter, no record | — | first run records `{floor R, own null}`; a later widening is followed |
| Own Restricted; the matter turns Restricted, then Standard again | — | R, then R (own kept) |
| Own Limited; the matter turns Restricted, then Standard | — | R, then **L** (own restored) |
| A hand edit to Standard on an own-Restricted row whose floor is Standard | reverted to the floor | **accepted**: the user widened their own narrowing (§5 Q3) |
| A hand edit below the floor | reverted | reverted (never looser than the parents) |
| Re-filed from a Restricted matter to a Standard one, no own | follows to Standard | follows to Standard (POML: keep the contract for rows without an own narrowing) |
| Parentless Restricted, then filed under a Restricted matter that later widens | follows to Standard | **stays Restricted** (own recorded while parentless) |
| To Do / Event / Communication, any case | today | **identical** |

### 2.4 Why the record is not field-secured

- **Securing adds no protection.** A user with Write on a document can already set the stored value (unsecured, and
  editable outside the form). The rule turns any such edit into an own narrowing or a widening down to the floor. A forged
  record can do no more:
  - the target is `max(own, live floor)`;
  - the floor is always recomputed from the parents and is never read from the record;
  - so nothing can go below the parents.
- **Securing adds a widening risk.** A secured column the BFF cannot read answers empty. Overwriting that empty answer with
  a backfill guess can drop a real own narrowing that equals the floor, which would widen later. Task 175 needs a read-proof
  pass for exactly this reason (`SecureRootInheritance.Cascade.cs:1288-1416`, the readability proof and the IsSecured check). It is not worth carrying here.
- **One cost.** The same column name is secured on work assignment and project but not on document. Both data-model docs
  say so, and the schema script's `-Verify` asserts `IsSecured = false` on the document.
- A caller-supplied record in a child-records create is **not** refused, because the caller could write it directly. The
  refusal in `SecureRootFilingGate` stays scoped to work assignment and project.

### 2.5 Who writes the record

| Writer | Sets / clears | How |
|---|---|---|
| `ChildAccessPermissionReconciler` (L4) | floor always; own by classification (§2.2) | value + record, one update, re-read first |
| `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync` (L1, child-records create) | the same decision for a document, including the parentless own record | value + record, one update |
| **T8 fallback writer (Office; not built)** | own = Restricted, **explicitly**, in the create payload | `AccessInheritance.OwnNarrowing(Restricted)` → `{parents: [], floor: Standard, own: R}` beside stored R. **Required:** under a Restricted matter, a fallback without the record equals its floor, so it is read as inherited and lost when the matter widens |
| A user's own choice | — | **No BFF route sets it today.** A user's narrowing arrives as a stored-value edit (form after T5, grid, Web API) and is classified. A future route (T3/T5 Manage Access) calls the same helper |

### 2.6 How a model-driven edit is classified

- **When it is seen.** The edit moves `modifiedon`, so the next run's recent-changes listing picks the row up (≤ 2 min).
  §2.2 step 5 then classifies it:
  - **stricter than the last floor:** own;
  - **equal to the last floor:** cleared;
  - **looser than the floor:** put back.
- **Which paths can edit today.** The Document form still locks the control on a filed document. T5 changes that lock
  ("on a filed document the pill may be set to Restricted"); it is not part of 189. Until then the paths are grid/bulk
  edit, the Web API, imports, flows and the BFF writers.
- **A narrowing equal to the inherited value is not recorded as own.** It follows the parent: the owner's words are
  "follows its parent (unless more restricted)".

### 2.7 Legacy rows (no record)

- **The rule** (task 175's backfill): stricter than the current floor is own; equal to it is inherited; looser is put back.
- **Why it is sound on master.** 173's reconciler has kept every parented child equal to its floor since 2026-10-08. So a
  parented legacy row cannot hold an own narrowing that equals its floor. The only own narrowings that exist today are on
  parentless rows, and the reconciler records those on first sight (§2.2 step 3). On dev that is 0 rows.
- **Trade-off (fail closed).** A parented row whose parent widened inside the deploy window (old code had not followed yet;
  new code sees no record) is classified own and stays Restricted. Partners lose it until someone widens it by hand. It is
  visible in the run report (`ownKept`) and bounded by the window. Treating every legacy Restricted as own instead would
  break AC 2 for the 26 dev rows (they would never follow a widening matter), so it was rejected.

### 2.8 Migration and read-back

1. **Schema.** Extend `scripts/Set-DocumentAccessPermissionSchema.ps1`, which already owns the document access column,
   with the record column:
   - dry run by default; `-Apply` creates it **unsecured** and in SpaarkeCore;
   - `-Verify` reads it back: exists, Memo, MaxLength ≥ 1000, `IsSecured = false`, in SpaarkeCore, on no `sprk_document`
     form;
   - `-SelfTest` covers it offline.
   - Mirror the attribute into `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Document/Entity.xml` (as task 175
     did for work assignment and project, with `IsSecured 0`).
   - **The agent runs only `-SelfTest` and the dry run.** `-Apply` on dev is the main session's step after this checkpoint.
2. **Deploy order.** Schema `-Apply` → `-Verify` (exit 0) → BFF (via R3). On its first run after the restart, the job's
   sweep records the 26 parented Restricted rows (dev): about 26 record-only writes, inside the cap. Then `-Verify` again.
   - **If the BFF lands before the schema,** documents are skipped and reported (fail closed). Nothing is widened or
     narrowed on documents until the column exists.
3. **Rollback.** A BFF without 189 ignores the column, and the column can stay.
4. **Report and logs.** The job's `accessPermission` result gains `recordsWritten`, `ownKept` and `changedConcurrently`.
   Each `set:` log line gains `own={Own}`. The live gate checks that no `set:` line on a document goes below its `own`
   (App Insights, ids only).

## 3. CLAUDE.md §11 — component justification

**The record column** (`sprk_document.sprk_accessinheritance`):
1. **Existing:** `sprk_accessinheritance` on work assignment and project (task 175) records own vs inherited for round 87's
   floor. Nothing on `sprk_document` can carry it (§1.2). Dataverse audit history was rejected:
   - T8's fallback and the inherited value are both written by the BFF application user;
   - audit can be disabled or purged;
   - a per-row audit read is costly.
2. **Extension:** this IS the extension. The same column name, the same v1 contract and the same `AccessInheritance`
   parser go on one more table, and task 175's classification is extracted and shared rather than copied. No new job,
   endpoint, interface, DI registration or package.
3. **Cost of doing nothing:** within one 2-minute run the BFF resets a filed document's own Restricted to its parent's
   Standard. Affected:
   - T8's fallback for an unidentifiable internal-only email;
   - a parentless Restricted document once filed;
   - a caller's Restricted at a child-records create.

   Under task 188's rule 2 that document becomes visible to partners, which breaks D7. "Never lower a document" without a
   record breaks AC 2: an inherited Restricted would stay after its matter widens, hiding documents from partners granted
   that matter.

**The helpers** (`AccessInheritance.OwnPermissionOf`, `AccessInheritance.OwnNarrowing`, `InheritedAccessPermission.DecideDocument`):
1. **Existing:** `SecureRootInheritance.Decide`, `InheritedAccessPermission.ResolveAsync`.
2. **Extension:** `OwnPermissionOf` is lifted out of `Decide`, not written anew. `DecideDocument` composes it with
   `ResolveAsync`'s value.
3. **Cost of doing nothing:** a second copy of the classification would drift from task 175's.

**Placement (bff-extensions.md, ADR-052):** BFF, inside the existing `SecureChildReconciliationJob` and the existing L1
refresh. Expected publish-size delta about 0, to be measured.

## 4. Known limits (K-class, stated up front)

- **K1 — a coinciding narrowing.** A user narrows a document to exactly the value its parent turns to within the same
  ≤ 2-minute window. The edit is classified inherited and follows a later widening. This needs two independent edits in one
  window. The consequence is today's behaviour.
- **K2 — millisecond race.** The re-read narrows the listing-to-write race to milliseconds but cannot close it, because
  there is no conditional update.
- **K3 — the form label.** The form still says "inherited from {parent}" on an own-narrowed filed document until T1/T5 read
  the record (S1 can expose `own`).
- **K4 — the deploy window.** Existing rows whose parent widened during the deploy window are kept as own (§2.7). This
  fails closed and is reported.

## 5. Questions for the main session / owner (recommendation first)

| # | Question | Recommendation |
|---|---|---|
| Q1 | The record column: reuse `sprk_accessinheritance` unsecured on `sprk_document` | **Yes** (§2.4). Alternatives: secured (needs task 175's read-proof machinery, no security gain), or a distinct name (a second record type for the same concept) |
| Q2 | Extract the classification from `SecureRootInheritance.Decide` (touches task 175's file; its tests prove no change) | **Yes**: one owner of the rule. Alternative: a documented copy in `InheritedAccessPermission` |
| Q3 | Who may remove an own narrowing | **Any user with Write**, by an edit (D7 note: "a user can widen it later only to the root's level"). Round 87's F3 for work assignment and project would need the stored value secured or a BFF route: a T5/T3 question, not 189 |
| Q4 | A document's own narrowing does not reach the documents or To Dos filed under it. 173's rule reads only the TOPS of a filing, so a user's Restricted on a `.eml` does not hide its attachments | **Out of 189**: it would change the contract for rows without an own narrowing. Raise with the owner; 188's rule 3 covers the email-derived case only |
| Q5 | T8 contract | **Relay to word-add-in-r1:** T8's fallback writes `AccessInheritance.OwnNarrowing(Restricted)` in the same payload as stored Restricted (§2.5) |
| Q6 | Order | 189 merges before T8 and before T5 unlocks the form pill. It is independent of 188: rule 2 reads the stored value, which 189 never lowers below an own narrowing, and a lagging inherited Restricted fails closed |

## 6. Implementation plan (after approval)

| File | Change |
|---|---|
| `Services/Access/SecureRootInheritance.Cascade.cs` | Extract `AccessInheritance.OwnPermissionOf`; add `OwnNarrowing`; extend the type's doc to name the document table and its differences |
| `Services/Dataverse/ParentLineage.cs` | `InheritedAccessPermission.RecordTables = { sprk_document }`; `DecideDocument` (pure) |
| `Services/Access/ChildAccessPermissionReconciler.cs` | Read the record on documents; decide through `DecideDocument`; one update; re-read; counters; record-column check |
| `Services/Dataverse/CoreAncestorResolver.cs` | `RefreshInheritedAccessPermissionAsync`: the same decision for documents |
| `Services/Access/SecureChildReconciliationJob.cs` | Report and log the new counters |
| `tests/integration/data-mutation/ChildAccessPermission/ChildAccessPermissionReconcileTests.cs` | New tests (below); the existing six unchanged |
| `tests/integration/data-mutation/ChildAccessPermission/InheritedAccessPermissionStampTests.cs` | Inline refresh: a document created Restricted under a Standard matter is kept |
| `tests/integration/data-mutation/ExternalAccess/SecureChildShareWorld.cs` | Fixture: a two-column update; the record column in the probe for `sprk_document`; a re-read hook |
| `scripts/Set-DocumentAccessPermissionSchema.ps1`, `scripts/README.md`, `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Document/Entity.xml` | Schema |
| `docs/data-model/child-access-permission.md`, `docs/data-model/access-inheritance.md`, `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (I-16) | The rule. I-16 amendment: "a parented Document holds max(own narrowing, inherited); the BFF never lowers it below its own narrowing (`sprk_accessinheritance`, task 189)". I-14's D7 text is task 188's |

**Tests.** Must fire:
- own Restricted survives a Standard parent (AC 1);
- the parentless own is recorded and survives filing plus a widening;
- the T8-style explicit own survives a widening.

Must not fire:
- an inherited Restricted follows a widening (AC 2);
- the legacy no-record row follows after the first run;
- a hand edit below the floor is put back.

Fail closed:
- an unreadable record, an unknown option, or a missing record column → nothing written (AC 3);
- a concurrent edit → skipped and carried.

Unchanged:
- the existing six reconciler tests and `Decide`'s tests (AC 4).

**Mutation table** (each new test must turn red):
- `own := null` always;
- ignore the record (backfill every run);
- treat an unreadable record as none;
- drop the re-read;
- drop the parentless record write.

**Guard named, not built** (Step 9.5 rule 6): an ArchTest source scan that fails when code outside `DecideDocument` writes
`sprk_accesspermission` on `sprk_document`. The stamp-path writers have no document caller today (§1.3).
