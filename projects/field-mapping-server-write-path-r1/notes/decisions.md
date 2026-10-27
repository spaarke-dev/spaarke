# Decisions — field-mapping-server-write-path-r1

Full decision log. The project `CLAUDE.md` §3 holds the one-line binding outcome. Mark superseded entries with the
date; never delete them.

## 2026-10-19 — Owner decisions Q1–Q12 + design-to-spec clarifications

Recorded in [design.md §7](../design.md) and [spec.md "Owner Clarifications"](../spec.md). Evidence:
[design-review-2026-10-19.md](design-review-2026-10-19.md).

## 2026-10-19 — W2 coordination gate (task 010): UAC-r2 conditional no-objection; task 011 HELD

Full picture: [w2-coordination.md](w2-coordination.md). This resolves the spec Unresolved Question "UAC-r2 timing" as
follows.

- **UAC-r2 (session answer, 2026-10-19):** no objection to moving OwnedChildWrite's create half plus
  `CallerWriteAuthorization` into RecordCreationService, under these conditions:
  - **(a)** 011 is a byte-for-byte move; the same tests pass with unedited assertions; census must-fire controls are
    kept and baselines do not grow.
  - **(b)** The owner stays record-first and fail-closed. The Secure owner bind, I-1 stamp, isolation read and share
    mirror keep the same order and the same refusal codes.
  - **(c) BLOCKING:** a mapped **filing** lookup (`SecureChildLineage` / `RecordReparent.FilingParentOf`) that fails
    AppendTo must **refuse**, not drop with a warning. Only reference lookups may drop. Pin it with a must-fire test
    on a Secure parent. **This amends spec FR-08 and needs the owner's decision.**
  - **(d)** UAC adversarially reviews the 011 and 017 diffs before merge.

  UAC also adds that any change to a refusal code, an owner decision or the post-create step order pings UAC before
  merge. Nothing of UAC's is in flight on the W2 files. #2115 item 5 (missing MayRefile gate) may be absorbed by
  the registry (012).

  UAC states the go-ahead for moving an authorization boundary is this project's owner's to give.
- **ebilling-r1 (new overlap, missed at planning):** `work/ebilling-r1` (#1911, draft, CONFLICTING) puts an
  invoice-family guard inside `OwnedChildWrite.CreateAsync` and `RefileAsync` and changes push. Its other points:
  - ebilling prefers to **merge first** and have W2 rebase. It lands as one #1911 merge, with **no date** (~31
    tasks left, owner-gated schema runs).
  - The guard is carried unchanged: create half to the registry, refile half stays in RefileAsync, one copy.
  - Single invoice creator: invoices are created only via submission. `InvoiceSubmissionPersister` (deterministic
    ids, one transaction) stays out of the re-point unless the service accepts a fixed-id transactional batch.
- **ontology-r1:** #1501 is superseded (treat as not live). Nothing of ontology's is in flight on the 017 files.
  033 (SignalWriter / DecisionRecordWriter) needs an ontology-owner review and keeps a list of invariants. Leaving
  those writers out of the service is a valid outcome.
- **Sequencing:** rebase plus `/conflict-check` before every W2-W4 task; whoever merges second rebases.
- **Task 011 status: HELD**, pending two owner decisions:
  1. Accept UAC condition (c) as an FR-08 amendment.
  2. Order W2 relative to ebilling #1911: wait for #1911, or proceed on master with the second lander
     reconciling the guard.

  W1 (001–006) is unaffected and proceeds.

## 2026-10-20 — Owner decisions after the W2 gate: 011 UNBLOCKED

- **FR-08 amendment accepted** (UAC-r2 condition c). A mapped filing lookup that fails AppendTo refuses, and only
  reference lookups drop with a warning. Applied to spec FR-08 and its error table, to project CLAUDE.md §2, and to the
  task 014 acceptance criteria (must-fire on a Secure parent plus a must-not-fire control).
- **W2 proceeds on master** and does not wait for ebilling #1911. Whoever lands second reconciles the invoice-family
  guard: the create half goes to the registry (012), the refile half stays in `OwnedChildWrite.RefileAsync`, and
  there is one copy only. The UAC-r2 conditions (a, b, d) and the ping-before-merge rule stay binding.
- **Task 006 live delete approved.** Dev attribute maps for `sprk_ai_search_index` and its name companion are
  removed (evidence: task-006-notes.md).
