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

## 2026-10-27 — UAC-r2: canonical record family (binding input for the W7 design; UAC reviews the addendum before task-create)

1. **Definition.** A family is a tree joined ONLY by FILING edges:
   - core → core: projects and WAs under a matter or project (`SecureRootInheritance.TypedFilingColumns` / `IntermediateFilingColumns` / record-type pair; depth 4);
   - core → child: the four regarding stamps (`sprk_regardingmatter` / `project` / `workassignment` / `servicerequest`) plus each child table's direct filing lookups (documents, communications and threads, events, to-dos, memos, invoices, analyses, agreements, budgets, report cards);
   - child → grandchild: the `SecureChildLineage` hops (file version → document, to-do → regarding document, communication attachment / participant → communication, budget bucket → budget, the invoice family → invoice; `MaxLineageDepth` 4).
   - Direction: down from the record, plus up to its root. Siblings come only through the shared parent.
   - NOT family: citations (signal / decision → policy); people and organization lookups (assigned*, attorneys, law firm, contacts); N:N associations; grant rows (`sprk_externalrecordaccess`); system rows.
   - Policy → version is included only if a policy can be filed under a core record.
   - A service request is a core record with its own family, NOT in the Secure family (task 194 adds its stamp to the ordinary walk only).
2. **Registry.**
   - ONE declared EDGE registry: (child table, lookup column, parent table, edge kind filing-core | stamp | lineage-hop, depth role, attributes such as InSecureFamily). It lives in `Services/Dataverse`, next to `CoreAncestorResolver`, as DATA.
   - The existing registries become checked views: a parity test with a ratchet baseline.
   - Seed it from `SecureChildLineage`, `TypedFilingColumns` / `IntermediateFilingColumns` / pair and `StampSourceColumns`. Do NOT start from scratch.
   - Seed only AFTER UAC task 194 merges (it changes `SecureChildLineage`: budget bucket, the ordinary service-request edge). UAC will send its head.
   - UAC owns the CONTENT. Any change needs a UAC review line, enforced by a source-scan / ArchTest note on the file.
   - Migrating UAC's walks onto the registry is UAC's call, not this project's.
3. **Read identity.**
   - The walk runs as the CALLER (OBO), level by level, batched per level (no N+1).
   - Never traverse through an unreadable record. Never count or hint at hidden members.
   - The root read probe uses the equalised gate call.
   - Actions re-derive the family as the caller at execution and intersect the confirmed ids; the client tree is never trusted.
   - No app-only walk plus trim.
4. **Limits.**
   - Depth 4. A size cap per view (500) and per level.
   - Past the cap: `truncated=true` plus per-table counts of what IS shown, and load more per level. Never truncate silently.
   - Actions take only the confirmed ids.
   - No cross-caller cache. Any per-caller cache lasts minutes at most and is evicted by the access-cache invalidators.
5. **Never:**
   - return an unreadable member, or its count, name or id;
   - be an authorization input (the family list is display only);
   - write (actions go through their own endpoints, which re-authorize per record);
   - expose access metadata (grants, No Access, sharees, the Secure owner team);
   - serve external users (the SPA uses `AccessibleRecordSetService`).

   Anything access-changing (bulk share/grant, Monitor if it shares) needs UAC review before it is built. "Email family members" takes recipients only from the people lookups on readable members, respects field security, and uses the existing communication send path.
- **5c follow-up:** UAC filed #2343. Recommendation: retire the `sprk_securitybu` mechanism (remove `HasLegacyPerProjectBusinessUnit` and its 409, then delete the column). Interim: hide the BU-form navigation. UAC takes the schema delete to the owner.

## 2026-10-30 — Owner: task 039 approved; chat create keeps task, contact and account

**Task 039 (FR-38, server-owned search index): approved as written.** It is sequenced after 017 and before the 019
W2 deploy, so the durable #2399 fix ships with W2. The procedure ran in full: design §13, then UAC-r2's review, the
spec amendment FR-38, the task, the TASK-INDEX entry and the owner's approval.

**Chat `dataverse.create_record` on the unified service: register `task`, `contact` and `account`** (option b).
- 017c found that, today, the chat create sends every user- or team-owned table through `OwnedChildWrite`'s create
  core, with a team owner from the resolver. The service creates only tables in `RecordTableRules`.
- The owner chose to register the three tables the tool offers, keeping today's resolver team owner. `task` is also
  pinned by an existing test.
- Every other unregistered table is refused, as 017's criterion states.
- Organization-owned and per-user tables, and unfiled communications, keep their run-as-user path
  (`OwnedChildWrite.PathFor`).
- Rejected:
  - refusing every unregistered table (a chat task, contact or account create would stop working);
  - keeping the old core for the rest (a second create core would remain).

**ebilling-r1 (2026-10-30):**
- The chat create also refuses `sprk_externalidentifier`. Its only writer is ebilling's resolution step (task 044),
  which normalises the value and fills the issuing organisation the alternate key needs. A chat-created row could
  route later invoices to the wrong matter.
- The four new invoice-family registry entries derive their mapping-protected columns from the guard's per-table
  `CoreRecordLookups`. This was done in 017b.
- A system-principal create of an invoice-family table being refused (403) is OK as the interim.

**Chat tool error codes (017, ADR-019 + NFR-07; no owner question).** Chat follows task 018's Office precedent: each
refusal kind maps to the handler's existing tool error code, and only the text comes from the shared map. Existing
codes (`AccessDenied`, `VALIDATION_FAILED`, `INTERNAL_ERROR`) do not change.

## 2026-10-30 — 017 phase 2d design answers (coordinator; within existing contracts)

2d's implementer raised three questions while reading. Each is settled without changing an existing contract.

1. **I-16 under decision 2a.** Only an I-1 derivation failure refuses the create.
   - `CoreAncestorResolver`'s I-16 resolvers are documented "never throws … display only, never fails a write"
     (`CoreAncestorResolver.cs` ~:1104-1110) and return null instead of failing.
   - So I-16 is set before the create when it resolves, and re-derived after the create.
   - That satisfies 2a ("refuses when the derivation fails") and keeps the I-16 contract.
2. **Refusal for a failed I-1 derivation.** Two new codes, using the service's existing kinds:
   - a permanent data condition (e.g. multiple roots): `Invalid` 400 `record_creation.stamp_underivable`;
   - a transient read fault: `Unavailable` 503 `record_creation.stamp_unavailable`.

   No existing code changes (NFR-07). UAC-r2 is pinged on refusal-code changes before merge.
3. **The `email.draft` path choice: option (a).**
   - The handler keeps deriving the ancestry, but only to choose owned vs run-as-user and the sender
     (`IsFiledUnderAnOwnershipParent`).
   - It no longer writes the stamp into the payload; the service's step 10 does that.
   - This preserves today's owner and sender for a draft filed only through its stamp (under an event or
     communication that is itself under a matter).
   - Rejected: (b), moving the path choice into the service. It changes the service's contract for one caller.

Split of the remaining 2d work:
- 2d-1 (step 10) and 2d-2 (task/contact/account) run in parallel;
- then 2d-3 (chat create + budget) and 2d-4 (email.draft);
- then 2d-5 (delete the create half of OwnedChildWrite, publish delta, CVE, notes).

**Revision (2026-10-30, the same day): `email.draft` uses option (a′), not (a).**
2d-4 probed the real resolver and found that (a) changes behaviour. The service's step 10 writes the stamp only into
the create body, and the owner (step 7) and the AppendTo checks (step 5) read the item.
- Today, a draft regarding an invoice under a secure, isolated matter goes to `SecureNamedTeam`. Under (a) it would
  go to the child BU team.
- A matter flagged secure but not isolated would no longer be refused.
- A caller who can AppendTo the invoice but not its matter would no longer be refused.

Under (a′) the handler keeps writing its derived stamp into the create item, as it does today, and step 10 has nothing
missing to add. Owners and refusals are unchanged, and the service needs no change.
- The sender stays the handler's: `sprk_sentby` is a server-set lookup. The service never writes "for" columns; step
  9 leaves them to the adapters.
- The handler keeps ONE WhoAmI. It guards the AppendTo check that must run before the derivation (otherwise an
  existence oracle).
- This deviates from 017's criterion "no longer resolve WhoAmI themselves". The reason is recorded here and goes in the
  PR.
- Option (b) is rejected for now: making steps 5 and 7 read step 10's stamp changes the contract for every caller and
  needs owner and UAC-r2 review.

### 2026-10-30 — Task 017: keep the three invoice-family registry entries (ebilling-r1)

- **Question** (from the 017 code-review gate): `RecordTableRules` registers `sprk_invoicesubmission`, `sprk_invoiceapproval` and `sprk_invoicecorrection`, each with an `InvoiceFamilyRule` and the mapping-protected core lookups. No create path reaches them through the service today: ebilling's persister writes them directly, and chat blocks them.
- **Answer** (ebilling-r1, 2026-10-30): keep them. With the entries already registered, ebilling's single creator can move onto the service later without the reconcile recipe. The 017-r2 drift test pins the registry's set to the guard's `CoreRecordLookups` keys.
- **ebilling-r1 on PR #2486:** no findings. The chat-side invoice-family not-found surfacing as `AccessDenied` with the uniform text is right; it matches correction (b). `CheckInvoiceFamilyAsync` and the refile half are unchanged.
- **UAC-r2 on PR #2486:** one F1 finding. Chat-created roots began sending external invitations (grantor = the caller), where master passed a null grantor. Fixed on `task/fm-017-r3` by restoring master's behaviour. Whether chat should invite is a separate product decision for the owner.
- **UAC-r2 on `sprk_userentityassociation`:** a known limit, not access-bearing. The only reader that affects access is `ParticipantCorrelationRung`, which reads contacts only. A stale owner row from the #2483 window lasts until the nightly reconciliation; UAC-r2 filed the housekeeping on their side.

