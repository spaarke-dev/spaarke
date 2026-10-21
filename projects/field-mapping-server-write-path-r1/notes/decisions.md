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