- **Task 011 may start** (it is still `parallel-safe=false`, opus/xhigh).

## 2026-10-20 — UAC-r2 review of task 011: no objection (condition d satisfied for 011)

- Range c4a2a7b81..5120bf358 was reviewed independently by UAC-r2. Conditions (a) and (b) are met.
- UAC accepts the 4 qualifier-only assertion lines in BusinessOwnedAppendTo.cs as "unedited". The D-66 guard
  extension is verified to fire.
- Non-blocking notes:
  - **K2:** the no-shared-member-names guarantee is only a comment. Task 013 now must qualify the calls or add an
    ArchTest (acceptance criterion added).
  - **K4:** re-run /conflict-check on rebase. Master moved to 28274e04e.
- ebilling-r1 edits OwnedChildWrite.cs next to the moved `WhoAmIAsync`, so a visible merge conflict is expected for
  the second lander. ebilling calls none of the moved helpers.
- 017 still goes to UAC before merge.

## 2026-10-21 — UAC-r2 no-objection to 013 going to master

UAC-r2 re-checked `fad68a797b..36d885f72f`: F-1 to F-3 are closed, and F-4 stays the owner's decision (FR-06 step 10).
Their no-objection binds us to the items carried into later tasks:
- K-2 → 014;
- K-3 → 018;
- K-4 → 017;
- the narrower SystemPrincipal option;
- the document access columns, in whichever of #2169 and 017 lands second;
- no pre-create stamp writer is re-pointed until FR-06 step 10 is decided.

017's diff goes to UAC-r2 for the same review. ebilling-r1 has no objection, subject to its re-check of the fix-round-2
seam tests (re-checked 27bac15a42: no objection, 2026-10-21).

## 2026-10-21 — #2188 fix design: D, the caller-mask (agreed with UAC-r2)

- **Why not the literal fix:** reading the mapping source as the caller doesn't fit as written.
  `IDataverseUserClient` returns JSON, the engine needs a typed `Entity`, and there is no OBO SDK client.
- **Rejected alternatives:**
  - A: a JSON→Entity converter plus a change to the shared OBO client;
  - B: a new per-request OBO ServiceClient (ADR-028 work);
  - C: turning off Office quick-create mapping.
- **D:** keep the app-only typed read for types only. One caller GET through `IDataverseUserClient`, selecting only the
  mapped columns (lookups via `_col_value`). A column that is non-null for the app but null or absent for the caller
  is removed before the engine runs.
- **UAC-r2 conditions:**
  1. Fail closed for any type that can't be compared: file, image, partylist, absent, and so on.
  2. The race between the two reads is accepted as a known limit.
  3. A masked column gets the empty-column text exactly; a missing or denied source keeps the source-level warning;
     nothing distinguishes masked from empty.
  4. An ArchTest makes every engine call site with a source pass through `MappingSourceCallerMask`.
  5. Cancellation is rethrown; a timeout or fault is Unavailable, with no app-value fallback.
  6. Must-fire and must-not-fire tests, plus mutants.
- **Sequence:** the Office hotfix PR to master first. Task 014's mapping step reuses the helper after master is merged
  in.

## 2026-10-21 — UAC-r2 no-objection to 014 (fix rounds 1–3)

- **Re-check:** UAC-r2 re-checked `58d1a3548d..405a9b4f90`. F-A and F-B are closed, and the union definition of
  "filing" is kept.
- **Known limits:** R3-1 (normalisation) and R3-2 (the implicit mapping source read before step 6) are accepted. Both
  are carried to 017.
- **Sequence:** the step-3 mapping read stays app-only only until the shared `MappingSourceCallerMask` from the #2188
  Office hotfix lands. Order:
  1. the hotfix PR merges to master;
  2. master is merged into the work branch;
  3. 014's step 3 is routed through the mask (the hotfix's bypass guard forces it);
  4. then the PR to master.

## 2026-10-21 — UAC-r2 accepts task 015 on condition (b)

- Range `58d1a3548d..ccd4fff6b9`, no F items, conditions (a)-(d) hold.
- Confirmed:
  - no live caller (only the DI registration);
  - the worker gets the same owner with a null `RequestedBy`;
  - step 8's index BU is the BU of the team that step 7 or the plan chose, and the autonumber retry reuses it;
  - a catalog fault gives 503 with nothing written;
  - the guard has both controls and counts each allow-listed use.