### 2026-10-31 — UAC-r2 task 195 merged (d0c1c5dc3c, #2496): what task 040 builds on

- **What changed.** A project's regarding pair (`sprk_regardingrecordid` + `sprk_regardingrecordtype`) is now its one filing root, and only the BFF writes it.
  - **The writer:** one core, `Services/Dataverse/ProjectMatterFiling.cs`.
  - **Its callers:** `PATCH /api/v1/child-records/sprk_project/{id}` and the chat update tool.
  - **Unchanged:** W7's Matter → Project edge (`RecordFamilyEdges.cs:231-233`).
- **The route's body.** It accepts exactly this, and nothing else: `{"sprk_regardingrecordid":"<matter>","sprk_regardingrecordtype@odata.bind":"/sprk_recordtype_refs(<matter type>)"}`.
- **Moves.** A project already filed under another record answers 409 `project_filing.already_filed` and nothing is written, unless the call passes `?move=true`. The chat tool always passes `move`.
- **Secure matters.** Filing under a secure matter is refused with 409 `project_filing.secure_matter_unsupported` until #1506 (task 178) is fixed. Moving a project from a secure matter to an ordinary one is allowed, and the project stays secure.
- **Census.** The root-owner report gains three counts: `projectsWithMatterLinkButNoPair`, `projectsWhosePairMatterHasNoLink` and `projectsWhosePairTypeIsUnknown`.
- **Owner decisions** (as UAC-r2 reports them):
  - a project can be re-filed to another matter;
  - matters are added to a project's list only through the wizard;
  - the backfill is accepted.
- **Not live yet.** The dev deploy, the ribbon import and the backfill all wait for the owner's go in UAC-r2's session. UAC-r2 will send the deploy time. Task 076's "projects pair-filed" check depends on that backfill.

### 2026-10-31 — FR-38 widened before task 039 (owner, in this session)

- **Tables.** FR-38 named only the registry's search-indexed tables (matter, project, document). Work assignment, event and invoice also carry `sprk_ai_search_index`, and their wizards bind the user's BU index on it (`workAssignmentService.ts:405-414`, `eventService.ts:233-267`, `invoiceService.ts:204-213`). #2399 itself was a Work Assignment create, so FR-38 as written left it able to recur. **Owner: mark `sprk_workassignment`, `sprk_event` and `sprk_invoice` `SearchIndexed = true`.** All six tables then get the drop, and step 8 fills the final owning BU's index (often none). Repair's step 8 (`RepairSearchIndexAsync`) follows the same flag.
- **Columns.** The legacy text column `sprk_searchindexname` is a second way to choose a record's index. `SearchIndexNameResolver` reads the lookup first and then the name, and where no BU has an index lookup (every customer environment, Q2 = (B)), the name decides. **Owner: both columns are server-owned.** A client value in either is dropped, before step 5 and on every BFF update path. Step 8 is the **only writer** of both columns on these tables: a value from field mapping or an adapter's post-mapping default is removed too, because an explicit `mappingSource` would otherwise let a client pick a parent whose index gets copied. No dev mapping rule targets either column (checked 2026-10-31, `sprk_fieldmappingrule` source/target LIKE '%search%': 0 rows). A server writer that sets an index on purpose escalates; it is not silently dropped.
- **The drop log** covers both columns. It carries ids only: record, table and the dropped and applied index id, plus, for the name column, the dropped and applied index NAME. An index name is catalog configuration, not personal data. UAC-r2 condition 4 is kept (no user names, no emails).
- **Reviews.** UAC-r2 reviewed FR-38 (conditions 1-4); ebilling-r1 reviews registry rules (the `sprk_invoice` flag). Both are told; the amendment ships in 039's PR for their review there.

### 2026-11-01 — UAC-r2 task 195 is live on dev

- **What's live** (UAC-r2's after-notice):
  - BFF d0c1c5dc3c since 2026-11-01T03:09:00Z;
  - the wizard commands script and the two Create wizard pages since 03:12Z;
  - the Project ↔ Matter subgrid ribbon since about 03:27Z.
- **Backfill:**
  - applied 03:14Z: all 7 N:N-linked projects are now pair-filed;
  - 3 of them re-owned to their root's owner at 03:15Z;
  - their children followed at 03:21Z and 03:24Z.
- **Gate:** PASS. 0 5xx, 0 ownership refusals, and the reconciler has converged.
- **Visible change:** PRJT.10001.01 now inherits Restricted from its matter REAL-2026-123456.02.
- **Facts for 040:** on dev, every project with a matter link has a pair, and none is filed under a secure matter. 195 no longer blocks 040, but 040 still needs 005 (`-Apply` on hold, owner) and 019.
- **For 076:** the "projects pair-filed" check can now be run.

### 2026-11-01 — #2502: users never choose the index; the index follows the content type (owner, in this session)

- **Owner:** "users should not choose the index; the index is determined by the document/file type."
- **For `POST /api/ai/rag/index-file`:** the client's `searchIndexName` is not honoured. The server picks the index from what is being indexed, using the canonical catalog (`docs/architecture/AI-SEARCH-INDEX-CATALOG.md`):
  - SPE document chunks go to `spaarke-files-index` (plus `spaarke-discovery-index`);
  - chat session files go to `spaarke-session-files`;
  - invoices go to `spaarke-invoices-index`;
  - and so on.
- **Found while checking:** the route stamps `sprk_searchindexname` with `GetDefaultIndexName()` even when the chunks went to a client-named index (`RagEndpoints.cs:638-648`). The record can therefore name an index the file is not in. The fix removes this mismatch.
- **Open (asked of the owner):** this rule pulls against the per-BU index hook kept by Q2 = (B), 2026-10-29: BU → `sprk_ai_search_index`, step 8's fill, and the resolver's parent-BU chain. Do the per-BU record columns stay as a hook, for example a Secure Records index, or go?
- **Not yet scheduled.** It takes the scope path (design → UAC-r2 review → spec amendment → task → TASK-INDEX → owner approval).

### 2026-11-01 — Chat creates send no external invitations (owner, in this session)

- **Owner:** "we can leave off invitations from chats; this functionality may change so we do not need to extend the chat functionality at this time."
- **Effect:** the chat Owned create keeps `Grantor = AssignedAccessGrantor.None` (task 017). The external "Assigned To" grants and shares are made, but no invitation is sent and no `sprk_grantedby` is recorded. No change is planned, and the open owner item is closed.

### 2026-11-02 — Owner accepts the three recommendations (in this session)

1. **#2502, index routing.** Plan one follow-up task through the scope path (design → UAC-r2 review → spec amendment → task → TASK-INDEX → owner approval). The task covers:
   - `rag/index-file` ignores a caller's `searchIndexName` and uses the configured files index;
   - the per-BU record routing is retired: step 8 stops copying the business unit's index, the indexer stops reading the record and BU columns, and the wizards stop sending them (W3);
   - existing column values stay;
   - the `sprk_aisearchindex` catalog table stays (provisioning seeds it; the picker and the allow-list read it).

   Facts behind it (checked on dev 2026-11-01): every business unit that has an index names `spaarke-files-index`, which equals `KnowledgeIndexName`; customer business units have none; each customer has its own search service with the fixed index names.
2. **Chat invitations.** None (recorded 2026-11-01).
3. **#2483, change 1 accepted.** In the target model, every record is owned by the customer's main business unit or by Secure Record, so owning an ordinary record by its root's business unit changes nothing there. It matches the reconciler and lets the fix close the 2–4 minute Make Secure window. The fix proceeds as UAC-r2 planned: a characterization test of today's window, then the fix (derive the stamp before step 7) as its own change, reviewed by UAC-r2. It also takes the scope path, as a task in this project.

### 2026-11-02 — UAC-r2: #2483 addendum (§15) OK with conditions

- **Verdict.** UAC-r2 returned OK with conditions.
- **Conditions.** They are recorded in full at the end of `notes/design-addendum-2483-stamp-before-owner.md`:
  - (a) two tests for row 5 (the transient case and the durable case);
  - (b) the refusal words stay the same, and there is no "SecuringRoot" exemption;
  - (c) a Repair skip is counted and logged;
  - thread filing is fixed in 042b if the parity theory confirms the gap; otherwise it is filed;
  - reconciler parity for row 7;
  - a creator-visibility assertion;
  - a full UAC-r2 pass on the 042b diff before merge.
- **Next on the scope path.** The spec is amended, then tasks 042a and 042b are created and added to TASK-INDEX, then the owner approves.

### 2026-11-02 — #2502 addendum (§14) reviewed; owner answers on old chunks, the Family deploy and event numbers

- **#2502 §14 reviews.**
  - UAC-r2: OK, with conditions.
  - Provisioning: no dependency.
  - Both are recorded at the end of `notes/design-addendum-2502-index-routing.md`.
