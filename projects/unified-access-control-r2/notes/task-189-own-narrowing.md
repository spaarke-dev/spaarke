# Task 189 — D7 T7a: the reconciler never widens a document's own narrowing (#2126)

> 2026-10-20 · branch `uac/189-own-narrowing` (worktree `C:/wt189`) from `origin/master` @ `4f5f22694`
> Status: design checkpoint **approved** by the main session (2026-10-20; decisions in §7). Implemented as §6 planned;
> evidence (tests, mutation table, gates, schema read-back) in §8.
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
| Filed under a Restricted matter (inherited R), then un-filed | keeps R (round 81) | keeps R, and R is recorded as its own (§2.2 step 3), so a later filing under a Standard matter keeps it. Fails closed |
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
  - **unchanged** (equal to max(own, last floor)): the own value stands, and the inherited part follows the live floor;
  - **stricter than the last floor OR the live one:** own;
  - **at or below both:** put back to the live floor. The own value is cleared, unless it is no looser than the edit
    (task 175's value-first, record-last write, §8.7).

  The first version compared an edit with the last floor only. That was a defect (the "K1 variant"), fixed in §8.7.
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

- **K1 — a coinciding narrowing on a row with no record.** A user narrows a document that has no record yet (it has
  only inherited Standard, or the sweep has not reached it) to exactly the value its parent turns to, within the same
  ≤ 2-minute window. The backfill rule reads the edit as inherited, and it follows a later widening. This needs two
  independent edits in one window, and the consequence is today's behaviour. With a record, the same edit is stricter
  than the last inherited value and is kept.
- **Not a known limit: the "K1 variant"** (an edit after the parents' value changed, before the next run, judged against
  the LAST inherited value). The main session classed it as a defect. It is fixed for documents and for task 175 (§8.7).
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
| Q4 | A document's own narrowing does not reach the documents or To Dos filed under it. 173's rule reads only the TOPS of a filing, so a user's Restricted on a `.eml` does not hide its attachments | **Decided (D4): handled by task 188 at read time** — an ancestor document's own Restricted on the ≤ 3-hop `sprk_parentdocument` walk hides its descendants. 189 does not change the tops rule and adds no stored cascade |
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

## 7. Decisions (main session, 2026-10-20, on design commit `0b0fe969ee`)

| # | Decision |
|---|---|
| D1 | **Column approved:** `sprk_document.sprk_accessinheritance`, task 175's v1 format and `AccessInheritance` parser, **not field-secured**. Security justification (§2.4, §11): the stored value is `max(own, inherited)`, and the inherited part is always recomputed from the parents, never read from the record. So a user's edit — of the stored value or of the record — can only (a) remove their own narrowing, which D3 allows, or (b) add a narrowing. Neither can take a document below its parents' live value. Securing the record would add no protection and would add the hidden-read widening risk. |
| D2 | **Classification:** extracted from `SecureRootInheritance.Decide` into ONE shared copy (`AccessInheritance.OwnPermissionOf`). Task 175's existing tests must pass unchanged. |
| D3 | **Removing an own narrowing:** any user with Write (D7). A Full Access / creator (F3) gate belongs to T3/T5 (Manage Access), not 189. |
| D4 | **A Restricted `.eml` and its attachments:** **handled by task 188 at read time.** An ancestor document's own Restricted, on the same ≤ 3-hop `sprk_parentdocument` walk, hides its descendants. Consistent with the owner's D7 answer ("follows its parent unless more restricted"). No stored cascade is needed, and 189 does not change the tops rule. |
| D5 | **T8:** the main session relays to word-add-in-r1 that the fallback writer writes the own-narrowing record in the same payload as stored Restricted. 189 exposes `AccessInheritance.OwnNarrowing`. |
| D6 | **Order:** 189 merges before T8 and before T5 unlocks the pill. It has no dependency on 188. |
| — | **The second writer is in scope:** `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync`, with a must-fire test (a create with Restricted under a Standard parent stays Restricted). |
| — | **K1 accepted:** the same value set within the same 2-minute window. |
| — | **Schema:** after code and tests are green, `-Apply` then `-Verify` on spaarkedev1 ONLY. Mirror into `sprk_Document/Entity.xml`. No other environment, and no field-security, role or app-setting change. Draft PR; no deploy. |

## 8. Implementation and evidence (2026-10-20)

### 8.1 Commits (branch `uac/189-own-narrowing`, base `origin/master` @ `4f5f22694c`)

| Commit | What |
|---|---|
| `5b8811fbe1` | BFF: `AccessInheritance.OwnPermissionOf` (extracted from `SecureRootInheritance.Decide`) and `OwnNarrowing`; `InheritedAccessPermission.RecordTables` / `DecideDocument`; the reconciler's document path (one update, re-read, counters, record-column check); `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync` for documents; the job's report and log; 15 tests and the fixture |
| `d76897f23c` | Schema: `scripts/Set-DocumentAccessPermissionSchema.ps1` (the record column; `-SelfTest` 17 checks), `scripts/README.md`, `sprk_Document/Entity.xml` (`IsSecured 0`) |
| `afad901388` | `docs/data-model/child-access-permission.md`, `docs/data-model/access-inheritance.md`, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-16 |
| `7c2aa42548` | this file: §5 Q4 (D4), §2.3 un-file row, §4 K1 variant, §8 |
| `85e7250116` | Fix round: the live comparison in `AccessInheritance.OwnPermissionOf` (§8.7) and 5 tests |
| `3629c9131a` | Fix round: the reconciler's remark (a document's value is not display only; D7, task 188) |
| `7a71342215` | Fix round: deploy order (customer deployment guide §7.4), the data-model docs, I-16 |
| `36527b2470` | Fix round: `DocumentAccessPermissionWriterGuardTests` (§8.7) |
| (this commit) | this file: §2.6, §4, §8.7 |