- Before 017/018 make the unified create live (blocking criteria on both POMLs):
  - K1: a test for the step-7 catalog fault;
  - K3: normalise a lowercase `sprk_ai_search_index@odata.bind` explicit null, so no case-duplicate keys reach the payload.
- #2206 (filed by UAC-r2): a stamped `sprk_ai_search_index` is not re-derived when the owner changes. Pre-existing on master; 015's default inherits it.
- 017/018, where a project takes its pair-named matter's BU team, go to UAC-r2 for condition (b).

## 2026-10-21 — Owner: Repair field security, option 1 (UAC-r2 concurs)

- **Problem:** Repair (task 016) applies mapping as the system principal, with no caller to mask against (#2188 design D
  masks to the caller's view).
- **Decision:** option 1. Repair skips field-secured source columns using the app-only metadata reader from task 033.
  The #2188 Roslyn guard gets a per-occurrence allow-list entry for Repair's single `ApplyFillOnly` call.
- **UAC-r2 conditions:**
  1. fail closed on metadata, with a distinct reason;
  2. never write the document access or D7 link columns (done in 016 through `RefuseAppOnlyWrite`);
  3. a per-occurrence allow-list entry;
  4. tests;
  5. 052 stays blocked until 033 lands with the skip.
- **Rejected:** option 2 (matching security profiles); it adds a comparison and gains nothing. Option 3 (Repair never
  maps) is the fallback if 033 slips past 052's need date.
- **Safety until 033:** nothing calls Repair (`RecordRepairReachabilityTests`); 052 is hard-blocked on 033.

## 2026-10-21 — Task 018 split, and the Office fill-only and fixture rulings

The 018 implementer stopped before writing code (the budget, plus live changes beyond the brief). The main session decided:

1. **Office mapping becomes fill-only (option a).** The spec covers this: Q6 (fill-only, caller wins; FR-02), with Q10 putting Office in scope. NFR-07 holds Office's **error codes** fixed, not its mapping semantics.
   - Four Office contract assertions pin the legacy overwrite and its warnings: `Post_Matter_WhenMappingBlanksTheName_KeepsTheRequestedName`, the Project equivalent, `…WritesANonMatterTypeValue_KeepsTheRequestedType`, and `…AndNoTypeWasRequested_RemovesIt`. They are rewritten to the fill-only outcome. Each change is listed in the 018 notes.
   - Release note: a mapping rule no longer replaces a value the user entered in Office quick-create.
   - Option b (a legacy overwrite mode) is rejected: it would mean two mapping semantics, contrary to FR-02.
2. **The contract fixtures follow the new write.** The service writes one Web API dictionary through `IFieldMappingDataverseService.UpdateRecordFieldsAsync` and retries on 412. The Office contract tests may capture that write and read their assertions from it; a fixture helper may view the dictionary as typed values. What each test asserts (the values written, the codes, the responses) stays the same. Assertions about the mechanism (`CreateAsync` `Times.Once`, `SetupSequence` duplicate-key retries) are re-expressed against the new write, and each is listed in the notes.
3. **Further live changes go to UAC-r2 for condition (b)** before 018b:
   - the Assigned To contact named in a request now gets an AppendTo check;
   - a Matter create with no bearer token now gets 500 `caller_rights_unverifiable`;
   - the matter/project post-create order gains the I-1 re-stamp and I-16, and the membership event moves from `OfficeEndpoints.cs:1858` into the service.
4. **018 runs in three phases (three PRs), with one POML:**
   - **018a:** service prerequisites, no live change (K1, K3, the K-3 reference drop-with-warning path, the K-7 post-mapping default hook).
   - **018b:** the Matter/Project adapter, after item 3.
   - **018c:** delete the Office core and re-point or retire the nine test files.
   - The Invoice leg waits for ebilling-r1; it uses nothing from `OfficeRecordCreationService`.

## 2026-10-21 — The Office Invoice quick-create leg is not moved (ebilling-r1, owner decision L)

ebilling-r1 says the owner decided (decision L, model §4/§9) that invoices are created only by submission (FR-17, FR-20).
- ebilling task 096 deletes the Office Invoice quick-create leg (`QuickCreateRequest.cs:180`) and the Invoice save target (`OfficeDocumentPersistence.cs:47`). It ships one release after word-add-in-r1's add-in release, because cached add-in clients can still call those legs.
- Task 018 therefore moves only Matter and Project. It leaves the Invoice leg exactly as it is, apart from a marker comment, and adds no `RecordCreationService` path for `sprk_invoice` from Office.
- The 018 Invoice criterion is withdrawn.
- The #1911 reconcile still adds the invoice-family `PreWriteRule` for `sprk_invoice` / `sprk_billingevent`, for the submission path.

## 2026-10-21 — UAC-r2 condition (b) answers for 018b

1. **Assigned To contact: refuse** when the caller lacks AppendTo on it, checked as the caller. Naming the contact grants it an uncapped Collaborate (ADR-034 A4). Dropping it with a warning would silently under-grant (NFR-03). The refusal names `assignedTo` and never reads as "matter not found". It is tested with an unreadable contact and a readable one.
2. **No bearer token:** accept the 500 `caller_rights_unverifiable` refusal (fail closed).
3. **Post-create order: OK, on three conditions, each tested:**
   - (i) the membership event fires exactly once, and the endpoint publish is removed;
   - (ii) a Project under a secure matter is owned by the Secure Record Owners team and is itself secure, with Access Permission as a floor and the search index following the owner;
   - (iii) a table-driven refusal map keeps Office's status for every service code; OwnerUnresolved stays 403, and an unmapped new code fails the test.

These are in the 018 POML.

## 2026-10-22 — 018a: an optional reference whose check cannot complete is dropped, not refused

When a request names an optional reference lookup (`ReferenceLookupColumns`: the registry-listed matter type, practice area and project type) and the caller check for it cannot complete, the value is **dropped with `record_creation.reference_value_dropped`**, and the create proceeds without it. This deviates from NFR-01's wording ("every caller check refuses on error"). It is accepted because:
- the value is never written, and step 5 re-checks everything that remains;
- it matches live Office behaviour (`OfficeRecordCreationService.cs:1346-1353`, which creates without the reference and warns);
- it applies only to caller-owned optional references, never to a filing lookup, the for-person column, a protected column, or an access-conferring column (018a fix round F4-1: these are rejected by the registry allow-list).

Raised by the 018a adversarial review (K-5). Cite this in the 018 PR and in UAC-r2's condition (b) review.

## 2026-10-22 — 018b-1 inventory: answers to G1–G7 (main session)

The 018b-1 inventory (`notes/task-018-notes.md`, "018b-1") left seven items open. The answers:

- **G1, "No matter type was supplied":** keep the warning. The service adds `RecordCreationOutcome.WrittenColumns`, the column names of the step-11 item. The adapter warns when `sprk_mattertype` is not among them and no `reference_value_dropped` already names it.
  - §11: it overlaps with no existing outcome member (`Warnings` names only drops). Reading back costs an extra app-only read and a race. Without it, the four pinned contract tests and the user's prompt to set the type are lost.
- **G2, no token vs no Dataverse user:** the service sets the WhoAmI failure as `ClientFailure` on the `PrincipalUnresolved` refusal. It adds no type and changes no code.
  - Office maps `UserContextRequired`, `OboNotConfigured` and `OboExchangeFailed` to 500 `caller_rights_unverifiable`, and `AccessDenied` to 403 `owner_unresolved`. An absent or unknown failure fails closed to 500 `caller_rights_unverifiable`.
- **G3:** keep the endpoint-resolved systemuser id as the adapter's input precondition (403 `owner_unresolved`). It is an input the maker's-contact default needs, not an authorization check. The service's WhoAmI stays the identity check.
- **G4, `secure_source_filing_mismatch`:** with a secure source, only the derived pair is written; this holds by construction. A profile rule aimed at the pair is skipped with `mapping_rule_skipped`, and the project is filed under the derived pair, into isolation. One test pins this.
  - The 403 becomes a 201 in a misconfigured-profile case. Because this changes a refusal code on a live path, **it is sent to UAC-r2 under condition (b)**. The swap may build on it, but the 018b PR does not merge without UAC-r2's concurrence.
- **G5:** the service names the refusing lookup on a `NotFoundOrDenied` step-5 refusal (`RefusedColumn`), so Office can map the pair's record to `caller_cannot_file_under_parent` and the Assigned To contact to its own `assignedTo` code (pass 2). This is done with G1 and G2 in pass 018b-1s, before the swap. Never parse `Detail`.
- **G6, reference-drop wording:** sign off. NFR-07 pins error codes and response shape, not warning text. The assertions take 018a's K-3 text. Release note: Office warning texts follow the service.
- **G7, an unavailable mapping source:** sign off. FR-04 says a source the caller can't read gives no mapping and a warning, so Default rules no longer apply in that case. This happens only on a read fault, because `QuickCreateSourceAccessFilter` already refuses an unreadable source. Release note.

## 2026-10-22 — G4: UAC-r2 concurs with option (a), on three conditions (condition (b))

UAC-r2 accepts that with a secure source, only the derived pair is written, held by construction. The old 403 existed so a mapped pair could never file a secure-sourced record under a different parent. Fill-only mapping cannot write a pair, so that outcome is impossible. Isolation is unchanged: the Secure team owner and `sprk_issecure` through the step-6 plan, and the empty or unreadable flag is still refused with `record_owner_parent_undetermined`.

Conditions, each tested in 018b and checked in UAC-r2's condition (b) review:

1. **The derived pair also wins over the client.** Under a secure source, a regarding pair in the Office request body that differs from the derived pair is **refused with 403 `secure_source_filing_mismatch`**, and nothing is written. A client pair equal to the derived pair gives 201.
2. **The skipped rule is visible.** The pair rule's `mapping_rule_skipped` reaches the Office response warnings, naming the rule or its columns.
3. **Must-not-fire controls:**
   - a secure source with no pair rule gives 201, secure, and no warning;
   - a non-secure source with a pair rule keeps the non-secure behaviour (skipped with the same warning under fill-only), and the test states it.

There is also the base test: a secure source with a pair rule gives 201, under the derived pair, secure, with the warning. Cite this entry in the 018b PR body.

## 2026-10-22 — G4 condition 1 is satisfied by the contract's shape (no regarding member), and pinned (UAC-r2)

`QuickCreateRequest` has no regarding member, and System.Text.Json drops unknown properties. A client therefore cannot send a regarding pair to Office quick-create, and the adapter only ever writes the derived pair. The condition-1 403 (`secure_source_filing_mismatch` for a differing client pair) is unreachable. UAC-r2 agreed: add no fields just to refuse them, and add no 400 for unknown members.

It is pinned by three tests:
- a body carrying extra regarding properties under a secure source gets 201, under the derived pair, and is secure;
- the same body under a non-secure source gets 201, under the derived pair;
- a reflection test over `QuickCreateRequest`'s public properties and JSON names fails on any member containing "regarding" (case-insensitive), with the message "a regarding member reintroduces G4 condition 1: refuse a differing pair under a secure source with 403 secure_source_filing_mismatch".

## 2026-10-22 — 018b-2: undetermined-flag wording; #2124 on the Repair path

- **The undetermined secure flag keeps its code, and the guidance changes.** An Office Project from a matter whose `sprk_issecure` is empty, or cannot be read, is refused with 403 `record_owner_parent_undetermined`, and nothing is written. The refusal's detail is now the step-6 plan's: it names the cause (no flag value, or the read failed). It no longer adds the old core's "Try again" or "Ask an administrator" guidance.
  - Accepted under NFR-07, which pins codes, not text, and consistent with G6.
  - Release note: Office refusal texts follow the service.
- **#2124 is fixed on the create path and still open on Repair.**
  - Create-time mapping passes the table's Money attributes, so a Default rule into a Money column writes a `Money`.
  - Repair (SDK, system principal) passes none and would write a decimal. It is dormant: Repair has no live caller until 052, and 052 is blocked on 033, which carries the app-only metadata reader the fix needs.
  - The 052 POML now has this as an acceptance criterion, and the full analysis is a comment on #2124.

## 2026-10-22 — UAC-r2 #2253: create-time mapping asks the refusal as `WriteIdentity.AppOnly`

UAC-r2 PR #2253 (#2251) adds an explicit write identity to `InheritedAccessPermission.RefuseAppOnlyWrite`, with the values `AppOnly` and `Caller`.

**Create-time mapping takes `WriteIdentity.AppOnly`.** This covers `RecordCreationService.Mapping.cs`, `DropAppOnlyRefusedRules`, and applies even when the create's principal is a caller. UAC-r2 agreed.
- The mapped values are written by the service's single app-only create (step 11), so no Dataverse privilege or field security stands behind them.
- Create-time mapping therefore also drops the sets #2253 adds: access tables, secure-root columns, and access-deciding columns on contact, organisation, systemuser and business unit. That matches #2220's create-side refusal.
- The `sprk_document` + `sprk_accesspermission` create exemption stays.
- Whichever of #2253 and 018b merges second adds the parameter, plus a must-fire test: an access-deciding contact column is dropped at create.

## 2026-10-22 — Owner decisions for 017 (2a–2c); dev mapping config created

**2a. FR-06 step 10: option (a).** The service derives the filing stamp (I-1, plus I-16 for To Do, Event and
Communication) into the payload BEFORE the create and refuses when the derivation fails, then re-derives after the
create. Documents stay post-create (#2169). This unblocks re-pointing the five pre-create stamp writers (Office To Do,
Workspace To Do, `TaskActionCore`, `CommunicationService`, `DecisionRecordWriter`) onto the service.

**2b. #1608: option (a).** The service refuses a caller-supplied server-owned column (`sprk_containerid`,
`sprk_securitybu`, `sprk_accesspermission`, and the table's other protected columns) on every path, chat included.
The same set applies to projects and matters. Closes #1608 when 017 lands.

**2c. Matter/project number from chat: option (a).** A payload naming `sprk_matternumber` / `sprk_projectnumber` is
refused (403 `record_creation.blocked_column`), and the chat tool description stops offering the field in the same
change.
- Owner note: the Dataverse autonumber is a quick interim fix; a numbering-service component may replace it.
- That does not change the decision. The rule is "the number is owned server-side, never supplied by the caller",
  whoever generates it. If the numbering service later accepts a user-chosen number (for example a client-matter
  number), that becomes an explicit, validated input of the numbering service, not a free column in the create
  payload.

**2d. SystemPrincipal: still open.** Examples sent to the owner. Finding while preparing them: the refusal applies
only to a system create of a project or work assignment filed under a record (step 6), or of a table with pre-write
rules (step 5½; none on master yet). None of the FR-21 system creators creates a project or work assignment, so
today's refusal blocks no real path. Revised recommendation: keep refusing (option c) and decide the narrower option
when the first system creator of a project or work assignment is designed (§11: no caller, no cost of doing nothing).

**Dev mapping config (owner-approved 2026-10-22, live write on spaarkedev1).**
- New profile "Matter to Project (Attorney Matrix)" `69e55b30-92ce-f111-a05d-3833c5e9614d` (Matter → Project record
  types), with 8 Copy rules: attorney 1/2, paralegal 1/2, law firm 1/2, assigned-to internal/external.
- 7 Copy rules added to "Matter to Work Assignment (Attorney Matrix)" `7b147316-…`: everything except attorney 1,
  which already existed.
- Every rule: source type 1 (Lookup, local set), target type 100000001 (Lookup, global set), Parent to Child, sync
  mode One-time (as the Event rules; 005 `-Apply` migrates them with the rest), active, execution order 1–8.
- Lookup targets checked live: people → `contact`, law firms → `sprk_organization`, the same on matter, project
  and work assignment. Read back: 16 rules active.

**1a finding: the add-in cannot create a Project under a matter.** `SaveFlow.createRelatedRecord` posts only
name, description, type ids and assignee. It never sends `sourceEntityType` / `sourceRecordId`, and no other client
calls `/api/office/quickcreate` with a source. So the #2191 filed-under path (and its mapping) is reachable only
by a direct API call today; it is covered by `SecureRootCreateRouteTests.OfficeFiling` and the Office contract
tests. The owner smoke covers the plain Office Project only.

## 2026-10-23 — Owner decision 2d: SystemPrincipal stays refused (option c)

A system-principal create of a project or work assignment filed under a record keeps being refused (403
`record_creation.system_principal_unsupported`, step 6), as task 013 built it.
- Every person-initiated create (wizards, Office quick-create, chat, forms) is unaffected: it runs as the caller,
  whose access is checked.
- `RecordCreationPrincipal.ForSystem` has no caller on master, so today's refusal blocks no real path.
- Revisit when the first system creator of a project or work assignment is designed (for example email intake that
  opens a work assignment under a matter). Then choose between "ordinary parents only" (option a) and an app-only
  AppendTo check on `RequestedBy` (option b; needs ADR-028 approval and must fail closed on a null `RequestedBy`).
- 017 carries no SystemPrincipal change.

## 2026-10-27 — UAC-r2 recommendations on 4a, 4b/4c, 4d (pending the owner)

Asked at the owner's direction ("consult with uac-r2 directly"). Nothing applied yet. The 4a migration `-Apply` is on hold, at UAC's request.

- **4a — sync mode:** classify rules; do not make every Copy rule re-pushable.
  - Assigned-To registry targets stay One-time (0), including the 15 added on 2026-10-22. Push runs `AssignedAccessMaterializer.RunAfterWriteAsync` per child, so re-pushing an assignee is a mass access grant and revoke from one click.
  - Filing, lineage and ownership targets are never re-pushable: that would be a mass re-file.
  - Plain data columns are re-pushable (1).
  - `Set-FieldMappingRuleSyncMode.ps1` becomes class-aware and prints the classification in its dry run. The values ship with task 040, in the same PR and deploy, after the owner reviews the dry-run list.
- **4b/4c — rights on copies:** agrees with the owner for field restrictions and ordinary lookups, with three boundaries:
  1. A mapped FILING lookup keeps the caller's AppendTo check; the whole create is refused (403). AppendTo is record-level: who may attach into a record's family. Without it, a View Only user could add rows under a record, including a Secure one. The K-2 field-security refusal on filing lookups also stays. If the owner still wants filing to bypass AppendTo, it is a UAC security decision that needs the owner's explicit sign-off in UAC's session.
  2. Ordinary mapped lookups and values are copied regardless of the caller's AppendTo or target field security (the owner's policy). This reverses #1617's drop. Assigned* columns are safe: the materializer applies Secure, Restricted and Limited policy itself.
  3. Never copied, whatever the rights: BFF-owned invariant columns (ownerid, sprk_issecure, sprk_accessinheritance, sprk_createdbyperson, sprk_regarding* stamps, sprk_accesspermission, contact identity/binding). Refused when a rule is authored and skipped at copy time. Also never copied: a value from a field-secured SOURCE column into a non-field-secured target (declassification); it is copied only into a field-secured target, otherwise dropped with a warning.
  - Keep the FR-04 source Read check as the caller.