- **Secure Records carve-out.** The owner deferred to UAC-r2, which judged it not needed because secure isolation rests on Read and the per-caller trim. The carve-out is **withdrawn**. A future secure index would be chosen by the server from the record's secure state, under its own design.
- **Old chunks.** Documents found indexed into a non-default index are **left**; they are not cleaned (owner).
- **#2513.** The owner approved importing TrackingFieldTrio 1.0.56 to dev after PR #2516 merges.
- **#2515.** The owner approved numbering events with `EVT-{SEQNUM:6}` on `sprk_event.sprk_eventnumber`, both in the solution and on dev. Existing events stay blank; there is no backfill.

### 2026-11-02 — W5 open questions resolved: the repair marker is a principal set; only Project has quick create on (task 050)

- **Evidence.** `notes/w5-open-questions.md`. Read-only against dev (`spaarkedev1`), on the BFF that carries task 017 (`fcf01480f4`). Task 019 was still pending; the coordinator approved this deviation.

**Q1: the repair marker.**
- **No impersonated creates.** No product path creates under impersonation:
  - `MSCRMCallerID` is set only on GET, PATCH and the owner's own share-revoke (`DataverseImpersonation.cs:64-73`, `DataverseWebApiService.cs:214-233`);
  - the entity-create sender takes no impersonation (`:249-253`);
  - `CallerObjectId` is never sent;
  - `createdonbehalfby` was empty on all 2,446 sampled dev rows.
- **Core creates.** The W2 core checks the caller (an OBO WhoAmI) but writes app-only (`RecordCreationService.cs:208-220`). So `createdby` is the BFF app user for every core create.
- **A single identity is NOT safe as the marker.**
  - Dev has two BFF application users: `SDAP-BFF-SPE-API` until 2026-08-17, and `mi-bff-api-dev` since.
  - The Signal, Decision Record and Budget Revision writers create as `mi-ontology-writer-dev`.
  - The Matter and Project wizards, and four legacy OBO create calls, create as the user.
- **Proposed marker (owner to confirm).** Select rows where `createdon >= max(go-live, watermark)` and `createdby NOT IN ProductPrincipals`.
  - The set is the shared BFF identity (resolved at runtime by WhoAmI), the ontology writer, and a configured list of retired identities. People are never in the set.
  - Selecting too many rows is harmless, because Repair is fill-only and idempotent.
  - `createdonbehalfby` is rejected because it is empty on every row. A server-written origin column is the fallback.
- **Excluded tables.** `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision` stay out of the repair table list.
- **Gap.** App-only creates that bypass the core have `createdby` set to the BFF app user and are never selected: the ebilling persister, ingestion, `CreateDocumentAsync` and `CreateEventAsync`. FR-21's census must either route them through the core or name them as exceptions.
- **Open (owner).** FR-21 says the Signal and Decision writers create through the service's system principal, which is the shared identity. They were given a dedicated managed identity on purpose. Keep it, or move them?

**Q2: quick create (dev only).**
- **Project:** the only table with it on, both live and in SpaarkeMaster (`IsQuickCreateEnabled` 1). It has no quick-create form.
- **Matter:** off. The XML says 0, so the earlier "XML says on" was wrong.
- **"Invoice Line Item":** this is `sprk_billingevent`, off live and 0 in the XML. There is no `sprk_invoicelineitem`.
- **Everything else:** all other `sprk_` registry tables are off.

**FR-24 table list.**
- `-Verify` covers the 22 `sprk_` tables in `RecordTableRules.All`. The only change needed is to turn `sprk_project` off.
- Reported but not changed:
  - `task` (a platform table, on);
  - the dormant quick-create forms on matter, document, event, invoice and kpiassessment.
- Other environments were not read; `-Verify` reports each environment separately.

**For tasks 051 and 052.**
- 051 also checks what Event "Add Memo" (`useQuickCreateForm` on `sprk_memo`) and Project "+ New" do live.
- 052 builds the selection from the principal set above, and adds Repair's go-live cutoff (019 finding P1-3).

### 2026-11-02 — Ontology-r1 research: the Signal/Decision writers keep their dedicated identity (recommendation; owner to decide)

- **Source:** ontology-r1's FR-21 answer (keep the dedicated writer identity; named FR-21 exception), reviewed privately with UAC-r2. The owner asked for deep research first.
- **Why the identity exists:** ontology D-1, "ADR-028 path A", 2026-10-03/04.
  - The shared BFF identities are System Administrator. Dataverse privileges are additive with no deny, so those identities can Write and Delete `sprk_decisionrecord`.
  - `mi-ontology-writer-dev` holds only the Spaarke Ontology Service role:
    - Create and Write on Signal;
    - Create only on Decision Record and Budget Revision.
  - That role is what makes the ledger append-only by privilege. It also limits attribution and blast radius.
  - It has no secret, so the ADR-028 E-1 to E-3 rules do not apply.
- **What depends on it:**
  - the idempotency and gate lookups filter on `createdby = writer user` (`DecisionRecordWriter.cs:733/1017/1662`, DecisionCommitService, SignalReadService, SignalReevaluationJob, GateDecisionRecordSharing);
  - three ArchTests;
  - the field-security profiles;
  - the `ontology-writer-failures` alert.
- **Provisioning gap:** provisioning never creates this identity; dev was set up by hand. Filed as #2518 (ontology DI-138).
- **Recommendation:**
  - keep the dedicated identity;
  - name it as an FR-21 exception: SignalWriter, SignalSuppressionService, DecisionRecordWriter, GateDecisionRecordSharing and the revise-budget step create through `OntologyWriterDataverseClient`, not `RecordCreationService`;
  - do not give the service a second principal.
- **For the FR-25 marker:** both `mi-bff-api-*` and the writer's application user are product principals. Resolve each from its configured client id (`systemuser.applicationid`) or WhoAmI, never from a dev GUID.

### 2026-11-02 — Owner approves tasks 041, 042a and 042b; the repair outcome; the 019 live-check rows

1. **Tasks 041, 042a and 042b: approved.** Under the scope path, that completes them. 042a starts now. 041 and 042b start once 019 clears.
2. **Repair and the marker.** The owner's words: "however technically this happens, what is important is that we have a process that for any records created outside the server-mediated owner process, there is a repair process that aligns all family members to the same owner; so however is the best approach to achieve this."
   - **The outcome is binding.** The repair process gives every member of a family the same owner. That includes members that already exist when a root is repaired, and the cross-BU re-own that task 016 left open. FR-25 is amended to say so.
   - **The mechanism is our choice.** We take the recommended route:
     - the marker is the principal set (task 050);
     - the Signal, Decision Record and Budget Revision writers keep their dedicated identity, as a named FR-21 exception, per ontology-r1's research. The service gets no second principal;
     - those three append-only tables stay out of repair.
   - **Task 052** must design the family cascade.
3. **The 019 live-check rows are approved.** They are created once R3's batch is live on dev. The test rows carry the `FMSWP-019` prefix, and their ids are recorded so the owner can delete them.

### 2026-11-02 — UAC-r2: a server create that files a project under a matter also writes the `sprk_Project_Matter_nn` link (new scope item, ours)

- **Found by:** 019 live check B. Office quick-create project `1cccf07d-0e0a-43f9-8ea0-f40533d5d691` got its matter pair but no N:N link row. UAC-r2's census reported "1 project filed under a matter with no link row".
- **Who writes the link today:** no BFF path writes it, now or before W2. Only the CreateMatter and CreateProject wizards do, through a client `$ref`.
- **UAC-r2's ruling (task 195):**
  - The pair is the only filing root. The N:N link grants nothing, and no server code reads it.
  - The matter's Projects subgrid lists projects through the link, and the census counts "pair but no link" as drift.
  - So a server path that files a project under a matter must also write the link. After W3 the server is its only writer.
- **Ownership:**
  - **Create path (ours):** `RecordCreationService` step 12, for `sprk_project` with a matter pair.
  - **Re-file path (UAC-r2):** task 195's core, reached from the chat update tool, has the same gap. UAC-r2 fixes it.
- **UAC-r2's rules** (they review our diff):
  1. The link is written app-only, after the pair is written and the caller's AppendTo on the matter has passed.
  2. It is idempotent: an existing link is not an error.
  3. A failed link write logs a Warning and does not fail the create (still 201).
  4. Only a matter pair gets a link.
  5. On a re-file, the earlier matter's link stays (195 §2.3).
  6. Tests: one link for a matter pair; none otherwise; a failed write still returns 201 and logs the Warning.
- **Issue:** #2524 (filed by UAC-r2), covering both paths.
- **Next on the scope path:** a design note (UAC-r2 has already reviewed it through these rules), then a spec amendment, task-create for task 043, TASK-INDEX, and owner approval.
- **Project `1cccf07d`:** it gets its link from the backfill, or by hand after the fix. Its access needs no change.

## 2026-11-03 — Task 019 closed (W2 gate passed); 042a closed

