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