- **4d — search index:** Secure isolation does not use the index.
  - Retrieval is trimmed per caller by `IRetrievalAccessTrim` (fail closed), and Secure files are isolated by their SPE container.
  - Do not stamp the index from the owning BU at create. Route by kind (files → spaarke-files-index, records → spaarke-records-index) through the tenant default.
  - The original 4d precedence question goes away.
  - Data clean-up for the owner: the singular `spaarke-file-index` catalog row, and the BU and document stamps on knowledge-index-v2 / spaarke-file-index.

## 2026-10-27 — Owner decisions on 4b/4c and 4d; dev search-index cleanup done

- **4b/4c: accepted as UAC-r2 recommended.**
  - A mapped filing lookup keeps the caller's AppendTo check (refused, 403), and the K-2 field-security refusal stays.
  - Ordinary mapped lookups and values are copied regardless of the caller's AppendTo or target field security. This reverses the #1617 drop and the per-value field-security drop.
  - Never copied: BFF-owned invariant columns (refused when a rule is authored, skipped at copy time), and a field-secured SOURCE value into a non-field-secured target (dropped with a warning).
  - The FR-04 source Read check stays, as the caller.
  - Lands with 017, the create re-point, unless sequenced otherwise.
- **4d: accepted.**
  - Do not stamp `sprk_ai_search_index` from the owning BU at create. Route by kind through the tenant default (files → `spaarke-files-index`, records → `spaarke-records-index`). Secure isolation does not depend on the index.
  - The 015 step-8 BU stamp is removed in code; tracked for 017.