- **Gate result:**
  - two adversarial passes with no open F-class finding (#2514, #2520);
  - live checks A, B and C pass;
  - the negative check passes as `# UAC Child BU Test User`. The child-records 404 and the Office 403 `OFFICE_009` are byte-identical to a nonexistent parent; no row was created.
- **Clauses moved to task 020:**
  - check A's "mapped fields";
  - the negative criterion's "denied mapping source → no mapped values + one warning". It cannot be reached live while every route's mapping source is also its filing parent; an explicit `mappingSource` arrives with 020. Unit tests cover it today.
- **Deviation:** the deploy was R3's batched dev deploy (5796774091), not a bff-deploy run from this session (standing rule).
- **Check C:** the owner supplied the name and parent when the Assistant asked. That is correct behaviour. Event `6d1563bb-…` stays (owner: do not delete).
- **Chat tool description:** re-seeded on dev (row `18b3531f-…`) so the model knows that `sprk_eventnumber` is set by the server.
- **042a:** done in #2521 (merged 2026-11-02T19:28Z); the index is now ✅.
- **Next:** 041 and 042b, **in sequence, not in parallel** (TASK-INDEX: both edit `RecordCreationService*.cs`). 041 goes first because W3 waits on it. 020 depends only on 019 and may run beside 041. UAC-r2 reviews the 041 diff (scoped) and the 042b diff (full) before merge.

## 2026-11-03 — Task 041: review and UAC-r2 verdict

- **Opus adversarial review.** Over c58ea58faf. 0 High, and no authorization bypass, existence oracle or data-loss path. Two findings were F-class:
  - F4: the bulk handler and the Office worker did not stamp the index. Fixed in round 1.
  - F3: the MF7 test proved nothing. Fixed in round 1.
  - K4 items 3–5 were also fixed in round 1.
- **Scoped Sonnet re-check.** Over b087022071. All five findings fixed, and nothing F-class new. N1 (a scoped service held by the Office worker), N3 and N4 were fixed in round 2 (bdadc2a3b9). N2 (the Office worker's stamp has no harness) is a disclosed known limit.
- **Filed.**
  - DI-013 #2538: the backfill scripts.
  - DI-014 #2539: the DeliverToIndex node reports an index it does not use.
  - The P2-5 note in task-019 is corrected: a malformed client index is dropped but no longer logged (K4).
- **UAC-r2 verdict (AC17).** "PASSES WITH NOTES; nothing blocks the PR."
  - Only write-time routing was removed, and no retrieval trim or search-time selection moved.
  - The drops are intact at all nine sites.
  - The AC9 exceptions are as listed, and no drop, refusal or authorization assertion was weakened.
  - NOTE 1: restore the document trigger-2 Repair test as owner-only. Done in round 3.
  - NOTE 2: the removed `ResolveAsync` Verify in Issue1510 is correct, so no action.
- **UAC-r2 on task 020 F-1.** No objection to refusing a caller-supplied `sprk_issecure` on a Matter create in the service with `sdap.access.secure_flag_transition_only`, exactly as for Project, with explicit false and null treated as the Project rule treats them. UAC-r2 sees the diff before merge.

## 2026-11-03 — Task 020 secure-flag refusal: UAC-r2 verdict; 005 applied on dev

- **UAC-r2 on 020 F-1/F-2** (3c4941bec5..2b6920e009): "OK WITH NOTES, merge when CI is green."
  - F-1: a Matter create that names `sprk_issecure` is refused in the service (plan code, text and 403; true, false and null; every principal).
  - F-2 is a real fix: `MappedItem.Columns` keeps null keys.
  - Running the helper before `PlanCreateAsync` changes no answer and adds no existence signal.
  - Note 1 (cosmetic): align the access-record refusal text with the plan's "…so the {noun} was not created". This goes into the 020 review fix round.
  - Note 2: UAC-r2 will make `SecureRootFilingGate.CheckAsync` and `PlanCreateAsync` read named columns, nulls included, as its own follow-up. Those update writers feed `WritesOf(item)` (`DataverseUpdateRecordHandler:299`, `ProjectMatterFiling:256`). Nothing more is needed in 020.
- **005 `-Apply` on dev**, with the owner's OK given in this session:
  - 40 Copy rules, all created by the owner, the newest 2026-10-22;
  - 36 migrated (20 empty, 16 One-time), 0 failed, VERIFY PASS;
  - a second pass found 0 to migrate.
  - Detail is in task-005-notes.

## 2026-11-03 — 042b F1: a caller-sent stamp is replaced by the derived one in the owner question (owner: A')

**Finding:** both 042b adversarial passes independently found the same F1 (the correctness pass confirmed it with a probe). Step 6½ is fill-only, so a caller-sent `sprk_regardingmatter` that disagrees with the intermediate's root is used for the owner decision. The step-12 restamp then overwrites it, which leaves the #2483 state for 2–4 minutes. This predates 042b.

**UAC-r2 (2026-11-03):** A with one change, "replace, not add": step 7 asks over the values the row ends with. The derived value replaces a disagreeing held stamp, and an agreeing stamp is kept unchanged. "Add" gets the reverse case wrong (a sent secure matter would make its owner briefly own a row stamped with an ordinary root). B (a new 400) is not recommended. C is not acceptable.

**Owner (2026-11-03, in this session): A' (replace).**
- The bind stays fill-only.
- The derived root is never probed as the caller, and no refusal changes.
- `CoreAncestorRestamper.PlanMissingStampAsync` (or a sibling) returns the full plan. That is one file outside 042b's two-file scope, justified in the task notes.
- AC12 is amended: a sent stamp that agrees changes nothing; one that disagrees is replaced in the owner question.
- Repair gets the same fix (security finding 6).
- Tests cover both directions plus the agreeing case.

Also agreed with UAC-r2: Signal / Decision Record under a team-owned intermediate of a Secure root refuse 409 `NoOwnerSource` (fail closed). This is K1 and gets a §15.4 row. #2556 (DI-017) and #2557 (DI-018) are filed for the owner.

## 2026-11-04 — 042c: a chat-created Signal or Decision Record with a Secure core is refused (owner)

**Decision (owner, this session, 2026-11-04): Accept.** After 042c, chat can no longer create a Signal or Decision Record whose core record (`sprk_corerecordtype` + `sprk_corerecordid`) is Secure. The request is refused with 409 `NoOwnerSource`, using the same code, status and words as design §15.4 row 8. Before 042c, chat created the row team-owned with the ordinary owner, until the reconciler moved it to the Secure team.

- **Unchanged:** `OwnerDecided` and the ontology writers (`SignalWriter`, `DecisionRecordWriter`).
- **Ordering:** a caller without AppendTo on the core gets the uniform AppendTo refusal, never the 409, so the refusal does not reveal that the core is Secure.
- **Approvals:** UAC-r2 OK'd the AC2 amendment with four conditions:
  1. ordering, plus a faulted catalog read gives the existing 503;
  2. the 409 is returned only when the caller holds AppendTo on the core;
  3. the must-not-fire tests;
  4. the parity of an ordinary core in Y with a subject in X.
- **Release notes:** list this change.
- **Rejected alternatives:**
  - giving Signal and Decision Record a Secure owner at create, which would change the 042b rule;
  - refusing all chat creates of these tables, which needs ontology-r1's agreement.

## 2026-11-04 — Owner decisions after the 031 review and on half-written pairs (owner, this session)

1. **031, playbook and event-type AppendTo:** exempt them in code. The route filter already authorises the lookup for this caller, so step 5 does not ask the caller for AppendTo on it. The scope is narrow (a request option; a filing or ownership-parent lookup is refused as an argument error), and UAC-r2 reviews it. Also: security roles must be aligned with the permissions each feature requires. This becomes task 046.
2. **Document as a mapping source:** if the user can read the document and may create or update the analysis, the document's values map. This becomes task 043.
3. **042c, a core type with no id: no refusal**, because a refused chat create is a dead end for the user. The server fills the id from the derived root, or clears the stray type, with a visible warning. A half core pair must never be written on any path. Task 044 covers the paths outside the server: field security plus a repair-pass backstop.
4. **Regarding pair:** where a regarding is set, every related row that should carry the regarding value gets it. Make the opposite impossible. This becomes task 045: an inventory first, then the design for the owner.

Numbering note (2026-11-04): the "task 043" planned on 2026-11-02 for the `sprk_Project_Matter_nn` link (#2524) was never written. 043 is now the Document mapping-source task. #2524 gets the next free number (047) when its POML is written.

## 2026-11-04 — Signal, Decision Record and Budget Revision rows are created only by their writers (owner; ontology D-204)

**Decision (owner, this session):** the general create path (`RecordCreationService` and the chat `dataverse.create_record` tool) refuses `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision` as whole tables, for the caller and for the system principal, before anything is written. Only the dedicated writers create these rows (`SignalWriter`, `DecisionRecordWriter`, the decision action executors); they do not use the general create and are unchanged. Chat keeps creating every other table.

**Why:** these rows are append-only evidence with one server-side owner each (ontology D-204), and the writers keep the lineage columns that downstream readers rely on consistent. Ontology confirmed on master `7c7d24a3b7` that no BFF code creates these tables through `RecordCreationService`, and that ordinary roles hold no Create on them, so no first-party user flow changes.

**Effect on 042c:** this replaces the fill-or-clear choice of 2026-11-04 (decision 3) for these tables: with the tables refused, step 6¾ is not reachable for them. 042c is reworked to the refusal; UAC-r2 and ontology review the diff. Task 044 is re-scoped to the writers' paths (no service create of the pair to keep working).

## 2026-11-04 — The generic update tools do not change Signal, Decision Record or Budget Revision rows (owner; D-204 extended)

**Decision (owner, this session):** chat's update-record tool, the playbook Update Record node and (subject to ontology's answer) playbook output mapping refuse to change `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision`. These rows are changed only by their own processes (the Signal lifecycle and evaluation, the decision commit, the budget revision executor), which are unchanged. Requested by ontology; UAC-r2 classifies it as an integrity rule. Read-only census on dev: none of the 93 configured playbook nodes targets these tables. Task 048.

## 2026-11-05 — Chat delete honours D-204 (owner)

**Decision (owner, this session):** the general delete path (chat `dataverse.delete_record`, and any other generic delete of a caller- or config-chosen table) refuses `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision`, reading the same `CreatedOnlyByItsWriter` flag. Admin privileges in the model-driven app are unchanged; person-role Delete privileges are task 046's question. Ontology and UAC-r2 agree. Added to task 048.

## 2026-11-05 — NFR-02 exceeded on the server create path: speed up the server (owner)

**Finding:** task 021's live timing (notes/task-021-live.md): the Create Matter create p95 rose by +2634 ms (budget +500 ms). Server medians: CallerChecks 983 ms (sequential AppendTo per bound record), PostCreate 1331 ms (the I-12 L1 assigned-access grant and the membership event inside the request), TableRules 302, Owner 261, FieldMapping 176, Create 136.

**Decision (owner, this session):** speed up the server rather than raise the budget: run the per-record caller checks concurrently, and move the assigned-access grant (and membership event) out of the request so it completes within seconds after the record is returned. UAC-r2 reviews the grant timing. Then create the Project to Matter profile on dev (owner approved 2026-11-05) and re-measure. The 021 wizard deploy waits for that. Task 049.

## 2026-11-05 — Role rulings on task 046's gaps (owner, given in UAC-r2's session)

| Gap | Ruling |
|---|---|
| G1 Report Card | Spaarke Core User: Create, Append and AppendTo on `sprk_reportcard` at Deep (AppendTo also files a KPI assessment under a report card) |
| G2 Event type | Spaarke Office Add In User: AppendTo on `sprk_eventtype_ref` Deep → Global |
| G3 Office save to a contact (Basic User) | Not applied; Spaarke Basic User is unchanged. Office Add In User carries it (G4) |
| G4 Office quick create with an assignee | Spaarke Office Add In User: AppendTo on `contact` Basic → Deep |
| G5 Procedure override | Spaarke Billing Administrator: Read on `sprk_procedure` at Deep (already on dev; the file aligns) |
| G6 Office version save | Spaarke **Office Add In User**: Write on `sprk_document` at Deep |
| G7 / G8 Event regarding an account / a budget | Spaarke Office Add In User: AppendTo on `account` and on `sprk_budget` at Deep |
| Office To Do; the Console Do lane | Stay Core-only |
| New Billing Event (no role creates it) | Not decided here; passed to ebilling as a ribbon question |

**Role model (owner):** add-on roles are **standalone**: an add-on may be held without a base role, so each add-on must meet its own features alone. Task 046b extends the matrix and guard to check each add-on alone, and sends the resulting gap list to UAC-r2 for one owner decision before any of it is applied.

**Writer-only tables at role level:** no person role holds Create, Write or Delete on `sprk_signal`, `sprk_decisionrecord` or `sprk_budgetrevision`, except Spaarke Ontology Administrator, the designated cleanup role (ontology D-160). Task 046b's guard pins that rule.

**How it is applied:** task 046b builds the role-file changes and an apply script (`-WhatIf`, `-Apply`, `-Verify`; edits the root role through the Web API and reads back every business-unit copy). UAC-r2 reviews it and runs `-Apply` on dev in its session under the owner's go.

## 2026-11-05 — Standalone add-on rulings, two role removals, and secure-from-creation wizard projects (owner, given in UAC-r2's session; UAC-r2 decisions.md 51793949a0)

- **Not standalone, by ruling:** Office Add In User and AI Analysis User are not standalone; neither is meant to be held alone, so no gap is opened for either alone (G9 dropped). Console User needs Core User. The guard and §5.1 record each as a ruled exception with that reason.
- **Two removals (task 046b):** Ontology Administrator loses Create on `sprk_budgetrevision` and keeps Write and Delete. Basic User loses AppendTo on `sprk_reportcard`, provided no §5.1 feature needs it on Basic User. The apply script gets an explicit removal step with the same fail-closed reads and read-back of every business-unit copy. UAC-r2 applies it on dev.
- **Secure from creation (task 049):** a project the wizard creates as secure is secure from creation. The wizard's create declares the intent in one optional field, validated on the server. RecordCreationService handles it like an isolated create, and provisioning does only what is left.
- **049 backstop:** the record-busy answer on `/no-access/enforce` and `/assigned-access/sync` stays a 500 ProblemDetails with a message and trace id. The restricted-root and ledger-root scans take the ordered read and the recent-change window, like the first scan. Removals by the reconciliation job stay report-only until UAC-r2's gate G-8 (#2574).

## 2026-11-06 — Removal R2 is not applied (under the 2026-11-05 ruling's own condition)

Basic User keeps AppendTo on `sprk_reportcard`. The ruling removed it only if no §5.1 feature needed it on Basic User. Communication send, reply and re-file ask that privilege as the caller when a report card is a regarding record (`CommunicationRecordAuthorizationFilter`), so the condition fails. Task 046b adds matrix rows for those routes. Removal R1 (Ontology Administrator loses Create on `sprk_budgetrevision`) proceeds. Recorded by fm and UAC-r2; the owner was informed through UAC-r2.

## 2026-11-06 — Communication send, reply and re-file need Core User (owner, in fm's session)

Basic User is not raised for communication send, reply or re-file (task 046b gaps G11 and G12). These features need Core User, like the other add-on rulings of 2026-11-05. A caller who holds Basic User only is refused the way each route already refuses: a 403 ProblemDetails, or the uniform 404 where the route does not reveal the target (re-file). The gaps stay in the guard's baseline, marked "owner ruled: not granted", so any change to them is a deliberate one. Removal R1 was applied on dev on 2026-11-06 (UAC-r2, from `e6c0c94fa3`).

## 2026-11-06 — Basic User is read-only on Spaarke tables (owner, in fm's session)

On every Spaarke table (`sprk_*`), Spaarke Basic User holds Read only. Creating, updating, deleting, filing, assigning or sharing Spaarke records needs Core User. Platform privileges that Dataverse needs for any user are not affected. Task 046c applies the rule: the role file, a removal through the reviewed `-Remove` step (run by UAC-r2 under the owner's go), and a guard.

## 2026-11-06 — Core User reads communication threads at Deep (owner, in fm's session)

Sending into a communication thread asks Read on `sprk_communicationthread` as the caller, and that table is user-owned. Core User gets that Read at Deep, so a Core user can reply into threads across their business-unit tree, like Core User's other reads (task 046b gap G13). For Basic User the gap stays "owner ruled: not granted" under the read-only ruling. UAC-r2 applies it through the reviewed add step.

## 2026-11-06 — G13 grant withdrawn (supersedes "Core User reads communication threads at Deep")

The ruling above rested on a wrong premise. Core User is not raised. Direct threads are private by ownership and explicit shares, so the depth of Read on `sprk_communicationthread` is part of their privacy rule and stays Basic. Record threads are owned by the owner of the record they are filed to, normally a team, and a member reads them at Basic. Task 046b sets the send/reply row to match. G13 stays open only if a record thread can remain owned by its creator. The owner was told in fm's session.

## 2026-11-06 — G13 is not a gap (closes "G13 grant withdrawn")

The send/reply thread read needs Basic. Record threads are owned by their record's team at creation (`ThreadResolver`), and a member reads them at Basic. Threads anchored to a party, and Direct threads, are read through ownership and shares, by the privacy rule. Core User and Basic User both hold Basic, so no role changes. Task 046b, `5d079d1508`.

## 2026-11-06 — Task 032 becomes a design task (owner, in fm's session)

- **Finding (task 032 agent, `task/fm-032` 96769b68bf):** since ebilling-r1 task 050a (D-134), invoice review creates no invoice. The only invoice creator is ebilling's `InvoiceSubmissionPersister`, which writes the invoice, lines, approval, Stored File rows and submission link in one transaction with fixed ids, inside a background job with no caller token. `RecordCreationService` creates one row per call with a new id and refuses mapping for a system principal.
- **Options put to the owner:** drop 032 and name the persister as an exception; a new design task; or a mapping-only step inside the persister.
- **Ruling:** a new Opus-tier design task. The create service gains a fixed-id, multi-row transactional create and a way to map under a system principal (for example the submitter as the recorded caller). UAC-r2 and ebilling-r1 review it. Task 032's POML is re-scoped before it runs; the single-creator rule holds (no second creator).

## 2026-11-06 — W3 direction: named exceptions plus guards, no core redesign in R1 (owner, in fm's session; supersedes "Task 032 becomes a design task")

- **Rule as written:** one server owner per invariant (`DATAVERSE-WRITE-PATH-ARCHITECTURE.md` :17, :58, :81). The invariant owners are the classes (I-1 stamp, record-first owner, mapping engine, secure gates); `RecordCreationService` is the L1 orchestrator that calls them, not the invariant itself.
- **Named exception:** a creator outside the service is valid when it calls the same owner classes for every invariant its table carries (or L3/L4 fill-only reconciliation covers it, fail-closed), and a guard pins it: this creator writes this table and calls these owners, and no other creator does.
- **No core redesign in R1:** no given-id input, no multi-row transactional create, no pinned-owner input and no app-only lookup translator in `RecordCreationService`. Any of these later is an owner-rule change reviewed by UAC-r2. A given id is never accepted on a table whose ids carry meaning to another rule.
- **032:** `InvoiceSubmissionPersister` stays the single creator of the invoice family, named in task 060's matrix with a guard. FR-20's Matter mapping for extracted invoices is deferred (the alternative, a fill-only step in the persister calling the same mapping engine, stays open if the owner wants it in R1).
- **030:** 030a routes the Office To Do through the service (adapter, no core change). 030b keeps the document save outside the service: the association moves into the existing create call, and guards pin that only the server writes the narrowing and pointer columns, that the save calls the stamp and owner classes, and that a client-supplied id that already exists is refused, never updated.
- **034:** the ingestors stay outside the service as named exceptions. Guards pin the dedupe on the message id, the unfiled-communication creator exception on the ingestor path only, and attachment ownership through the same resolver.
- **033:** re-scoped under this rule before it runs; likely dropped.
- **046c:** before Basic User loses them, Core User gains AppendTo on `sprk_agreementtype`, `sprk_organizationtype_ref` and `sprk_documenttype_ref` (Global). The add-on gaps are "requires Core User" (as 046b ruled). Before the live apply, dev users holding Basic plus an add-on without Core User are counted and either given Core User or listed for the owner.
- **Reviewed by:** UAC-r2 (independent read, 2026-11-06).

## 2026-11-06 — Owner rulings: 044 scope, 032 mapping, Office To Do assignee order, live checks (owner, in fm's session)

- **044: no field security on the core pair** (revises 042c's "task 044 covers the paths outside the server: field security plus a repair-pass backstop"). The pair is written whole by the server writers only. Outside the server, an edit is detected and traced, not blocked: column auditing on both pair columns (confirmed on dev), a guard that no non-admin role gains Write on `sprk_signal` or `sprk_decisionrecord`, and task 052's report flags a half pair and a pair that changed after create. The Signals access check keeps a fail-closed re-read when the caller's read of the pair returns neither half. Ontology-r1 and UAC-r2 agreed.
- **032: extracted invoices get Matter field mapping in R1.** `InvoiceSubmissionPersister` stays the single creator; a fill-only mapping step inside it, before its transaction, calls the same mapping engine (W3 direction, option 2). No change to `RecordCreationService`. Ebilling-r1 and UAC-r2 review.
- **Office To Do assignee order:** the user's explicit choice; else the field-mapped value; else the user's own contact (the order 030a implements).
- **Live checks on dev approved:** the Project→Matter profile check and the 021 re-measure, test rows prefixed FMSWP-.

## 2026-11-06 — 049b: only the maker is granted inside the request (UAC-r2)

**Finding (021 re-measure, task-021-live.md):** when the created root's Assigned To is its maker, 049 runs the assigned-access pass inline for every assigned subject (5 subjects, 11 writes, about 6.6 s), so the create p95 was 11.8 s.

**Ruling (UAC-r2):** the inline pass exists so the maker can open the record at once. Inline grants the maker's own subject only; every other subject, external grants included, goes through the queued path the non-maker case uses. Conditions:
1. The queued remainder uses the same ordered scan and revoke-on-change (G-8) path: no new queue, no second owner.
2. If enqueueing the remainder fails, it is logged and the reconciler completes it; the create never reports "all granted".
3. If the maker's inline grant fails, today's behaviour is unchanged.
4. Tests: maker case is exactly 1 subject inline and N-1 queued; the non-maker case is unchanged; an external subject is never inline.

**Amended (UAC-r2, 2026-11-07), condition 2:** when the remainder cannot be enqueued in the maker case, the full pass runs inline once, with the grantor, as master's inline path does (an external assignee still gets the invite), and a neutral event counts it. The reconciler-only fallback was rejected: an external assignee would silently never be invited. Accepted known limits: a narrow race on the unleased maker pass (master's inline full pass has it too), and a one-subject pass acts only on a share.

## 2026-11-07 — Owner decisions on the open questions (owner, in fm's session)

1. **NFR-02:** keep the window as written and speed the create path up further (it is about +2 s over with the grant queued). A new task (049c) profiles the remaining steps and removes the largest costs, with no change to what the create guarantees.
2. **The "Project to Matter (Attorney Matrix)" profile stays on dev.**
3. **046c dev apply: approved.** Core User gains its three AppendTo rights first and is verified; then Basic User's write rights on Spaarke tables are removed and verified. Rollback: `-Restore -Gap R3`. Users holding Basic plus an add-on without Core User are counted before the removal (2026-11-06 condition).
4. **Stash `da43b8a2` (lint-staged automatic backup): drop it** once its content is confirmed committed.
5. **mcp-bff re-merge:** the owner relays it; it does not hold this project.

## 2026-11-07 — 033 dropped (owner, in fm's session)

- The system creators of task 033 (generation, playbook, Signal, Decision) stay outside `RecordCreationService` as named exceptions under the W3 direction; ontology-r1 agreed that leaving the ledger writers out is valid. No field mapping goes into Signal or Decision Record rows.
- 033's one piece another task needed (wiring the app-only column-metadata reader into Repair, including the Money attributes of #2124) moves into task 052.
- Task 060's matrix names these creators as exceptions with guards.

## 2026-11-07 — UAC-r2 review of the 032 design (`task/fm-032` a7b787ffbe): approved with conditions

Binding on 032's implementation, together with ebilling-r1's answers (pending):
- **Reader set (Q-U1):** a role-source assertion in `FeatureRolePrivilegeGuardTests` pins that `sprk_invoice` Read depth is never wider than the smallest core-table Read depth, so a widening fails CI.
- **Partner columns (Q-U2):** the protected targets are the union of the invoice modules' `ReadableColumns`, plus the columns the invoice search-index document builder reads, unless ebilling-r1 shows partners have no search path to invoices. Ebilling-r1 confirms that partner notifications and exports render only module columns (Q-E5).
- **#2188 exemption (Q-U3):** one entry, `Uses = 1`, pinned to the system-reader call site.
- **Field security (Q-U4):**
  - a metadata fault or an unknown `IsSecured` maps nothing;
  - a source column missing from a successful metadata answer drops only its rule, with a Warning naming the rule;
  - metadata is read per delivery, with no cache.
- **Secure-source gate (Q-U5), required:** map nothing when the core record is Secure, or under no-access or hard-deny. Reuse Repair's `SourceMayBeCopiedAsync` logic, not a re-implementation. Tests: a Secure core maps nothing and writes the admin note (must fire); an ordinary core maps (must not fire).
- **Mapped lookups (Q-U6):** written app-only with no AppendTo check, accepted. Filing and access-conferring lookups stay protected. Lookups that target `systemuser`, `team` or `contact` are protected too, unless they are on an explicit allow-list.

## 2026-11-07 — ebilling-r1 review of the 032 design (a7b787ffbe): approved with conditions

These conditions are fixed in the design before any code, together with UAC-r2's above. Checked against `work/ebilling-r1` e418803715.
- **A refused mapped value (Q-E1): re-commit once without the mapped columns.**
  - Only when `FailedRequestIndex == 0`, at least one column was mapped, and the outcome is not unknown.
  - The commit and its catch are wrapped in one local function, called at most twice, so the second attempt goes through the whole existing catch chain.
  - Request 0 and the submission update are rebuilt with the same count and order.
  - §6 also checks option-set membership, numeric min, max and precision, and that a lookup target exists.
  - Tests: refused once, refused twice, refused with nothing mapped.
- **Fault note (Q-E2): faults only.** It is a new `AdminAttentionText` segment kind that `Merge` and `WithoutStopped` recognise, in a fixed order, codes only. The combined text stays within `AdminAttentionMaxLength` (500); the Signal segment shrinks to make room. A test covers the cap.
- **Protected even when empty (Q-E3):** `sprk_currency`, `sprk_invoicedate`, `sprk_billingperiodstart` and `sprk_billingperiodend`, and `sprk_correlationid`.
- **Re-delivery (Q-E4):** a test pins that two deliveries, the first rolled back, store exactly the second delivery's values, and that a resume reads nothing.
- **Search index (Q-E5):** `InvoiceIndexDocument.InvoiceColumns` is excluded by reference. Partners have no search path to invoices; partner rendering is `ReadableColumns` only.
- **Constructor (Q-E6):** three optional trailing parameters. A DI test resolves the persister from the real container with all three non-null, and one Warning is logged when the step does not run.
- **C1, protected set:** an allow-list of target columns approved by ebilling-r1 and UAC-r2, with the deny-list as a second check. `sprk_billingexport` and `sprk_invoiceduedate` are protected unless the owner approves the due date as a target.
- **C2, secure gate:** gate on the owner answer's secure-if-any, already resolved in the persister, so Secure ancestors are covered. Secure means map nothing and log a code, with no admin note. With UAC-r2 Q-U5, this also covers no-access and hard-deny, through Repair's `SourceMayBeCopiedAsync` logic, failing closed.
- **C3, all or nothing:** any rule-fault warning from `ApplyFillOnly` means nothing is mapped and the note is written. A test covers it.
- **C4, determinism:** a static resolver-field key set; ties in `ExecutionOrder` are broken by rule id; the lookup overload of `IsFilingLookup` is used.
- **C5, money guard:** the persister exposes its owned-column list, so the step loads no money or firm string (`InvoiceMoneyColumnsAppOnlyWriteGuardTests`). That guard joins the regression list.

**What 032's code waits on:** 052a's metadata reader (`task/fm-052a`), and the persister layout in `work/ebilling-r1`.
- **UAC-r2 follow-up (2026-11-07):**
  - UAC-r2 backs the allow-list (C1). The proposed list needs sign-off from both ebilling-r1 and UAC-r2.
  - The secure-source gate also maps nothing when the core record is the object of any active, well-formed No Access entry, internal walls included. It reuses `NoAccessListReader` and `NoAccessShareEnforcer.TryClassify`, keyed alongside the owner answer's secure-if-any. A failed read maps nothing.
  - Must-fire and must-not-fire tests.

## 2026-11-07 — Owner approves the 045 recommendations (in fm's session)

Design: `task/fm-045` f35b4d12b1, `notes/task-045-regarding-propagation-design.md`. The owner chose "follow recommendation" for Q1–Q12:
- **Q1 (a):** the 18 tables that carry the full pair.
- **Q2 (a):** the id is filled from the agreement columns, never from the stamp columns, and never from mapped lookups.
- **Q3 (a):** a warning on clear only.
- **Q4 (b):** updates keep the pair in step with a moved typed lookup, on all pair tables.
- **Q5 (a):** an update reports a clear by log event; the response is unchanged.
- **Q6 (c):** id-only pairs: only the reply copy is fixed.
- **Q7:** display columns are re-derived on a move (b), and cleared otherwise (a). Renames go to the 052 report.
- **Q8 (a):** the invoice family is re-derived on the row's own core-lookup change.
- **Q9 (a):** direct writes are covered by the 052 report, Repair and column audit.
- **Q10 (a):** the connections control's unlink is left as is.
- **Q11 (a):** no change to the timing windows.
- **Q12 (a):** the split is 045a (create step and guard 1), 045b (update planner and wiring, guard 2), 045c (invoice re-derivation, missing display columns, reply type, client number clear), plus a 052 report line.

UAC-r2 (2026-11-07): the playbook Update Record and output-mapping paths run as the user, or app-only behind authoring controls. 045b adds no AppendTo check there.

## 2026-11-08 — Owner decisions on 032, the repair pass, inbound mapping, checks and NFR-02 (in fm's session)

- **032: option B.** Extracted invoices get Matter→Invoice mapping, including the access-conferring Assigned Attorney and Paralegal columns, with the matching access grant. The work is coordinated with ebilling-r1. ebilling-r1's answers (2026-11-08):
  - nothing reads the invoice-level copies, now or planned, so the grant may be queued; no ebilling decision may ever read them;
  - build on `work/ebilling-r1` after its task 082d, in a separate task branch; it reaches master in ebilling-r1's PR;
  - mechanism: a post-commit, resumable step in `InvoiceSubmissionAfterSteps` that writes the grant first, then the fill-only columns.

  Next: a design note (`notes/task-032b-design.md`, on `task/fm-032b-design`), reviewed by ebilling-r1 and UAC-r2 together. UAC-r2 signs off the grant path, and answers the re-file question (revoke or keep).
- **Repair pass (052):** task and account are excluded. A contact must be owned by the customer's main business-unit team.
  - Today, a contact created in the model-driven form is owned by the creating user. Dataverse's default applies, and no server code runs on that create (ADR-002: no plugins).
  - BFF chat creates use the resolver's team.
  - Nothing re-owns contacts after create. On dev, contacts were moved to the "Spaarke Business Unit 1" team by a manual re-own on 2026-11-02.
  - Proposal: a contact owner-only rule in 052b's pass. It finds the main team per environment, and refuses and logs if it can't, never falling back to the creator. No field mapping on contacts. UAC-r2 reviews it.
  - Open: which team is "main". The root BU "Spaarke"'s team, or a configured team?
- **Inbound mapping:** recorded as a known gap, DI-022 (#2633).
- **Checks:** (a) PASS. In place of (c), the owner hit a push-preview defect: a table with no profile failed the whole preview. It is fixed in scope as task 076c (`task-040-notes.md`).
- **NFR-02:** the dev r3 run (`task-021-live.md`) shows a p95 delta of +1288 ms. The owner moved the verdict to UAT: "we'll test this in UAT". No 049d work starts on the dev result.

## 2026-11-08 — 032b design reviews; owner on O1/O2 and the contact team (in fm's session)

Design note: `task/fm-032b-design` 2692910f2d (`notes/task-032b-design.md`).
- **ebilling-r1 agrees to E1–E6.**
  - **E4 condition:** the admin note uses the existing admin-attention writer, stays within the 500-character cap, and writes no status and no other column.
  - **E6 condition:** sprk_description and sprk_highpriority never join the external invoice ReadableColumns or the firm-facing rendering without both projects' review.
  - **Base:** 082d is merged into work/ebilling-r1 (7cd62a1710; tip d58e3a3554).
- **UAC-r2 approves, with two conditions.**
  - **C1:** the job payload pins the core (table, id) from the persister's committed result. Gate 2 requires invoice core == payload core == submission core, plus a must-fire re-point test.
  - **C2:** settle U3 before 032b-2. Enumerate who resolves membership for sprk_invoice.
    - If no caller does, the four assignee copies confer nothing. sprk_invoice leaves the registry, the copies become display copies, and the grant machinery goes.
    - If any caller does, the copies are cleared on a core change and on Revoked/Declined.
  - **U1, U2, U4, U5:** approved.
- **Owner, O2 (move the mapping gates into a shared class):** approved. The owner also asked which other create processes need the same gates. An inventory of no-caller creators is in progress.
- **Owner, O1 (after-save job vs inline):** not decided. The owner asked:
  - what the implications are when a process fires on the field values (a form or server trigger);
  - and stated that this must also hold for other record types that inherit the sprk_assigned* values.

  The design note's round 2 answers this with a consumer inventory. It also evaluates inline-before-the-transaction if U3 lands on (c).
- **Owner, contact owner team:** an environment setting is acceptable. First check whether a shared mechanism already exists, and use one consistent process rather than a parallel approach. An investigation is in progress.
- **032b round 2 (9083f5df93), UAC-r2: approved (2026-11-08).**
  - **U3 → (c), confirmed.** Re-checked on master and on work/ebilling-r1: the only rights-granting membership callers are asked for project, matter and work assignment only, so the sprk_invoice registry entry grants nothing. Dev has no app-setting override of the registry.
  - **U7 accepted** from the access side. It is a visible product change (Daily Briefing stops selecting invoices through these columns), so the owner decides.
  - **E7:** no further OK needed under (c), provided every gate still runs before any value is read.
  - **Inline placement:** fine. C1 holds by construction.
  - **Registry removal:** done in fm's branch with UAC-r2's review, landing before or with 032b-2, together with the guard. The PR text must state the push side effect: the four columns become one-time and are never re-pushed.
- **032b round 2, ebilling-r1: confirmed (2026-11-08).**
  - **Inline placement:** in `PersistCoreAsync`, after `FillInvoice` and before `BuildInvoiceCreateRequest`. Values go in request 0, there is one re-commit without them, and a resume never maps. The §7 constraint is accepted.
  - **Conditions for 032b-2:**
    - Branch from the CURRENT origin/work/ebilling-r1 tip (verify with `merge-base --is-ancestor`), and re-anchor line numbers.
    - Add 082d's snapshot tests (`ProcedureSnapshot*`, the add-line and correction round trips from e552c406f8) to the regression list. `sprk_findingsjson` must survive the re-commit rebuild byte for byte.
    - T-E7 asserts that `sprk_findingsjson` is identical to an unmapped run.
    - Send ebilling-r1 the diff before it merges into their work branch.
  - **Owner decisions still open:** O1 (both reviewers prefer inline) and U7 (Daily Briefing).
- **Owner (2026-11-08): O1 inline placement: yes. U7 (Daily Briefing stops selecting invoices through the copied assignee columns): accepted.** With O2 approved earlier, the 032b design (9083f5df93) is final.
  - 032b-1 (fm branch: shared gates, registry removal with its guard, inventory of no-caller creators) merges to master first.
  - Then 032b-2 and 032b-3 run on the current work/ebilling-r1.
- **Contact owner team:** reuse `ExternalAccess:CustomerBusinessUnitId`, which resolves the BU's single default Owner team. It already sets the owner for the four identity-contact creates and fails closed when unset or ambiguous. The owner did not object, so it applies to the repair pass's contact rule (052b) and to the chat contact create. UAC-r2 reviews.

## 2026-11-08 — Task 060: ownership matrix; outbox and gate-record exceptions kept

- §5.2 of `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` holds the per-table ownership matrix. It has 25 rows, one per `RecordTableRules.All` entry, and I-6 points to it.
- **Notification outbox: keep.** Recipient-owned rows are the BFF's per-user delivery key (app-only reads filtered to the caller; no target-user parameter). No Spaarke role holds any privilege on the table, and the row is filed under nothing.
- **`DecisionRecordWriter` gate record about an unfiled communication: keep.** It is owned by the communication's own owning user and shared Read with its internal sharees (D-144 as amended, D-169). Its readers follow the communication's. It is secure-if-any first and fails closed without a readable, active user owner.
- Contact row: today's two rules, plus "052b, pending" (every contact to the `ExternalAccess:CustomerBusinessUnitId` team).
- Found in passing: row I-18 is stale (the Signal owner is now the resolver's team): DI-023, #2636.

## 2026-11-08 — 032b-1 review by UAC-r2; invoice Assigned To targeting (owner, in fm's session); mapping-source rule

- **UAC-r2 approved 032b-1 on the access side** (63fb6ce98e, 79b1aab39c, 4d24082a95). `AssignedAccessMaterializer.RegistryFor` grants on root types only, so removing the invoice entry revokes nothing.
- **Premise correction (UAC-r2 F1):** Office quick-create writes the invoice's Assigned To to `sprk_assignedto1` (`OfficeService.InvoiceAssignedToAttribute`). U7 was accepted on the belief that nothing writes it. The `MembershipOptions` comment now states the Office writer.
- **Owner decision (corrected U7): invoice Assigned To targets people without conferring access.** Daily Briefing and notifications also read the invoice's `sprk_assignedto1/2`, outside the access-conferring registry. No new grant path; the registry/display-copy guard still holds. Keeps the standing rule "notifications target Created By and Assigned To". Built as task 032b-1b.
- **Mapping-source rule (UAC-r2, 2026-11-08):** a source record that is secure (the secure-if-any answer the 032b gate uses; one predicate) maps only into a create that is secure from creation. Otherwise the create maps nothing and returns a warning with a stable code; a source whose answer faults maps nothing. Enforced in the shared `MapFieldsAsync`, so every create that maps on the server gets it. The Event, To Do, Invoice, Work Assignment and Report Card wizards still map in the browser until tasks 023–027 move them onto the server route (FR-22); until then this rule does not apply to their mapped values. The Project wizard preview turns Secure on and locks it when the chosen Matter is secure (a project under a secure parent is secure). Built in task 022's fix round 2.

## 2026-11-09 — Direct message audience (owner)

- Option A approved: a Direct message records both participants itself, `sprk_sentby` and a new `sprk_directrecipient` (lookup to systemuser), both stamped by the BFF at create and secured in 054b together with the message's thread lookup. The audience UAC-r2 task 198 grants to is exactly that pair; it is never read from the thread. Option C (the Direct team owns threads) was rejected: "threads do not move" stands. Detail in `tasks/054-direct-message-owner-team.poml`.

## 2026-11-10 — Outbound Direct messages go through the pair thread (owner)

- Option (a) approved: an outbound Direct message (a Message send with no `ThreadId` and no record filing) whose addressees resolve on the server to exactly one internal, enabled, non-application user other than the sender is filed into the Direct thread of that pair (`IDirectThreadAccessService.FindOrCreateDirectThreadAsync(sender, recipient)`), so piece 2 stamps `sprk_sentby` + `sprk_directrecipient` on it. Recipient resolution compares systemuser ids only. Any other send keeps today's path and gets no recipient (fail closed). Inbound stays fail-closed. UAC-r2 reviews the change (it changes how Direct threads are created and shared). Option (b), defer, was rejected. Detail: `notes/task-054-notes.md`, "054a piece 2b".

## 2026-11-11 — 054b and piece 3 (owner)

- **054b rule 2 guard: strict pair.** Filing never anchors a Direct-typed thread to a record when it is a strict Direct pair (`DirectMessageRecipient.ReadStrictPairShapeAsync`) or any of its messages carries `sprk_directrecipient`; a read fault means no anchor. Other Direct-typed threads (inbound email threads) still anchor, as decided 2026-10-11. A type-only guard was rejected because a thread created without a record is typed Direct.
- **Piece 3 while the Direct settings are unset: send normally.** While both `DirectMessage:BusinessUnitName` and `DirectMessage:OwnerTeamName` are unset on an environment, the Direct owner rule is off: Direct messages keep today's owner and nothing is refused. Once the settings are set, a Direct message with a valid stamped pair is owned by the Direct team; a configuration that is set but invalid (one setting missing, an ambiguous name, a team with members, a business unit with users, the root or Secure unit) refuses a send with 409 before anything is sent, and after the row exists keeps today's owner and logs the Error (AC2). This refines AC2: "unset" is off, not invalid. Lets a BFF with piece 3 deploy before an environment's Direct setup.

### 2026-11-11 (later) — piece 3 split and its open points

- **Split.** Piece 3 runs as 3a (lookup state + the one owner step on the three join sites + pre-send refusal + source-scan ArchTest), then 3b (unfile direction), then 3c (registry/census/guard updates + notes). Reason: one agent could not hold it, and 3b needed UAC-r2's answer.
- **Pre-send refusal scope (owner).** Only a 1:1 Message send (exactly one other person, no record filing) is refused when the Direct settings are set but invalid. Group, record-filed and email sends go out as today.
- **Transient lookup fault before a 1:1 send (owner).** 503, "try again"; nothing is sent. 409 is kept for a configuration problem an admin must fix.
- **Only one setting set (UAC-r2 agreed).** A new `PartiallyConfigured` refusal inside `DirectMessageOwnerTeam` (one lookup for the BFF and 198). `IsNotConfigured` only when BOTH are unset (whitespace = unset). Stable code `partially_configured` naming the missing setting by name. 198 then reads it as unknown (nothing granted or revoked; run status TeamUnknown with a Warning). Must-fire tests in 198's test files are named edits by this project; 198's production code is unchanged.
- **Unfile cascade (UAC-r2 agreed, named edit by this project, reviewed by UAC-r2).** In `SecureChildReconciler` a content row follows its message to the Direct team only when the message's owner is the RESOLVED Direct team (ids, non-null). Secure-owned and ordinary messages keep today's behaviour. Lookup unknown/faulted/partial → no owner write (no KeepCreator fallback), logged with a stable event code; the next reconcile retries.

### 2026-11-11 (later) — piece 3b, the unfile direction (owner)

- **Unfile when the Direct team cannot be confirmed: refuse it.** For an eligible Direct message (valid stamped pair, thread typed Direct and not the default thread), an unfile whose lookup does not resolve is refused before anything is written: 409 for a configuration problem, 503 for a fault. Nothing changes and the user can retry. `NotConfigured` keeps today's behaviour. This is the same rule as the 1:1 send. If the lookup resolves but the owner write then fails, the error is logged and the 054c script is the recovery.
- **Every unfile path follows the same rule.** The product re-file (`OwnedChildWrite.RefileAsync`), the generic record update, the AI Update Record action and the field-mapping push all use 3b's one unfile rule for a communication, so an unfile behaves the same whichever path does it.
- **Scope note from the 3b scoping pass.** `IncomingAssociationResolver` is not an unfile site (a create question, and additive filings only). The `SecureChildReconciler` cascade needs no edit: a message's content rows have the message as their parent, so 3a's resolver rule answers for them. On an unfile from an ordinary record the 3b step moves the content rows itself; from a Secure record it leaves them to the reconciler pass, which also removes the secure mirror.

### 2026-11-12 — piece 3b review outcome (UAC-r2, owner)

- **#2722 decided (owner, via UAC-r2).** An unfiled communication that is not an eligible Direct message moves to its creator's business-unit team (D-11), except one that left a Secure or Restricted record, which keeps its former owner. 3b keeps Unchanged for it. UAC-r2 builds the rule as a follow-up after 3b reaches master, in the single decision point `RecordOwnershipResolver.NonDirectUnfileAnswer` (named in 3b for this), so there is one writer of that file at a time.
- **Documents linked through `sprk_relatedcommunication` (UAC-r2, #2728).** A document whose only link to a Direct message is `sprk_relatedcommunication` keeps its owner when the message is unfiled. Making it follow its communication as an ownership parent is UAC-r2's follow-up, together with #2722. 3b does not move it (a second writer of the secure mirror was rejected). Recorded as a 3c known limit.
- **3b approved by UAC-r2** (including the F3 `leavesSecureOwner` change and the guard), merged `--no-ff` onto the work branch.

### 2026-11-12 — task 022b: Restricted mapping source (UAC-r2, round 75; from rounds 2, 67, 81, 84, 87)

- **Agreement:** `sprk_agreement.sprk_accesspermission` is declared in the SOURCE set only, so the Restricted-source check reads it and fails closed. No inheritance or enforcement on agreements. `AccessPermissionTablesGuardTests` holds the code set equal to the solution with no ratchet.
- **Project and work assignment:** the root's value is set in the create from `SecureRootInheritance`'s own decision, unchanged: the value max(own, floor) AND the access record marking the floor part inherited and the caller's value own. The max is never written as the record's own value (round 87: only inherited strictness follows a parent down). The post-create step stays and agrees with the create. No second writer.
- **Readings confirmed:** a Standard or Limited source maps unchanged (secure sources stay with the secure copy gate); an empty value is Standard; an unreadable value fails closed; the filing refusal reuses `record_creation.mapping_source_secure_filing` with Restricted wording (no client contract change).
- **A document filed only through the regarding pair:** a known limit. The strip fails closed; a document's access permission is a display copy with no enforcement of its own (round 81), and the reconcile brings it in step.
- **Repair and the field-mapping push:** task 022c (#2738, DI-027), started right after 022b merges, reviewed by UAC-r2.
