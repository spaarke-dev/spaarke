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
- **Fail closed, never below the parents.** An unknown stored option writes nothing for that row. An unreadable record,
  or a missing record column, only RAISES the row to what its parents give and never lowers it (fix round C, K1+K2,
  §8.9). The row is logged with ids and reported. **The schema goes before the BFF:** a document raised while the column
  is missing, whose parents then widen, keeps the stricter value, and the first run after the script records it as the
  document's own (security pass 2, §2.8).

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

1. The record cannot be parsed (bad JSON, wrong version, unknown option) → **undetermined**, carried and logged with ids
   only (AC 3). Its value is only raised to what its parents give, never lowered, and the record is left as it is (fix
   round C, K1+K2: `DecideDocumentRaiseOnly`; the first design wrote nothing).
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
   - **write the record** when there is none, or when it differs from the stored one. Every parented row gets its
     record, the Standard majority too (fix round C, F1, §8.9; the first design skipped "no own, floor Standard" to save
     the `modifiedon` churn).
   - Value and record go in **one** update, so they change together.
6. **Re-read before a document write** (task 175's pattern, verifier c): just before the update, re-read the stored value
   and the record by id. If either changed since the listing, nothing is written: the row is counted
   `changedConcurrently` and carried, and the next run decides again. A user's narrowing made during a run is never
   overwritten. `IGenericEntityService` has no conditional update, so a millisecond window remains (§4, K).
7. **Every write** counts against the existing write cap (`deferred` past it).

**Tables without the record column** (To Do, Event, Communication; and a document table in an environment where the
schema has not landed):
- **To Do / Event / Communication:** today's code path, byte-for-byte (AC 4).
- **`sprk_document` without `sprk_accessinheritance`:** decided raise-only (fix round C). A document below what its
  parents give is raised to it (the value alone, `raisedOnly`), none is lowered, and the record column is neither read nor
  written. The table is reported in the run's `tablesWithoutColumn` with a warning naming the record column. A raise made
  then is kept, and recorded as the document's own once the column exists (§2.8).

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
2. **Deploy order: the schema MUST go before the BFF.** Schema `-Apply` → `-Verify` (exit 0) → BFF (via R3). On its
   first runs after the restart, the job's sweep gives every parented document its record (fix round C, F1: 279 on dev,
   about 2 runs; §8.9). Then `-Verify` again.
   - **If the BFF lands before the schema** (security pass 2 corrected this paragraph), documents are decided raise-only:
     one below what its parents give is raised to it, none is lowered, and `sprk_document` is listed in
     `tablesWithoutColumn`. A raised document whose parents then widen keeps the stricter value. On the first run after
     the script, that value is stricter than what its parents give and the document has no record, so it is recorded as
     the document's OWN narrowing and is never widened again.
   - **Finding and fixing them** (`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.4): in that window, the job's `set:` lines on
     `sprk_document` that say `own unknown` (counted in `raisedOnly`), and the child-records refresh's
     `[INHERITED-ACCESS-PERMISSION]` warnings naming the missing record column. In the first runs after the script, each
     of those whose `set:` line has an `own` value stricter than `inherited` (counted in `ownKept`) was kept by the raise,
     not by a user. The operator widens each by hand: an edit back to the value its parents give removes the own
     narrowing (D3).
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

- **K1 — a coinciding narrowing on a row with no record.** A user narrows a document that has no record yet (since
  fix round C, F1, only a legacy row the sweep has not reached; before it, also one that only inherited Standard) to
  exactly the value its parent turns to, within the same ≤ 2-minute window. The backfill rule reads the edit as inherited, and it follows a later widening. This needs two
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
| `42e64bf51f` | this file: §2.6, §4, §8.7 |
| `51cb5fb94b` | Fix round 2: documents fenced out of the stamp path (§8.8) and 5 test cases |
| `7933cf456f` | Fix round 2: the guard's baseline is empty; the fenced stamp members are non-document writers |
| `a8b301292b` | this file: §8.8; the data-model doc and I-16 |
| `c20e9882aa` | Fix round C, F3: the writer guard finds a multi-line dictionary initializer entry (§8.9) |
| `b7f74100f3` | Fix round C, K1+K2: raise only when a document's own narrowing cannot be known (§8.9) |
| `f69b007da8` | Fix round C, K5: app-only generic writers refuse a document's Access Permission and record (§8.9) |
| `abcf516022` | this file: §8.9 |
| `7c7b245b10` | Fix round C, F1: every parented document gets its record (§8.9; committed on the main session's decision) |
| `4546c0d6ed` | this file: §8.9, F1 and the deploy-order gate |
| `c8c591777f` | Security pass 2, K1: the field-mapping push skips a document's Access Permission and record (§8.10) |
| `48869bc7c5` | Security pass 2, F1: a BFF before the schema keeps its raises as the document's own; guide, notes, guard, script (§8.10) |
| (this commit) | this file: §8.9 gate timed from the run's end (F4), §8.10 |

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
  BFF. (Corrected since: until the column exists, the BFF only raises a document's Access Permission and never lowers
  it, fix round C; and a raise kept then becomes the document's own once the column exists, security pass 2, §2.8.)
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

### 8.8 Fix round 2 (main session, 2026-10-20): the stamp path is fenced

- **Decision.** The main session accepted the §8.7 deviation from the literal rule, the "no looser" clause and the
  edit-below-floor behaviour. It asked for the stamp path to be fenced now rather than filed.
- **The fence.** `CoreAncestorResolver.InheritedAccessPermissionAsync` is the helper that `StampAsync` (both overloads),
  `DeriveForHostAsync`, `ApplyInheritedAccessPermissionAsync` and `ResolveInheritedAccessPermissionAsync` take the
  inherited value from. It now returns none for a table that records its own narrowing
  (`InheritedAccessPermission.RecordsOwnNarrowing`, i.e. `sprk_document`). `ResolveInheritedAccessPermissionAsync` returns
  before it reads the row.
- **Before → after.** A document passed through the stamp path:
  - before: got the bare inherited value in its payload, which lowered an own narrowing (no caller did this today);
  - after: gets none.

  To Do, Event and Communication are unchanged.
- **Why a fence and not the guard.** The guard sees write sites, not callers, so a new document caller of `StampAsync`
  would pass it. The fence is what stops the bare-value write. The guard's ratchet baseline is now empty, and the
  fenced members are listed as non-document writers with the fence as their reason.
- **Tests (2 new methods, 5 cases, in `InheritedAccessPermissionStampTests`).**
  - `StampAsync_UnderARestrictedMatter_CarriesTheInheritedValue_ExceptOnADocument`, a theory over both payload shapes:
    - must fire: `sprk_document` gets no `sprk_accesspermission`;
    - must not fire: `sprk_todo`, `sprk_event` and `sprk_communication` carry Restricted.
  - `TheOtherStampPathMembers_GiveADocumentNoValue_AndResolveDoesNotReadIt`: Apply, Derive and Resolve give a document
    none, and Resolve makes no read of the document.
- **Mutants.**

  | # | Perturbation | Red |
  |---|---|---|
  | M12 | remove the helper's fence | 2: the document theory case, the Apply/Derive/Resolve test |
  | M13 | remove the Resolve fence | 1: Resolve reads the document |

- **Guard mutants G0–G6**, re-run after the baseline change, all fail the guard as before. G3 is now a listed member
  that no longer writes, which reports STALE.
- **Accepted limit** (main session): the scan cannot see a key held in a local variable, or payloads from the generic
  update writers and playbooks. It is listed in the guard's remarks and the PR body.
- **Gates.**
  - Build: 0 warnings, 0 errors.
  - Filtered baseline suite: 325 → **330, all pass**.
  - Wide run, first attempt: 1 failure. `ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_FailsLoudlyAndLogsTheContainerId`
    hit its 30-second wall-clock wait while two publishes ran beside it. It passed 4 times alone. It is not in code
    this task touches, and it is reported to the main session (not filed).
  - Wide run, rerun without the load: **10049 passed, 0 failed, 9 skipped**.
  - ArchTests: **1211 / 1211**.
  - Publish size: master `3531ed65e3` (no `src/server` change since the base `4f5f22694c`) 39,314,243 B; branch
    `7933cf456f` 39,320,170 B; delta **+5,927 B**.
  - CVE: none.

### 8.9 Fix round C (main session, 2026-10-20): the first security pass

The pass came back FIX-ROUND-NEEDED. One commit per item, each with must-fire and must-not-fire tests and mutants.

- **F3 (`c20e9882aa`). The writer guard missed the usual multi-line dictionary initializer.**
  - Before: the initializer pattern needed `{` or `,` before the entry, so an entry that starts its own line was not
    seen.
  - After: `(?:^|[{,])\s*\{\s*KEY\s*,`.
  - Controls: a multi-line initializer with the literal key and with the constant (must fire); a multi-line set holding
    the column and a multi-line dictionary of another column (must not fire).
  - The real-source scan found no new site. Guard mutant G7 (the old pattern) fails the control.
- **K1+K2 (`b7f74100f3`). Raise only, when the own narrowing cannot be known.**
  - Before: an unreadable record (not JSON, another version, an unknown level inside it), or an environment without the
    record column, wrote nothing. A Standard document under a Restricted matter stayed Standard.
  - After: `InheritedAccessPermission.DecideDocumentRaiseOnly`. Whatever the own narrowing is, max(own, inherited) is at
    least the live floor, so a stored value below a decided floor is raised to it. Only the value is written; the record
    is untouched. A value at or above the floor is kept, nothing is lowered, and an unknown stored level still writes
    nothing.
  - The reconciler decides documents without the record column raise-only instead of skipping them, and never asks for
    the column (its re-read compares the value alone). The create refresh does the same.
  - An unreadable record is still reported `undetermined` and carried. The run reports `raisedOnly` (also in `changed`).
  - The test world now refuses any read or write that names the missing column, as Dataverse does.
- **K5 (`f69b007da8`). App-only generic writers refuse a document's two columns.**
  - Writers: `UpdateRecordActionCore` when it impersonates nobody (the Update Record node; the action seam without a
    user), and `DataverseUpdateHandler` (a playbook's output mapping).
  - They refuse a payload naming `sprk_accesspermission` or `sprk_accessinheritance` on `sprk_document`
    (`sdap.access.document_access_server_only`, nothing written).
  - Unchanged:
    - the same columns on a to-do or event;
    - a user's own write: an impersonated update, the user-OBO chat tools, the field-mapping push (written as the caller
      since task 166), and the form.
  - The chat create tool already blocks documents. No other app-only payload writer reaches a document: the create node
    executors write notifications and tasks only.
- **F1 (`7c7b245b10`). Every parented document gets its own-narrowing record.** First prepared and held back, because
  its churn feeds #2161's re-file pass (below). The main session then decided to commit it and to handle #2161 by deploy
  order and a gate.
  - The change: `writeRecord = record is null || !next.SameAs(record)` in `DecideDocument`.
  - Before: a parented document with no record, no own narrowing and a Standard inherited value got none. If its user
    set it Restricted in the window its Standard matter tightened to Restricted, the run read the edit as inherited
    (`{own none, floor Restricted}`). The document then followed the matter back to Standard.
  - After: the record (last inherited Standard) exists before the edit. So the edit is stricter than it, is the user's
    own, and survives the widening. Each row without a record is written once, then not again while nothing changes.
    K1 narrows to legacy rows the sweep has not reached yet.
  - Tests:
    - the main session's sequence (must fire);
    - the create refresh gives a Standard document under a Standard matter its record;
    - the one "Standard majority gets no record" assertion is updated. It was the only one.

    3 red on the old line, all pass after.
  - **What reacts to a document's `modifiedon` change:**
    - **Re-index or re-profile: nothing, on master.**
      - RAG indexing (`BulkRagIndexingJobHandler`, `ScheduledRagIndexingService`, which is off by default) picks
        `sprk_ragindexedon eq null` only.
      - Profile and index triggers are event-driven (upload, create).
      - `RecordSyncJob` and `DataverseIndexSyncService` do not include `sprk_document`.
      - No plugin step, flow or AI Search indexer over Dataverse exists in source.
    - **Audit:** both columns are audited and the entity's audit is on. Each parented document gets one entry, once, when
      its record is first written. After that an entry is written only on a real change (a re-file, a parent's level
      changing, a user's edit).
    - **The reconciler: about N/500 runs.**
      - Record writes share `MaxAccessPermissionWritesPerRun` (default 500) with value writes, and the sweep reads
        `MaxAccessPermissionRowsPerRun` (default 1000) rows a run across the four tables.
      - While the backfill fills the cap, value fixes (a matter that turned Restricted) are deferred. The run keeps its
        window and the next run takes them, so their convergence slows by the number of capped runs, never more.
    - **#2161** (open, head `2662545da2`), the re-file pass `RelocateRefiledDocumentsAsync`. It lists documents with
      `modifiedon` at or after its watermark; its remarks name "any write to the row" as the way back into its window.
      Per run it examines at most 500 (60-second slice), deriving each document's containers.
      - A document whose drive is allowed counts `inPlace`: reads only.
      - A document whose drive is NOT allowed is moved by `RelocateIfMisplacedAsync(..., Refile)` (at most 25 relocator
        calls a run).
      - An undecidable derivation fails the run, is carried, and is dropped after 5 runs.

      So the backfill churn, seen by a live #2161, would move every already-misplaced file. That is handled by the gate
      below.
  - **N on dev (spaarkedev1, read-only aggregate counts, 2026-10-21):**
    - `sprk_document`: 596 rows, of which **279 parented** (any of the 14 filing lookups set), **0 with a record**. So
      the backfill writes 279 records.
    - 0 parentless documents hold Limited or Restricted, so no parentless record is written.
    - The sweep's four tables: 319 communications, 596 documents, 83 events and 66 to-dos, 1,064 rows in all.
    - **Cadence:** the job runs every 2 minutes (`*/2 * * * *`). With the default caps the sweep needs 2 runs (1,000 + 64
      rows). Its 279 record writes fit one run's cap of 500, so the backfill on dev completes in **2 runs, about 4
      minutes** after the deploy. A larger environment takes about max(rows / 1000, documents / 500) runs.
  - **Deploy-order gate (main session, 2026-10-21):**
    1. #2169 deploys before #2161.
    2. #2161 deploys only after task 189's record backfill is complete, and then at least 70 minutes after the END of
       the run that completed it: 60 for #2161's first-run look-back (`InitialRecentChangesLookback`) plus a 10-minute
       margin (security pass 2, F4).
    3. Separately, #2161's pre-deploy gate includes a read-only count of misplaced files (the main session adds it).
  - **How to verify "backfill complete".** The field is `ResultJson.accessPermission.sweep.complete` of the
    `secure-child-reconciliation` job. Read it from `GET /api/admin/jobs/secure-child-reconciliation/status` (the last 10
    runs), or from the log line `[CHILD-ACCESS-PERMISSION] run=… sweepComplete=True`. The backfill is complete when it is
    `true` in a run on the instance carrying #2169, and that run also has:
    - `accessPermission.mode` = `"write"`;
    - `accessPermission.failure` = null and `writeCapReached` = false (the sweep advances only in such a run);
    - `failed` = 0 and `carriedRows` = 0. The sweep advances past a row it could not decide or write (`undetermined`,
      `failed`, `changedConcurrently`) and carries it; each run retries it, and it gets its record, and a new
      `modifiedon`, when it succeeds. A nonzero `carriedRows` that stays is looked at by id (the run's `problems`)
      before the gate passes;
    - `tablesWithoutColumn` not listing `sprk_document`. Otherwise documents are decided raise-only and no record is
      written.

    `sweep.complete` stays `true` in the later runs of that instance. **Time the wait from the END of the first run
    meeting all of the above** (security pass 2, F4): that run can itself write up to 500 rows, each one a new
    `modifiedon`. Take its `completedOn` in the status response, or its last `[CHILD-ACCESS-PERMISSION] set:` log line,
    then wait **60 minutes (#2161's look-back) plus a 10-minute margin**, and only then deploy #2161.
  - **Why not "a run with zero record writes".**
    - There is no count of record-only writes: `accessPermission.recordsWritten` counts every record write, with or
      without a value change.
    - A zero in one run proves nothing. The sweep walks the tables in order (communication, document, event, to-do), so
      a run whose window holds only communications writes no record while every document is still to come.
    - Record writes continue in the steady state (a re-file, a parent's level changing, a new document).

    `sweep.complete` is the signal that every row was reached in runs that wrote and were not capped. It is per instance:
    after a restart the sweep runs again, and a document that already has its record is not written again.
- **Accepted known limits (main session; in the PR body).**
  - K3: a Limited edit with two parent moves in one window is misread. The partner predicate is unaffected, because it
    reads Restricted only.
  - K4: the race between the re-read and the write can write a stale value. It corrects itself on the next run.
- **Mutants** (a scratch runner, since deleted; filter: the reconcile, stamp, cascade, root-inheritance and app-only writer
  classes). All killed.

  | # | Perturbation | Red |
  |---|---|---|
  | M14 | no raise when the own narrowing is unknown | 5 |
  | M15 | raise-only writes the floor even at or above it (lowers) | 4 |
  | M16 | the re-read asks for the missing record column | 1 |
  | M17 | the reconciler skips a table without the record column again | 1 |
  | M18 | the refresh writes nothing on an unreadable record | 1 |
  | M19 | `RefuseAppOnlyWrite` never refuses | 5 |
  | M20 | the playbook update core does not ask | 3 |
  | M21 | the output-mapping handler does not ask | 2 |
  | M22 | a user's own (impersonated) update is refused too | 1 |
  | M23 | the columns are refused on every table | 5 |
  | G7 | the guard's old initializer pattern | the control |
- **Gates (round C, on `f69b007da8`).**
  - Build: 0 warnings, 0 errors.
  - Filtered suite (the round B classes plus the K5 class): **347, all pass**. With the writer suites
    (`RefilePathRestampTests`, `UpdateRecord*`, `DataverseUpdateHandler*`, `OutputOrchestrator*`, `ActionSeam*`):
    **479, all pass**.
  - Fails before: the K1+K2 tests against the round B production files, 8 red. K5 and F3 by mutants (M19 to M21, G7),
    because the tests name the new reason code.
  - Wide run (no other load): **10066 passed, 0 failed, 9 skipped**.
  - ArchTests: **1211 / 1211**.
  - Publish size, fresh detached worktrees: master `44063b3473` 39,314,306 B; branch `f69b007da8` 39,322,093 B; delta
    **+7,787 B**. The worktrees were removed afterwards.
  - CVE: none.
  - Merge: `origin/master` `44063b3473` and #2161 (`2662545da2`) merge cleanly. #2170 (`c3da0a5d0e`) has one hunk
    on I-16, as before. #2150 (`0ba86217e2`) has one hunk in `SecureChildShareWorld.Answer`; keep both sides. Its
    `UpdateRecordActionCore` and `DataverseUpdateHandler` merge cleanly.
- **Gates after F1 (on `7c7b245b10`).**
  - Build: 0 warnings, 0 errors.
  - Filtered suite: **349, all pass**. With the writer suites: **481, all pass**.
  - ArchTests: **1211 / 1211**.
  - Wide run (the 20 namespaces): **10068 passed, 0 failed, 9 skipped** (10066 before F1, plus its 2 new tests; the skips already existed).

### 8.10 Security pass 2 (main session, 2026-10-21): FIX-ROUND-NEEDED, no widening path found

- **F1 (`48869bc7c5`). An operator-facing doc was wrong.** The guide said a document "keeps its stricter value until the
  script has run" when the BFF lands before the schema. It keeps it for good:
  1. while the column is missing, the raise-only path lifts the document to its parents' value;
  2. it never lowers it, so when the parents widen it stays;
  3. on the first run after the script, the value is stricter than its parents and there is no record, so the backfill
     records it as the document's own and never widens it.

  Fixed in `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.4 and its change log. The guide now says plainly that the schema goes
  first, how to find such documents, and that the operator widens them by hand:
  - the window's `set:` lines that say `own unknown`, and the refresh's missing-column warnings;
  - then `ownKept`, with `own` above `inherited`, in the first runs after the script;
  - an edit back to the inherited value removes the own narrowing (D3).

  The same stale claims are corrected in:
  - this file's §0, §2.2 (including the pre-F1 "the Standard majority never gets a write") and §2.8, and §8.7 item 3;
  - the guard's reasons for `RunAsync` and `DecideDocumentAsync`;
  - the schema script's notes (`-SelfTest`: 17 PASS);
  - `docs/data-model/child-access-permission.md`.
- **F4. The #2161 gate is timed from the END of the run that completes the sweep** (its `completedOn`, or its last
  `set:` line), with 60 minutes plus a 10-minute margin, because that run can itself write up to 500 rows. See §8.9.
- **K1 → fixed in scope (`c8c591777f`; the owner's D7 rule is "a child narrows, never widens").** A field-mapping push
  could map a parent's `sprk_accesspermission` onto `sprk_document.sprk_accesspermission`, or write
  `sprk_accessinheritance`. One push from a Standard matter then cleared the own narrowing of up to 500 child documents.
  Before task 189, the reconciler reset such writes.
  - `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` (it owns both the target table and the rules) asks
    `InheritedAccessPermission.RefuseAppOnlyWrite` for each rule's target: the same table test and normalised column
    list, with no second list.
  - A rule naming either column on a document is reported `Skipped` with the reason, and the rest of the push applies. A
    child whose only rule was skipped counts as skipped.
  - To-dos and events are unchanged. `RefuseAppOnlyWrite`'s remarks and the data-model doc name the push.
  - This **touches field-mapping code**, which the field-mapping project owns. The main session tells that session.
  - Tests: `DocumentAccessPermissionPushTargetTests`, 3 methods and 6 cases. **4 red on the old code**, all pass after.
    - Must fire: both columns, and a padded mixed-case name, are skipped while the document's other column is pushed. A
      push of only the column writes nothing.
    - Must not fire: the column on a to-do and on an event is pushed.

  | # | Mutant | Red |
  |---|---|---|
  | P1 | skip the column on every table | 2 (to-do, event) |
  | P2 | skip the whole child, not the rule | 3 |
  | P3 | match the exact literal only (no normalisation, no record column) | 2 |
  | P4 | no skipped result reported | 4 |

  "No skip at all" is the old code, so it is covered by the 4-red run (as a mutant it did not compile: CS8519).
- **K3 → filed as #2183** (not fixed here): an unreadable record row is carried forever. It blocks `carriedRows = 0`, and
  more than 1000 such rows freeze the watermark. The suggested fix is to report such rows without carrying them. The
  behaviour fails closed, and no real data does this today.
- **Gates** (on `48869bc7c5`; the later commits change only the notes):
  - Build: 0 warnings, 0 errors (BFF, unit tests, ArchTests).
  - The filtered 189 suite: **355, all pass** (349 + 6).
  - With the FieldMapping test classes: **491, all pass**. With the writer and push suites too (`RefilePathRestampTests`,
    `UpdateRecord*`, `DataverseUpdateHandler*`, `OutputOrchestrator*`, `ActionSeam*`,
    `AssignedAccessWriterTriggerTests`, `RefileWriterBindShapeTests`): **631, all pass**.
  - ArchTests (full project): **1211 / 1211**.
  - `git merge-tree` against `origin/master` (`44063b3473`): clean. #2161 (`2662545da2`): clean. #2170 and #2150: the
    known one-hunk conflicts only. Of the open PRs, only #2161 and #2150 touch `FieldMappingEndpoints.cs`, and both merge
    cleanly there.
  - Publish size was not measured again: no type was added. The change is one check in `FieldMappingEndpoints.cs`.