- **Dev cleanup (owner-approved 2026-10-27), done and verified:**
  - Two catalog rows point at Azure indexes that do not exist on `spaarke-search-dev`, and both rows were already Inactive:
    - `spaarke-file-index` (dd04e55f, "Development Files 2");
    - `spaarke-knowledge-index-v2` (c104e55f, "Development Files 1").
  - Set `sprk_ai_search_index` to null on every record that pointed at them: BUs "Spaarke" and "Spaarke Demo", 28 matters, 8 projects, 8 work assignments, 16 events, 4 invoices and 77 documents (143 rows). The re-query shows 0 left.
  - A null value falls back to the tenant default (`SearchIndexResolver` and `SearchIndexNameResolver`).
  - Left as is: "Spaarke Business Unit 1" points at the canonical `spaarke-files-index` row (fdcc183b, default), which is harmless. The catalog rows were not deleted; they stay Inactive.
  - Not verified: whether those 77 documents are present in `spaarke-files-index`, because there is no data-plane read on the search service. They may need re-indexing.

## 2026-10-27 — 4a: opt-in Assigned-To re-push on "Push Updates" (owner proposal; UAC-r2 design; pending owner OK of the design)

Owner proposal: an ordinary push leaves Assigned-To rules One-time. A checkbox in the Matter/Project "Push Updates" ribbon dialog, "Update all related 'Assigned To' contacts", also re-pushes them. UAC-r2 accepts this from the access side, with this shape:

