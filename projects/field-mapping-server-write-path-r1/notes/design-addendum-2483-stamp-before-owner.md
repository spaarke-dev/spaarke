## 15. The owner decision sees the derived stamp's root (addendum 2026-11-02, #2483 / DI-009; DRAFT for UAC-r2 review)

Binding, not re-opened: notes/decisions.md 2026-11-02 item 3; DI-009; #2483 and its two comments (UAC-r2 facts 2026-10-30; owner decision 2026-11-02). Line numbers are at `4fdb0a5c99` (master merged).

### 15.1 The defect

- **Where.** `RecordCreationService.DecideAsync` decides the owner at step 7 (`RecordCreationService.cs:730-747`) over the item: `OwnedChildWrite.LookupsOf(item)`, the project pair parent and a work assignment's intermediates (`ResolveOwnerOverAsync`, `:1030-1067`, through `OwnerQuestionOf` → `RecordOwnershipContext.ForRow`, `:1074-1082`).
- Step 10 (`:771-777`, `StampBeforeCreateAsync` `:806-911`) derives the I-1 stamp fill-only (`CoreAncestorRestamper.PlanMissingStampAsync`, `CoreAncestorRestamper.cs:525-569`, app-only) and binds it into `fields` only. The item, which steps 7 and 12 read, does not change (`:802-805`).
- **Why secure-if-any misses the root.** The resolver looks through only parents that are NOT team-owned: the primary look-through (`RecordOwnershipResolver.cs:3715-3718`) and the lineage walk in `SecureDecisionAsync` (`:3836-3843`). A team-owned intermediate is taken at its own business unit (`:3796-3798`).
- **Scenario.** Matter MAT-1 is made secure. It is now owned by the Secure Record Owners team in the Secure Record BU, with `sprk_issecure = true`. Invoice INV-1 (`sprk_matter = MAT-1`) is still owned by the Litigation BU's default team, because the Make Secure pass has not reached it yet or ended incomplete.
  1. A user who can AppendTo INV-1 posts `POST /api/v1/child-records/sprk_todo` with `{ sprk_regardinginvoice: INV-1 }` and no stamp.
  2. Step 5 asks AppendTo on INV-1: allowed.
  3. Step 7 resolves the owner over [INV-1]. INV-1 is team-owned, so nothing above it is read, and the answer is the **Litigation** team.
  4. Step 10 derives `sprk_regardingmatter = MAT-1` into the body.
  5. The to-do is created in Litigation. The Litigation team can read it, and also INV-1.
  6. Within about 2–4 minutes, `SecureChildReconciliationJob`'s recent-changes pass (`*/2 * * * *`, `SecureChildReconciliationJob.cs:131`; writes on unless the emergency stop is set, `:140-143`) asks the written row's question. That question includes the stamp (`RecordOwnershipContext.ForExistingRowAsync` → `OrderedParentsOf`, `RecordOwnershipResolver.cs:437-441`), so the reconciler re-owns the to-do to the Secure team.