### 8.2 Tests

- **New:** 11 in `ChildAccessPermissionReconcileTests` (through the job, as the scheduler runs it) and 4 in
  `InheritedAccessPermissionStampTests` (the inline refresh). The six existing reconciler tests and task 175's tests are
  unchanged.
- **Filtered suite** (the baseline filter: `ChildAccessPermission | InheritedAccessPermission | ChildAccessCascadeTests |
  SecureRootInheritance | ParentLineageTests | SecureChildReconciliationJob | ChildRecordEndpoints`): **305 → 320, all
  pass.**
- **Every namespace that uses the fixture, the resolver or the reconciler** (20 namespaces): **10039 passed, 0 failed,
  9 skipped** (the skips are existing `[Skip]` Office contract tests).
- **ArchTests (full project): 1208 / 1208 pass.**
- **Fails before.** The new tests call new APIs (`AccessInheritance.OwnNarrowing`, the fixture's two-column update), so
  master's files cannot be copied back under them. The before-behaviour is reproduced instead by routing documents
  through the old code path (M6, M7b below): every must-fire test turns red.

### 8.3 Mutation table (each run: one perturbation, build, the two test classes, restore)

| # | Perturbation | Red (of the 15 new) |
|---|---|---|
| M1 | `own := null` always (`DecideDocument`) | 6: own Restricted under Standard; parentless own survives filing + widening; Office fallback record; edited back to floor; narrowed mid-run; refresh create Restricted under Standard |
| M2 | ignore the record (the backfill rule every run) | 3: inherited follows both ways; legacy row follows a widening; edited back to floor |
| M3 | an unreadable record read as none | 1: unreadable record not written |
| M4 | drop the re-read | 1: narrowed between listing and write |
| M5 | drop the parentless record write | 2: parentless own survives filing + widening; refresh parentless narrowing recorded |
| M6 | the refresh takes the old path for documents | 4: all four refresh tests |
| M7 | `if (false && _recordTables.Contains(table))` | **invalid** (does not compile: CS8602 in the then-unreachable local function); replaced by M7b |
| M7b | documents never take the record path (old reconciler behaviour) | 9 of 11 reconciler tests. The two that stay green are the ones whose old behaviour is correct: a hand edit below the floor is put back, and a missing record column skips documents (decided before the path) |

Every new test is red under at least one perturbation.

### 8.4 Gates

- **Build:** `dotnet build src/server/api/Sprk.Bff.Api/` — 0 warnings, 0 errors.
- **Publish size** (bff-hygiene item 4): `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`,
  framework-dependent linux-x64, Release, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`,
  PDBs included (4 each), from fresh detached worktrees `C:/wt189m` (master `4f5f22694c`) and `C:/wt189b` (`5b8811fbe1`).
  192 files each. **Master 39,314,279 B (37.49 MB); branch 39,320,210 B (37.50 MB); delta +5,931 B (+0.006 MB).**
- **CVE:** `dotnet list package --vulnerable --include-transitive` — no vulnerable packages. No package changed.
- **ADR / constraints:** no endpoint, DI registration, package, job or timer added (ADR-001/010/036/052); no plugin
  (ADR-002); the write stays in the existing job and the existing L1 refresh.

### 8.5 Schema on dev (spaarkedev1 only, the main session's approval in §7)

- `-SelfTest`: PASS, 17 checks.
- Dry run, then `-Verify` before the apply: **VERIFY FAIL: record absent** (the check fires).
- `-Apply`: `created sprk_document.sprk_accessinheritance (not secured) under SpaarkeCore`; `published sprk_document`;
  exit 0.
- `-Verify`: **VERIFY PASS**, exit 0 — Access Permission `match`, ships in SpaarkeCore (ViaTable); record
  `match (type Memo, MaxLength 4000, IsSecured False)`, ships in SpaarkeCore; on no form.
- Independent metadata read: `sprk_accessinheritance` / `sprk_AccessInheritance`, MetadataId `ac9462bf…`,
  IsSecured false, MaxLength 4000, Format TextArea, audited, RequiredLevel None.
- No other environment, no field-security, role, form or app-setting change. The deployed dev BFF (`4f5f22694c`) does not
  read the column; nothing is deployed by this task.

### 8.6 Found in implementation

- **A K1 variant** (§4): an edit made inside the window in which the parents' value changed was judged against the last
  inherited value. Shared with task 175 by D2. Fixed in the fix round (§8.7).
- **An un-filed document's last value is recorded as its own** (§2.3 new row). It follows from §2.2 step 3; it fails
  closed.
- **Cost:** one extra column-metadata read per job run (the reconciler checks `sprk_document` for the record column), and
  one per child-records document write (the refresh). The walk's column cache filters to the columns it selects, so it
  cannot answer the question.
- **Report-only mode** counts `wouldChange` for value writes only; a record-only plan is logged, not counted, and
  `recordsWritten` stays 0.
- **Deploy order** (§2.8): on dev the schema is in place, so a BFF with 189 reconciles documents from its first run. On
  any other environment, run the schema script first.

### 8.7 Fix round (main session, 2026-10-20)

**1. The "K1 variant" is a defect, fixed in the shared rule** (`AccessInheritance.OwnPermissionOf`; task 175 and task
189 both use it). A stored value is read against the record (own, last floor) and the live floor:

| Stored value | Own after | Why |
|---|---|---|
| equal to max(own, last floor) | own (unchanged) | no edit since the record was written; the inherited part follows the live floor both ways |
| stricter than the last floor OR the live one | the stored value | a user's (or BFF writer's) narrowing |
| at or below both | own if it is no looser than the stored value, else none | put back to the live floor; a user with Write removes an own narrowing (D3) |

- **Before → after (the K1 variant).** The matter goes Restricted → Standard, and before the next run a user sets the
  document (or work assignment) to Limited. Before, Limited was below the last floor, so it was put back and the record
  went to Standard. After, Limited is stricter than the live Standard, so it is the record's own and stays Limited.
- **Where it differs from the proposed rule, and why.** The proposal compared every changed value with the live value
  alone. Taken literally, it widens in three cases, so these keep the stricter reading:
  - an unchanged own narrowing equal to the last floor (the T8 fallback under a Restricted matter, `own R`, `floor R`)
    would read as inherited and follow a widening;
  - a narrowing set while the matter tightens to the same value (`floor L` → live R, the user sets R) would be cleared
    and lost when the matter loosens;
  - an own value widened by a user while the matter tightens (`own R`, `floor S` → live L, the user sets L) would be
    cleared instead of kept at L.

  The "no looser than the edit" clause protects task 175's write order. Its value goes first and its record last, so a
  record write that fails after the value followed a LOWER floor leaves max(own, live floor) beside the old record.
  Clearing own there would lose an own value hidden under the floor.
- **Other behaviour change (both tasks).** An edit BELOW the floor now removes the own narrowing and is put back to the
  live floor. Before, it kept the own value, so setting Standard under a Limited matter left an own Restricted at
  Restricted, while setting Limited removed it. A looser edit now never ends stricter than an edit to the floor itself.
- **Task 175's tests.** None encodes the old comparison against a moved floor. All pass unchanged: `ChildAccessCascadeTests`,
  `SecureRootInheritance*`.
- **New tests (5).**
  - Reconciler (documents):
    - `Run_ALimitedSetAfterItsMatterWidened_IsKeptAsTheDocumentsOwn` — the 189-shape must-fire, with the must-not-fire
      beside it: an unchanged inherited Restricted follows back to Standard;
    - `Run_ARestrictedSetWhileItsMatterTightened_IsKeptAsTheDocumentsOwn`;
    - `Run_ADocumentEditedBelowItsFloor_DropsItsOwnNarrowing_AndIsPutBackToTheFloor`.
  - Cascade (task 175):
    - `ALimitedSetAfterTheParentWidened_IsKeptAsTheChildsOwn` — the 175-shape must-fire, with the same must-not-fire;
    - `AnOwnLimitedUnderTheFloor_SurvivesARecordWriteThatFailedAsTheFloorDropped`.
  - The existing must-not-fire tests (`Run_ADocumentsInheritedRestricted_FollowsItsMatter_BothWays`,
    `TheJob_MakesAChildsAccessPermissionFollowItsMatter_BothWays…`) pass unchanged.
- **Mutants** (the filter now also runs `ChildAccessCascadeTests` and `SecureRootInheritance*`; 315 tests):

  | # | Perturbation | Red |
  |---|---|---|
  | M8 | revert to the last-applied comparison (the old body; this is also "fails before") | 3: the K1 variant (both shapes), the edit below the floor |
  | M9 | an edit at or below both floors always clears own | 1: the failed record write |
  | M10 | an edit judged against the live floor only (the proposal's comparison) | 1: the narrowing set while the matter tightens |
  | M11 | an edit judged against the last floor only | 2: the K1 variant (both shapes) |

**2. The writer guard** (`tests/Spaarke.ArchTests/DocumentAccessPermissionWriterGuardTests.cs`, enforcement ladder: an
ArchTest source scan).
- **What it does.** It finds every write of `sprk_accesspermission` in `src/server/**`, by its literal and by its
  constants, and attributes each to its member (method or local function). There are 11 sites.
- **Lists.**
  - Approved document writers: the reconciler's `DecideDocumentAsync` and `CoreAncestorResolver.RefreshDocumentAccessPermissionAsync`.
  - Non-document writers, six, each with its reason: the reconciler's and the refresh's non-document paths, task 175's
    `WritePermissionAsync`, the communication association and draft, and the event create payload.
  - **Ratchet baseline (existing violations):** `CoreAncestorResolver.StampAsync` (both overloads) and
    `ApplyInheritedAccessPermissionAsync`. The shared stamp path writes the bare inherited value for any host that
    `InheritedAccessPermission.AppliesTo` admits, documents included, without the record. It has no document caller
    today (event, communication, To Do, decision record). Recommended follow-up: fence documents out of the stamp path.
- **Checks.** NEW and STALE entries fail, and a second test fails on a new constant that holds the column. The control
  runs the detector over an inline source:
  - must fire: 12 sites in 11 members, including the outer method after a local function, a tuple return and a `}`
    inside a string;
  - must not fire: a read, a comparison, a column list, a set, a comment, another column's constant, a log message, a
    one-argument `Add`.
- **Guard mutants** (each one applied, ArchTests built, guard run, file restored). Each one fails the guard:

  | # | Change | Result |
  |---|---|---|
  | G1 | a new writer in the BFF | NEW |
  | G2 | a listed member renamed | NEW + STALE |
  | G3 | a baseline entry for no member | lost signal |
  | G4 | the key loses `InheritedAccessPermission.Column` | non-vacuity + control |
  | G5 | a new constant holding the column | alias test |
  | G6 | strings not blanked for the member detector | control |

  G0 (nothing listed) printed all 11 sites with the expected members.

**3. Also.**
- The deploy-order rule is in `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.4 and the PR body: the schema first, then the
  BFF. Until the column exists, the BFF writes no document's Access Permission.
- An un-filed document keeps its last value as its own. This is stated in the PR body and fails closed.
- `ChildAccessPermissionReconciler`'s remark now says a document's value is not display only (D7, task 188). The I-16
  line conflicts textually with #2170, which carries the same meaning.

**Gates.**
- Build: 0 warnings, 0 errors.
- Filtered baseline suite: 320 → **325, all pass**.
- Wide run over 20 namespaces: **10044 passed, 0 failed, 9 skipped**.
- ArchTests (full project): 1208 → **1211 / 1211**.
- Publish size: master `4f5f22694c` 39,314,242 B; branch `36527b2470` 39,320,206 B; delta **+5,964 B**. Fresh
  worktrees, same conditions as §8.4, 192 files and 4 PDBs each.
- CVE: none; no package changed.
- Conflicts (`git merge-tree`): #2170 has one hunk in I-16, as expected; #2150 has the known one hunk in
  `SecureChildShareWorld`.