1. `/push` gains `includeAssignedTo`, false when absent. Filing columns are never pushed; invariant columns are never copied.
2. **Preview first.** `/push` with `preview: true` runs the same enumeration and rights checks with no writes. It returns, per child whose Assigned value would change: name, column, from → to, and the Secure (suggestion only) or Restricted (no contact grant) flag. It also counts the children skipped because the caller lacks Write. The dialog shows the list and this line: "Replaces the assignee on these N records. Their automatic access moves to the new assignee; access staff set by hand is kept."
3. **Gate:** Write on the SOURCE record when `includeAssignedTo` is true, asked through the equalised gate probe. Per child, the caller's own impersonated Write applies, as today. There is no Share/Assign or role gate.
4. **Hand-changed assignees:** the person unticks them in the preview, and the confirmed push takes an explicit child-id list. The server re-checks each id: it must be a child of the source under the rule's relationship, and writable by the caller. Any other id is refused per id. A child already equal to the parent is not listed.

Unchanged: `SecureRootFilingGate` per child; `AssignedAccessMaterializer.RunAfterWriteAsync` after each child write.

Expected tests:
- preview writes nothing;
- absent flag → Assigned columns untouched;
- Read-only source + flag → 403;
- an unticked child keeps its value and grants;
- a foreign id is refused;
- a Secure child gets a suggestion, not a grant.