- **Reach (UAC-r2, #2483).** Affected: the route and the chat Owned create for to-do, event, memo and analysis, a filed communication, and a decision's "Add To Do". Not affected: `email.draft` (§15.5), documents (#2169, no pre-create stamp, `:810-811`), work assignments (the #2475 intermediates) and the budget inquiry.
- The job's own remarks say "in-product creates no longer open that window" (`SecureChildReconciliationJob.cs:56-57`). DI-009 is an in-product create that does.

### 15.2 Change 1, stated precisely (accepted by the owner 2026-11-02)

Today's create does not do this. The fix introduces it at create. The reconciler's question already gives the same answer.

- The derived stamp joins step 7's question under its real column (`sprk_regarding{matter|project|workassignment|servicerequest}`). It is therefore an ordinary member of the table's primary order (`RecordOwnershipContext.InPrimaryOrder`, `RecordOwnershipResolver.cs:515-572`), not a Secure-only parent.
- On a row with no subject (no pair on a regarding-builder host, `:562`; any row of event, memo, analysis or communication), the lineage order decides the primary. Where that order ranks the root column above the intermediate's column, an **ordinary** row's owner moves from the intermediate's BU default team to the root's.
- Under today's orders, that happens only for a row filed under a **report card**. `sprk_regardingreportcard` ranks after matter and project on to-do, event and communication, and memo's `sprk_reportcard` ranks last (`SecureChildLineage.cs:124`, `:174-175`, `:213-215`, `:246-247`). Every other intermediate ranks above the roots, and the work-assignment root ranks last. Signal and Decision Record keep the core, then the subject (`RecordOwnershipResolver.cs:548-559`). A to-do with its pair keeps its subject first.
- This is the reconciler's answer for the written row. The reconciler moves an ordinary row only when `OrdinaryMovesWritesEnabled` is on (`SecureChildReconciliationJob.cs:103-112`).
- **Owner rationale:** in the target model every record is owned by the customer's main BU or by Secure Record, so the move changes nothing there.

### 15.3 The fix: new step order

| # | Step | Change |
|---|---|---|
| 1–5½ | principal … pre-write rules | none (step 5's AppendTo set is unchanged; the derived root is never asked as the caller) |
| 6 | secure-root plan | none (stamped tables never `Inherits`, so `isolatedTeam` is always null for them) |
| **6½** | **I-1 derivation** (moved from 10) | `PlanMissingStampAsync` over the item, app-only, exactly as step 10 runs it today (`:818-850`). Refusals: 400 `stamp_underivable` and 503 `stamp_unavailable`, the same codes, words and trace step (`StampAndInheritance`, begun here). A document is skipped, as today. Runs for every principal. |
| 7 | owner | `DecideOwnerAsync` takes the derived columns. `ResolveOwnerOverAsync` asks over `LookupsOf(item)` ∪ {derived column → `RecordOwnershipParent(root)`}, through the same `OwnerQuestionOf` / `ForRow`. Fill-only: a stamp the item already holds is never derived, so there are no duplicate keys. |
| 8 | search index | none in code; it reads the FINAL owning team, which may now be Secure (§15.5) |
| 9 | creator / defaults | none |
| 10 | **bind + I-16** | binds the 6½ result as the caller (`BindStampAsync`, `:859-885`; 500 `no_decision` / 503 unchanged, still here) and sets I-16 from the same row (`:889-908`). No second derivation. A system principal still binds after the create (DI-007). |
| 11–12 | create, post-create | none |

- **Shape:** split `StampBeforeCreateAsync` into a plan half (6½) and a bind/I-16 half (10). The derived dictionary is passed through `DecideAsync`'s locals. No new type, enum member, interface, registration or package.
- **Repair** (`RecordCreationService.Repair.cs:119-133`) has the same order: the owner is decided at `:121` and the stamp is added at `:133`. 042b plans the missing stamp once, before `RepairOwnerAsync`, adds it to `keyed` (`RecordCreationService.cs:1187`) and hands the same result to `RepairStampAsync`. If planning fails, the owner is kept (Skipped, `record_repair.stamp_unavailable`) and never re-owned over the lookups alone (Q2).

### 15.4 Refusal parity (NFR-07)

A refusal raised by a single condition keeps its code, status and words. Only the ORDER changes when two conditions fail at once, plus the new refusals (rows 5–7). The maps are unchanged: `RecordCreationProblemMap.cs:33-41` (400 Invalid / 409 OwnerUnresolved / 500 Undecided / 503 Unavailable).

| # | Conditions | Today (step 7 first) | After 042b (6½ first) | Reach |
|---|---|---|---|---|
| 1 | Root underivable (two roots, or gone) **and** step 7 refuses | 409 owner code | 400 `stamp_underivable` | a caller already passed step 5 AppendTo on the intermediate, so this needs a compound data fault |
| 2 | Root read faults **and** step 7 refuses | 409 | 503 `stamp_unavailable` | compound |
| 3 | Root read faults **and** the owner decision faults | 503 `owner_unavailable` | 503 `stamp_unavailable` | the same status; the code differs |
| 4 | System principal, intermediate missing (no step 5) | 409 `ParentUnresolved` | 400 `stamp_underivable` | no system creator exists (DI-007, owner task 033) |
| 5 | **New:** the root is flagged secure but not isolated | 201, owned ordinary | 409 `SecureParentNotIsolated` (`RecordOwnershipResolver.cs:3876-3889`) | UAC change 3, fail closed (Q1) |
| 6 | **New:** the root cannot be read at step 7 after 6½ read it (a race) | 201 | 409 `ParentUnresolved` | a race only |
| 7 | **New (change 1):** a report-card child whose root's BU has no default team | 201 (report card's team) | 409 | rare; the reconciler asks the same question (its outcome is to be confirmed by the parity test) |

- Unchanged:
  - the bind refusals (they stay at step 10);
  - the step-6 refusals;
  - `ownership_exception` (an unfiled row has no source, so nothing is derived);
  - the secure-filing refusals in `OwnerDecided` (`:1106-1121`): every stamped table is a reparentable child, so a Secure owner is allowed.
- **Edges:**
  - route: `ChildRecordEndpoints.RefusalResult`;
  - chat: the tool codes (`ChatCreatedChildStampTests.cs:310-345`);
  - `email.draft`: `EmailDraftToolHandler.cs:1296-1306`;
  - Office quick-create: roots only, so it never reaches 6½.
- **No new leak (UAC):** every row above follows step 5's AppendTo on the intermediate. A 400 says no more than step 10's 400 says today.

### 15.5 Interactions

- **System principal (#2479, DI-007):**
  - it already derives at step 10 (`:859-862`) and binds after the create;
  - after 042b it derives at 6½ and step 7 uses the result, so the owner is right before the stamp is bound;
  - DI-007 itself is unchanged.
- **`email.draft` (a′):**
  - the handler checks AppendTo as the caller on the regarding, derives `DeriveForHostAsync` (`EmailDraftToolHandler.cs:360-408`) and keeps the stamp in the item (`:446-448`);
  - so step 5 asks AppendTo on it, step 7 already sees it, and 6½ finds nothing missing;
  - the owner and the checks do not change. Only 6½'s root read moves ahead of step 7, as row 1–3 ordering.
- **Office:**
  - the Office to-do is its own writer;
  - it stamps first and then asks `ForChild(todo)` with the stamps (`OfficeService.cs:3030-3060`, `:3105-3119`), which is the shape 042b copies;
  - Office quick-create uses the service only for a matter or project (`:2431`), which are not stamped;
  - Office codes are unchanged (NFR-07).
- **Repair:** §15.3. It has no production caller yet (052, DI-008).
- **Reconciler:**
  - **After the fix it still has work:**
    - the intermediate itself (INV-1 stays ordinary until the pass reaches it);
    - rows that existed before the pass;
    - writes made outside the product;
    - documents.
  - **Agreement by construction:**
    - the create asks `ForRow(table, item lookups ∪ derived stamp, pair)`;
    - the reconciler asks `ForExistingRowAsync(written row)`;
    - the written row's ownership parents are exactly the item lookups plus the bound stamp. The creator-person and index lookups are not ownership parents.
    - 042b pins this with a parity test.
  - **Known difference beyond the stamp (to be confirmed by that test):** the reconciler and Repair add a message's record-thread filing (`SecureChildReconciler.cs:3040-3051`, `RecordCreationService.cs:1183-1187`), and the create does not (Q3).
- **Step 8 index:**
  - `sprk_event` is search-indexed (`RecordTableRules.cs:256-259`);
  - an event made Secure at create gets an empty index plus `search_index_unset_for_owner_bu`, as FR-38 says for the Secure Record BU (§13.3);
  - today it gets the ordinary BU's index, and the reconciler's re-own leaves the index in place;
  - #2502 retires per-BU routing later.

### 15.6 Test plan (ADR-038; KEEP paths; the characterization lands first, alone)

**042a: characterization (tests only, no `src/` change). It pins today's wrong owner.**

- **A1:** `RouteCreatePreCreateStampTests.cs` (the route harness, the real service, a `StampWorld` and `.WithSecureRoot("sprk_matter", SecureMatter)`). Name: `RouteCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheEventsOrdinaryTeam_DI009Window`.
  - Setup: the Event is team-owned by `ChildTeam` in `ChildBu` and filed under `SecureMatter`. The stamp world derives `sprk_regardingmatter = SecureMatter`.
  - Pins:
    - 201;
    - `ownerid@odata.bind = /teams(ChildTeam)`;
    - `sprk_RegardingMatter@odata.bind = /sprk_matters(SecureMatter)`;
    - no sharee-mirror row on the new event.
  - The comment names #2483 and says that 042b inverts this test.
- **A2 (same file): the reconciler disagrees.**
  - Build the written row from A1's create body and ask the same resolver `ForExistingRowAsync`.
  - Pins: `SecureNamedTeam`. This is the window's disagreement in one assertion.
- **A3:** `RecordCreationServicePipelineSeamTests.PreCreateStamp.cs`. The same world as the system principal. Pins:
  - an ordinary owner;
  - no stamp in the body;
  - the stamp is bound by the post-create re-derivation (as `:178`).
- **Ordinary control (existing):** `ChatCreatedChildStampTests.cs:106` (an event under an event under an ORDINARY matter, owned by `ChildTeam`).

**042b: the fix. A1–A3 are inverted in the same PR; nothing is deleted.**

- **Must fire:**
  - A1/A3: owned by `SecureNamedTeam`; the sharee mirror is written at create (as `ChatCreatedChildStampTests.cs:130`); the event's index is empty and the warning is logged.
  - A chat twin in `ChatCreatedChildStampTests`.
  - A2 now agrees.
  - **Parity theory** (`WriterBuilderOwnershipParityTests` style): for every table in `RecordTableRules` that `IsStampedChildEntity` (to-do, event, memo, analysis, communication, Signal, Decision Record), with a secure world and an ordinary one, the create's owner equals `ResolveOwnerAsync(ForExistingRowAsync(written row))`.
  - **Change 1:** a to-do with no pair under a team-owned report card in BU X, whose matter is in BU Y, is owned by Y's team.
  - **Change 3:** a matter flagged secure but not isolated gives 409 `SecureParentNotIsolated` and no create.
  - **Order:** a root read fault combined with an owner world that refuses gives 503 `stamp_unavailable` (pins row 2).
  - **Repair:** a user-owned to-do missing its stamp under a team-owned invoice of a Secure matter is re-owned to Secure in the one update. If planning faults, the owner is kept (`stamp_unavailable`).
- **Must not fire:**
  - `ChatCreatedChildStampTests.cs:106` stays green. Its comment at `:108-110` ("step 10 adds it to the create BODY only, so the owner question is unchanged") is rewritten.
  - The same report card with the to-do's pair set is owned by X.
  - Step 5's AppendTo probes are identical. The derived root is never asked as the caller.
  - `EmailDraftRecordCreationTests`: the owner and the AppendTo calls are unchanged.
  - A browser-sent stamp: nothing is derived and the question is unchanged.
  - A document (seam `:229`) and a work assignment (seam `:64`) derive nothing.
  - Single-condition refusals are unchanged: `RouteCreatePreCreateStampTests.cs:64,81`, seam `:84-148`, `ChildRecordCreateRefusalContractTests`, `CrossSurfaceOwnerTests.cs:78-144`, and the Office suites (`OfficeRecordOwnershipTests`, `SecureRootCreateRouteTests.Office*`).

### 15.7 ADR checks, placement (§10) and component justification (§11)

| ADR | Point | Resolution |
|---|---|---|
| ADR-008 path A | Caller checks as the user, create app-only | Comply. Step 5's set is unchanged. The derivation stays app-only, as `PlanMissingStampAsync` already is (`CoreAncestorRestamper.cs:41`), and its answers are never shown before the caller's AppendTo on the intermediate. |
| ADR-010 | DI | Comply. There is no new registration; `_restamper` is already injected. |
| ADR-002 | No plugins; one server-side owner per invariant | Comply. It strengthens the rule: the create and the reconciler ask ONE question, built by `ForRow`, and a parity test proves it. |
| ADR-038 | Tests | Comply. The characterization lands first, alone (042a), then the inversion with the fix (042b). Tests are at KEEP paths (`data-mutation`, `seam`). No `Mock<HttpMessageHandler>`. |

- **Placement:** a modification inside `RecordCreationService` (and `.Repair.cs`) in the BFF. There is no new endpoint, service, package or background work. The publish-size delta is measured per §10 bullet 4 (expected ≈ 0).
- **§11:** no new surface. The two private halves of an existing method and one extra argument to a private method are a pure modification.
- **Hot path:** BFF Y, unchanged.

### 15.8 Risks

- **The creator may lose sight of their own row.** A caller who is not a sharee of the Secure matter creates a to-do they then cannot read. This is the intended outcome; it is the same as filing directly under a Secure matter today.
- **Cost:** none added. The derivation's root read moves ahead of step 7; it is not repeated.
- **More 409s during Make Secure** if the flag is set before the move (row 5, Q1).
- **The parity theory may expose other differences between the create and the reconciler** (the thread filing, Q3). Each one is fixed in 042b or filed. None is suppressed.
- **The stale comment at `ChatCreatedChildStampTests.cs:108-110`** would mislead a later reader if it is not rewritten. It is listed under 042b's must-not-fire tests.

### 15.9 Questions for UAC-r2

1. **Make Secure order.** In a normal Make Secure pass, is there an interval where the root is flagged `sprk_issecure` but not yet owned in the Secure Record BU? If so, 042b turns that interval's ordinary creates into 409 `SecureParentNotIsolated` (row 5). Is that acceptable, or should a root that is mid-Make-Secure be named the way `UnsecuringRoot` is for the reverse transition?
2. **Repair.** Do you accept the same reorder in `RepairAsync`, with a planning failure keeping the current owner (`stamp_unavailable`, no re-own), rather than re-owning over the row's lookups alone?
3. **Thread filing.** The reconciler and Repair add a message's record-thread filing to its question; the create does not. If 042b's parity theory confirms that a create can carry `sprk_communicationthread` today, should it be fixed in 042b or filed as its own issue?

### 15.10 Tasks (scope path: this design → UAC-r2 review → spec amendment → task files → TASK-INDEX → owner approval)

**042a: characterization of the #2483 window** (tests only; sonnet / high; STANDARD, plus code-review and adr-check because it touches `tests/**`)

- **AC1:** A1, A2 and A3 (§15.6) are added and pass on the current `src/`. `git diff --stat` shows no `src/` change.
- **AC2:** A1 pins all four facts: 201, the `ChildTeam` owner, the Secure matter stamp in the body, and no mirror row. A2 pins `SecureNamedTeam`.
- **AC3 (negative):** A1 fails if `ResolveOwnerOverAsync` is given the derived stamp. This is verified once by a local scratch edit that is never committed, and recorded in the notes.
- **AC4:** the existing ordinary control `ChatCreatedChildStampTests.cs:106` is unchanged and green.

**042b: derive the stamp before step 7** (depends on 042a merged and on UAC-r2's answers to Q1–Q3; sonnet / xhigh; FULL; reviewed by UAC-r2)

- **AC1:** the new order in §15.3. Step 10 does not derive a second time. The bind, I-16 and the system-principal post-create bind are unchanged.
- **AC2:** A1–A3 are inverted, and every must-fire test in §15.6 passes.
- **AC3 (negative):** every must-not-fire test in §15.6 passes. Specifically:
  - no new AppendTo probe;
  - `email.draft` is unchanged;
  - single-condition refusal codes, statuses and words are unchanged;
  - Office codes are unchanged.
- **AC4:** the parity theory passes for every stamped table registered in `RecordTableRules`. Any other difference it finds is fixed here or filed with a DI entry (Q3).
- **AC5:** the refusal-order rows 1–7 of §15.4 are recorded as K lines in the PR. Rows 2 and 5 are covered by tests.
- **AC6:** the publish-size delta is measured against a fresh master build. The UAC-r2 review line is in the PR. DI-009 is closed, and #2483 is closed with a link to the PR.