Placement: task 040 (push on the canonical engine). The dialog replaces `openConfirmDialog` in `sprk_fieldmapping_push.js` with a small custom dialog (MODAL-DECISION-CRITERIA). It is new surface on an existing endpoint, so the §11 three questions are answered at task-create time.

Sync-mode migration (UAC-r2 4a): class-aware. Assigned-To targets stay 0; filing targets are never re-pushable; plain data → 1. The values ship with 040 after the owner reviews the dry-run list.

## 2026-10-27 — New scope: record family service + TrackingFieldTrio "Family" tool (W7, in this project)

- **Owner decision:**
  - Build a shared, read-only "record family" service.
  - Add a Family tool to the TrackingFieldTrio PCF on the Matter, Project, Work Assignment and Event forms. (The WA main form already hosts the PCF: `sprk_WorkAssignment/FormXml/main/{7e578eef-…}.xml`.)
  - The tool opens a modal listing the family, with an action toolbar. The first actions are "Push field updates" and "Update Assigned To", using UAC's /push preview → confirmed id list. More actions later.
- **Procedure:**
  1. design.md addendum, including Placement Justification, §11, hot-path and ADR tensions;
  2. UAC-r2 review (canonical family definition and access) and ontology review (hierarchy and terminology);
  3. spec amendment (new FRs);
  4. `task-create` for the new tasks;
  5. TASK-INDEX.
- **Canonical family definition:** the owner says alignment with UAC-r2 is "very important". Asked on 2026-10-27 (definition, canonical registry and owner, read identity, hidden-member counts, limits).
  - Today there is no single family service: about 8 separate enumerations with their own registries (see the code-map summary in the UAC message).
- **5c:** see the task-006 notes (name maps deleted; the system map can't be deleted; handed to UAC).
- **77 documents:** no re-index (owner).
